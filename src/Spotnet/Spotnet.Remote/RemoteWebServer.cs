using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NLog;

namespace Spotnet.Remote;

/// <summary>
/// Hostgegevens die de Kestrel-routes nodig hebben: de /status-endpoint en de
/// discovery-payload vullen zich hieruit. Windows leest dit uit AppHelper en
/// Settings.Default, macOS uit zijn eigen voorkeuren- en databaseservice.
/// </summary>
public interface IRemoteHostInfo
{
    string GetVersion();
    string GetProviderName();
    long GetTotalSpotsInDb();
    bool IsSyncing { get; }
    string GetNickname();
}

/// <summary>
/// De spots-catalog achter de /spots- en /favorites-endpoints: query, detail,
/// omschrijving/afbeelding en reacties. De implementatie leest de eigen
/// spots-database van het platform; het DTO-contract is gedeeld.
/// </summary>
public interface IRemoteSpotCatalog
{
    IReadOnlyList<FilterDto> GetFilters();
    IReadOnlyList<SpotDto> GetSpots(string query, int? category, string filterId, int page, int pageSize, string sort);
    SpotDetailDto GetSpotDetail(long id);
    byte[] GetSpotImage(long id, string messageId);
    IReadOnlyList<SpotCommentDto> GetSpotComments(long id, string messageId);
    (bool success, string error, SpotCommentDto comment) PostComment(long id, string messageId, string nickname, string body);
    void ToggleFavorite(string messageId, bool favorite);
    IReadOnlyList<SpotDto> GetFavorites(int page, int pageSize);
}

/// <summary>
/// De meldingen achter de /notifications-endpoints, zoals Windows ze uit de
/// NotificationEngine leest. Een null-provider is toegestaan; de endpoints
/// antwoorden dan 503.
/// </summary>
public interface IRemoteNotifications
{
    NotificationsResponseDto GetNotifications();
    int MarkAsRead(string id);
    int MarkAllAsRead();
    int DeleteNotification(string id);
    int ClearAll();
}

/// <summary>
/// De downloadwachtrij achter /queue en de downloadknop. Een null-provider is
/// toegestaan; de bijbehorende endpoints antwoorden dan 503.
/// </summary>
public interface IRemoteDownloadQueue
{
    QueueStatusDto GetQueue();
    /// <summary>Zet een spot in de wachtrij. Geeft succes en een evt. foutmelding terug.</summary>
    (bool success, string error) EnqueueSpot(long id, string messageId);
    bool PauseItem(string id);
    bool ResumeItem(string id);
    bool CancelItem(string id);
    bool SetSpeedLimit(int kbps);
}

/// <summary>
/// De ASP.NET Core-host van Spotnet Remote, gedeeld door beide clients. De
/// Kestrel-opzet, het CORS-beleid, de auth-middleware en alle /api/v1-routes
/// staan hier; het platform levert alleen de providers eromheen. Het
/// endpoint-aanbod is identiek aan Windows' RemoteServer, zodat de Android-app
/// geen onderscheid ziet.
/// </summary>
public class RemoteWebServer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly RemoteAuthManager _auth = RemoteAuthManager.Instance;
    private WebApplication _app;
    private CancellationTokenSource _cts;
    private readonly object _lock = new object();
    private DateTime _lastActivityUtc = DateTime.MinValue;
    private string _lastActiveClientName = "";
    private readonly object _activityLock = new object();

    private RemoteConfig _config;

    public event Action StatusChanged;

    public bool IsRunning { get; private set; }
    public int ActivePort { get; private set; } = 8770;

    /// <summary>Het map waarin de web-shell (index.html, app.js) staat.</summary>
    public string WebRootOverride { get; set; } = "";

    public IRemoteHostInfo HostInfo { get; set; }
    public IRemoteSpotCatalog Catalog { get; set; }
    public IRemoteDownloadQueue Queue { get; set; }
    public IRemoteNotifications Notifications { get; set; }

    /// <summary>Start "nieuwe spots ophalen" op de host; null als dat niet kan.</summary>
    public Func<SyncStatusDto> SyncTrigger { get; set; }

    /// <summary>
    /// Slaapvoorkoming in het platform: Windows roept SetThreadExecutionState aan,
    /// macOS start/stoppt caffeinate. Zelfde vorm als SleepPreventer.UpdateState.
    /// </summary>
    public Action<bool> SleepPreventer { get; set; } = _ => { };

    public bool IsClientActive
    {
        get
        {
            if (!IsRunning) return false;
            lock (_activityLock)
            {
                return (DateTime.UtcNow - _lastActivityUtc).TotalSeconds < 45;
            }
        }
    }

    public DateTime LastActivityUtc
    {
        get
        {
            lock (_activityLock) return _lastActivityUtc;
        }
    }

    public string LastActiveClientName
    {
        get
        {
            lock (_activityLock) return _lastActiveClientName;
        }
    }

    public void RegisterClientActivity(string clientName = "")
    {
        lock (_activityLock)
        {
            _lastActivityUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                _lastActiveClientName = clientName;
            }
        }
        StatusChanged?.Invoke();
    }

    /// <summary>De inlog-/koppelgegevens, voor het instellingenvenster en de koppeling.</summary>
    public RemoteAuthManager Auth => _auth;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _config = _auth.Config ?? RemoteConfig.Load();
            _auth.Config = _config;
            if (!_config.Enabled)
            {
                Log.Info("Spotnet Remote is disabled in settings.");
                return;
            }

            ActivePort = _config.Port > 0 ? _config.Port : 8770;
            _cts = new CancellationTokenSource();

            try
            {
                var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });

                builder.Logging.ClearProviders();

                builder.WebHost.UseKestrel(options =>
                {
                    if (_config.AllowLan)
                    {
                        options.Listen(IPAddress.Any, ActivePort);
                    }
                    else
                    {
                        options.Listen(IPAddress.Loopback, ActivePort);
                    }
                });

                builder.Services.AddRouting();
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
                });

                var app = builder.Build();
                _app = app;

                app.UseCors();

                string webRoot = ResolveWebRoot();
                if (Directory.Exists(webRoot))
                {
                    var fileProvider = new PhysicalFileProvider(webRoot);
                    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = fileProvider,
                        ServeUnknownFileTypes = true
                    });
                }

                MapApiRoutes(app);

                app.MapFallback(async (HttpContext context) =>
                {
                    string indexPath = Path.Combine(webRoot, "index.html");
                    if (File.Exists(indexPath))
                    {
                        context.Response.ContentType = "text/html; charset=utf-8";
                        await context.Response.SendFileAsync(indexPath);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("Spotnet Remote Web Shell not found.");
                    }
                });

                app.StartAsync(_cts.Token).GetAwaiter().GetResult();
                IsRunning = true;
                if (_config.KeepAwake)
                {
                    SleepPreventer(true);
                }
                if (_config.AllowLan)
                {
                    RemoteDiscoveryService.Instance.Start(ActivePort, _config.RequireAuth);
                }
                StatusChanged?.Invoke();
                Log.Info("Spotnet Remote Host started on port {0} (LAN={1}, KeepAwake={2})",
                    ActivePort, _config.AllowLan, _config.KeepAwake);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                _app = null;
                _cts = null;
                SleepPreventer(false);
                StatusChanged?.Invoke();
                Log.Error("Remote Host error: {0}", ex.Message);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning && _app == null) return;
            try
            {
                _cts?.Cancel();
                if (_app != null)
                {
                    _app.StopAsync().Wait(TimeSpan.FromSeconds(2));
                    _app.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Error while stopping Remote Host: {0}", ex.Message);
            }
            finally
            {
                RemoteDiscoveryService.Instance.Stop();
                SleepPreventer(false);
                IsRunning = false;
                _app = null;
                _cts = null;
                StatusChanged?.Invoke();
                Log.Info("Spotnet Remote Host stopped.");
            }
        }
    }

    public void Restart()
    {
        Stop();
        Thread.Sleep(300);
        Start();
    }

    // ── Routes ────────────────────────────────────────────────────────────────
    // Eén-op-één de endpoints van Windows' RemoteServer; waar een provider ontbreekt
    // antwoordt het endpoint 503 in plaats van te crashen.

    private void MapApiRoutes(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/auth/login", async (HttpContext ctx, LoginRequestDto req) =>
        {
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var res = await _auth.TryLoginAsync(req, clientIp);
            if (res.Success)
            {
                RegisterClientActivity(res.Username ?? clientIp);
                return Results.Json(res);
            }
            if (res.ErrorMessage != null && res.ErrorMessage.Contains("geblokkeerd"))
            {
                return Results.Json(res, statusCode: 429);
            }
            return Results.Json(res, statusCode: 401);
        });

        api.MapPost("/auth/pair", (HttpContext ctx, PairRequestDto req) =>
        {
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            RegisterClientActivity(req?.DeviceName ?? clientIp);
            var res = _auth.TryPair(req, clientIp);
            return Results.Json(res);
        });

        api.MapGet("/status", (HttpContext ctx) =>
        {
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            RegisterClientActivity(clientIp);
            var queue = Queue?.GetQueue();
            return Results.Json(new ServerStatusDto
            {
                Version = HostInfo?.GetVersion() ?? "3.0",
                IsReady = true,
                CurrentProvider = HostInfo?.GetProviderName() ?? "Usenet",
                TotalSpotsInDb = HostInfo?.GetTotalSpotsInDb() ?? 0,
                QueueCount = queue?.ActiveCount ?? 0,
                DownloadSpeed = 0,
                DownloadSpeedFormatted = queue?.OverallSpeedFormatted ?? "",
                PairedDevicesCount = _config.PairedDevices.Count,
                Port = ActivePort,
                LanEnabled = _config.AllowLan,
                IsSyncing = HostInfo?.IsSyncing ?? false,
                DefaultNickname = HostInfo?.GetNickname() ?? "",
                RequireAuth = _config.RequireAuth,
                HasPasswordAuth = !string.IsNullOrEmpty(_config.PasswordHash)
            });
        });

        var protectedGroup = api.MapGroup("");
        protectedGroup.AddEndpointFilter(async (invocationContext, next) =>
        {
            var ctx = invocationContext.HttpContext;
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";

            if (_config.RequireAuth)
            {
                string rawToken = "";
                if (ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    string h = authHeader.ToString();
                    if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        rawToken = h.Substring(7).Trim();
                    }
                }
                if (string.IsNullOrEmpty(rawToken) && ctx.Request.Query.TryGetValue("token", out var qToken))
                {
                    rawToken = qToken.ToString();
                }

                if (!_auth.ValidateToken(rawToken, clientIp, out var matchedDevice))
                {
                    // Afbeeldingen mogen ook vanaf een reeds gekoppeld IP zonder token.
                    if (ctx.Request.Path.Value?.EndsWith("/image", StringComparison.OrdinalIgnoreCase) == true
                        && _config.PairedDevices.Any(d => d.IpAddress == clientIp))
                    {
                        RegisterClientActivity(clientIp);
                    }
                    else
                    {
                        return Results.Unauthorized();
                    }
                }
                else
                {
                    RegisterClientActivity(matchedDevice?.Name ?? clientIp);
                }
            }
            else
            {
                RegisterClientActivity(clientIp);
            }
            return await next(invocationContext);
        });

        // Filters
        protectedGroup.MapGet("/filters", () =>
            Results.Json(Catalog?.GetFilters() ?? new List<FilterDto>()));

        // Spots-catalogus
        protectedGroup.MapGet("/spots", (string query, int? category, string filterId, int? page, int? pageSize, string sort) =>
        {
            if (Catalog == null) return ServiceUnavailable();
            return Results.Json(Catalog.GetSpots(query, category, filterId, page ?? 1, pageSize ?? 25, sort ?? "date_desc"));
        });

        protectedGroup.MapGet("/spots/{id:long}", (long id) =>
        {
            if (Catalog == null) return ServiceUnavailable();
            var detail = Catalog.GetSpotDetail(id);
            return detail == null ? Results.NotFound() : Results.Json(detail);
        });

        protectedGroup.MapGet("/spots/{id:long}/image", (long id, string messageId) =>
        {
            var bytes = Catalog?.GetSpotImage(id, messageId);
            return bytes == null || bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "image/jpeg");
        });

        protectedGroup.MapGet("/spots/{id:long}/comments", (long id, string messageId) =>
            Results.Json(Catalog?.GetSpotComments(id, messageId) ?? new List<SpotCommentDto>()));

        protectedGroup.MapPost("/spots/{id:long}/comments", (long id, PostCommentRequestDto req) =>
        {
            if (Catalog == null) return ServiceUnavailable();
            if (req == null || string.IsNullOrWhiteSpace(req.Body))
            {
                return Results.BadRequest(new { error = "Reactie mag niet leeg zijn." });
            }
            var result = Catalog.PostComment(id, null, req.Nickname, req.Body);
            if (!result.success)
            {
                return Results.BadRequest(new { error = result.error });
            }
            return Results.Json(result.comment);
        });

        // Sync
        protectedGroup.MapPost("/spots/sync", () =>
        {
            if (HostInfo != null && HostInfo.IsSyncing)
            {
                return Results.Json(new SyncStatusDto
                {
                    Success = true,
                    IsSyncing = true,
                    Message = "Spots worden momenteel al bijgewerkt..."
                });
            }
            if (SyncTrigger == null)
            {
                return Results.Json(new SyncStatusDto
                {
                    Success = false,
                    IsSyncing = false,
                    Message = "Synchroniseren is op deze host niet beschikbaar."
                });
            }
            return Results.Json(SyncTrigger());
        });

        // Downloaden
        protectedGroup.MapPost("/spots/{id:long}/download", async (HttpContext ctx, long id) =>
        {
            if (Queue == null) return ServiceUnavailable();
            DownloadRequestDto req = null;
            try
            {
                req = await ctx.Request.ReadFromJsonAsync<DownloadRequestDto>();
            }
            catch
            {
                // Een lege body is toegestaan; de spot-id volstaat.
            }
            var (success, error) = Queue.EnqueueSpot(id, req?.MessageId);
            return success ? Results.Json(new { success }) : Results.Json(new { success, error }, statusCode: 400);
        });

        // Favorieten
        protectedGroup.MapGet("/favorites", (int? page, int? pageSize) =>
            Results.Json(Catalog?.GetFavorites(page ?? 1, pageSize ?? 50) ?? new List<SpotDto>()));

        protectedGroup.MapPost("/favorites/{messageId}", (string messageId) =>
        {
            Catalog?.ToggleFavorite(messageId, true);
            return Results.Json(new { success = true });
        });

        protectedGroup.MapDelete("/favorites/{messageId}", (string messageId) =>
        {
            Catalog?.ToggleFavorite(messageId, false);
            return Results.Json(new { success = true });
        });

        // Wachtrij
        protectedGroup.MapGet("/queue", () =>
            Results.Json(Queue?.GetQueue() ?? new QueueStatusDto()));

        protectedGroup.MapPost("/queue/{id}/pause", (string id) =>
            Results.Json(new { success = Queue?.PauseItem(id) ?? false }));

        protectedGroup.MapPost("/queue/{id}/resume", (string id) =>
            Results.Json(new { success = Queue?.ResumeItem(id) ?? false }));

        protectedGroup.MapDelete("/queue/{id}", (string id) =>
            Results.Json(new { success = Queue?.CancelItem(id) ?? false }));

        protectedGroup.MapPost("/queue/speedlimit", (SpeedLimitDto req) =>
            Results.Json(new { success = Queue?.SetSpeedLimit(req?.Kbps ?? 0) ?? false }));

        // Apparaten
        protectedGroup.MapGet("/auth/devices", () => Results.Json(_config.PairedDevices));

        protectedGroup.MapDelete("/auth/devices/{deviceId}", (string deviceId) =>
            Results.Json(new { success = _auth.RevokeDevice(deviceId) }));

        // Meldingen
        protectedGroup.MapGet("/notifications", () =>
            Notifications == null ? ServiceUnavailable() : Results.Json(Notifications.GetNotifications()));

        protectedGroup.MapPost("/notifications/{id}/read", (string id) =>
            Results.Json(new { success = true, unreadCount = Notifications?.MarkAsRead(id) ?? 0 }));

        protectedGroup.MapPost("/notifications/read-all", () =>
            Results.Json(new { success = true, unreadCount = Notifications?.MarkAllAsRead() ?? 0 }));

        protectedGroup.MapDelete("/notifications/{id}", (string id) =>
            Results.Json(new { success = true, unreadCount = Notifications?.DeleteNotification(id) ?? 0 }));

        protectedGroup.MapDelete("/notifications", () =>
            Results.Json(new { success = true, unreadCount = Notifications?.ClearAll() ?? 0 }));
    }

    private static IResult ServiceUnavailable() =>
        Results.Json(new { error = "Deze host biedt deze dienst niet aan." }, statusCode: 503);

    private string ResolveWebRoot()
    {
        string[] candidates =
        {
            WebRootOverride,
            Path.Combine(AppContext.BaseDirectory, "Remote", "Web"),
            Path.Combine(AppContext.BaseDirectory, "Spotnet", "Remote", "Web"),
            Path.Combine(AppContext.BaseDirectory, "Web")
        };

        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && Directory.Exists(c) && File.Exists(Path.Combine(c, "index.html")))
            {
                return Path.GetFullPath(c);
            }
        }

        return "";
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // fallback
        }
        return "127.0.0.1";
    }

    public string GetRemoteUrl(bool useLanIp = true)
    {
        string host = useLanIp ? GetLocalIpAddress() : "127.0.0.1";
        return $"http://{host}:{ActivePort}";
    }
}
