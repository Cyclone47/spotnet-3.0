using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Spotnet.Mvvm.Threading;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Notifications;

namespace Spotnet.Remote;

public class RemoteServer
{
    private static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<RemoteServer> InstanceHolder = new Lazy<RemoteServer>(() => new RemoteServer());
    public static RemoteServer Instance => InstanceHolder.Value;

    private WebApplication _app;
    private CancellationTokenSource _cts;
    private readonly object _lock = new object();
    private DateTime _lastActivityUtc = DateTime.MinValue;
    private string _lastActiveClientName = "";
    private readonly object _activityLock = new object();

    public event Action StatusChanged;

    public bool IsRunning { get; private set; }
    public int ActivePort { get; private set; } = 8770;

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

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            var config = RemoteConfig.Load();
            if (!config.Enabled)
            {
                Log.Info("Spotnet Remote is disabled in settings.");
                return;
            }

            ActivePort = config.Port > 0 ? config.Port : 8770;
            _cts = new CancellationTokenSource();

            try
            {
                var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });

                // Configure logging to be minimal
                builder.Logging.ClearProviders();

                // Kestrel configuration
                builder.WebHost.UseKestrel(options =>
                {
                    if (config.AllowLan)
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

                // Determine Web folder location
                string webRoot = ResolveWebRoot();
                if (Directory.Exists(webRoot))
                {
                    var fileProvider = new PhysicalFileProvider(webRoot);
                    app.UseDefaultFiles(new DefaultFilesOptions
                    {
                        FileProvider = fileProvider
                    });
                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = fileProvider,
                        ServeUnknownFileTypes = true
                    });
                }

                // Setup API Routes
                MapApiRoutes(app, config);

                // Fallback to index.html for SPA routing
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
                if (config.KeepAwake)
                {
                    SleepPreventer.UpdateState(true);
                }
                if (config.AllowLan)
                {
                    RemoteDiscoveryService.Instance.Start(ActivePort, config.RequireAuth);
                }
                StatusChanged?.Invoke();
                Log.Info("Spotnet Remote Host started on port {0} (LAN={1}, KeepAwake={2})", ActivePort, config.AllowLan, config.KeepAwake);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                _app = null;
                _cts = null;
                SleepPreventer.UpdateState(false);
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
                SleepPreventer.UpdateState(false);
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

    private void MapApiRoutes(WebApplication app, RemoteConfig config)
    {
        var api = app.MapGroup("/api/v1");

        // Auth endpoint (no auth required)
        api.MapPost("/auth/login", async (HttpContext ctx, LoginRequestDto req) =>
        {
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var res = await RemoteAuthManager.Instance.TryLoginAsync(req, clientIp);
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
            var res = RemoteAuthManager.Instance.TryPair(req, clientIp);
            return Results.Json(res);
        });

        // Server status endpoint
        api.MapGet("/status", (HttpContext ctx) =>
        {
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            RegisterClientActivity(clientIp);
            var queue = RemoteQueueService.Instance.GetQueue();
            return Results.Json(new ServerStatusDto
            {
                Version = AppHelper.AppVersion?.ToString() ?? "3.0",
                IsReady = true,
                CurrentProvider = AppHelper.ServersDb?.ODown?.Server ?? "Usenet",
                TotalSpotsInDb = (long)Settings.Default.DatabaseFilter,
                QueueCount = queue.ActiveCount,
                DownloadSpeed = queue.OverallSpeedBytesPerSec,
                DownloadSpeedFormatted = queue.OverallSpeedFormatted,
                PairedDevicesCount = config.PairedDevices.Count,
                Port = ActivePort,
                LanEnabled = config.AllowLan,
                IsSyncing = DbUpdater.IsDbUpdateInProgress,
                DefaultNickname = Settings.Default.Nickname ?? "",
                RequireAuth = config.RequireAuth,
                HasPasswordAuth = !string.IsNullOrEmpty(config.PasswordHash)
            });
        });

        // Protected API endpoints
        var protectedGroup = api.MapGroup("");
        protectedGroup.AddEndpointFilter(async (invocationContext, next) =>
        {
            var ctx = invocationContext.HttpContext;
            string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";

            if (config.RequireAuth)
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

                if (!RemoteAuthManager.Instance.ValidateToken(rawToken, clientIp, out var matchedDevice))
                {
                    // Allow spot image requests if coming from an already paired device
                    if (ctx.Request.Path.Value?.EndsWith("/image", StringComparison.OrdinalIgnoreCase) == true
                        && config.PairedDevices.Any(d => d.IpAddress == clientIp))
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

        // Filters (synced with desktop)
        protectedGroup.MapGet("/filters", () =>
        {
            var filters = RemoteCatalogService.Instance.GetFilters();
            return Results.Json(filters);
        });

        // Spots Catalog
        protectedGroup.MapGet("/spots", (string query, int? category, string filterId, int? page, int? pageSize, string sort) =>
        {
            var spots = RemoteCatalogService.Instance.GetSpots(
                query,
                category,
                filterId,
                page ?? 1,
                pageSize ?? 25,
                sort ?? "date_desc"
            );
            return Results.Json(spots);
        });

        protectedGroup.MapGet("/spots/{id:long}", (long id) =>
        {
            var detail = RemoteCatalogService.Instance.GetSpotDetail(id);
            if (detail == null) return Results.NotFound();
            return Results.Json(detail);
        });

        protectedGroup.MapGet("/spots/{id:long}/image", (long id, string messageId) =>
        {
            var bytes = RemoteCatalogService.Instance.GetSpotImage(id, messageId);
            if (bytes == null || bytes.Length == 0)
            {
                return Results.NotFound();
            }
            return Results.File(bytes, "image/jpeg");
        });

        // Comments
        protectedGroup.MapGet("/spots/{id:long}/comments", (long id, string messageId) =>
        {
            var comments = RemoteCatalogService.Instance.GetSpotComments(id, messageId);
            return Results.Json(comments);
        });

        protectedGroup.MapPost("/spots/{id:long}/comments", (long id, PostCommentRequestDto req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Body))
            {
                return Results.BadRequest(new { error = "Reactie mag niet leeg zijn." });
            }

            var result = RemoteCatalogService.Instance.PostSpotComment(id, null, req.Nickname, req.Body);
            if (!result.success)
            {
                return Results.BadRequest(new { error = result.error });
            }
            return Results.Json(result.comment);
        });

        // Trigger New Spots Sync
        protectedGroup.MapPost("/spots/sync", () =>
        {
            bool isSyncing = DbUpdater.IsDbUpdateInProgress;
            if (isSyncing)
            {
                return Results.Json(new SyncStatusDto
                {
                    Success = true,
                    IsSyncing = true,
                    Message = "Spots worden momenteel al bijgewerkt..."
                });
            }

            DispatcherHelper.UIDispatcher.InvokeAsync(() =>
            {
                try
                {
                    Sys.MainWindow?.ScheduleDbUpdate();
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to trigger ScheduleDbUpdate from remote: {0}", ex.Message);
                }
            });

            return Results.Json(new SyncStatusDto
            {
                Success = true,
                IsSyncing = true,
                Message = "Nieuwe spots ophalen gestart op de PC!"
            });
        });

        // Enqueue Download
        protectedGroup.MapPost("/spots/{id:long}/download", async (long id, DownloadRequestDto req) =>
        {
            bool success = await RemoteQueueService.Instance.EnqueueSpotAsync(id, req?.MessageId);
            return Results.Json(new { success });
        });

        // Favorites
        protectedGroup.MapGet("/favorites", (int? page, int? pageSize) =>
        {
            var favs = RemoteCatalogService.Instance.GetFavorites(page ?? 1, pageSize ?? 50);
            return Results.Json(favs);
        });

        protectedGroup.MapPost("/favorites/{messageId}", (string messageId) =>
        {
            RemoteCatalogService.Instance.ToggleFavorite(messageId, true);
            return Results.Json(new { success = true });
        });

        protectedGroup.MapDelete("/favorites/{messageId}", (string messageId) =>
        {
            RemoteCatalogService.Instance.ToggleFavorite(messageId, false);
            return Results.Json(new { success = true });
        });

        // Download Queue
        protectedGroup.MapGet("/queue", () =>
        {
            var queue = RemoteQueueService.Instance.GetQueue();
            return Results.Json(queue);
        });

        protectedGroup.MapPost("/queue/{id}/pause", (string id) =>
        {
            bool success = RemoteQueueService.Instance.PauseItem(id);
            return Results.Json(new { success });
        });

        protectedGroup.MapPost("/queue/{id}/resume", (string id) =>
        {
            bool success = RemoteQueueService.Instance.ResumeItem(id);
            return Results.Json(new { success });
        });

        protectedGroup.MapDelete("/queue/{id}", (string id) =>
        {
            bool success = RemoteQueueService.Instance.CancelItem(id);
            return Results.Json(new { success });
        });

        protectedGroup.MapPost("/queue/speedlimit", (SpeedLimitDto req) =>
        {
            bool success = RemoteQueueService.Instance.SetSpeedLimit(req?.Kbps ?? 0);
            return Results.Json(new { success });
        });

        // Device Management
        protectedGroup.MapGet("/auth/devices", () =>
        {
            return Results.Json(config.PairedDevices);
        });

        protectedGroup.MapDelete("/auth/devices/{deviceId}", (string deviceId) =>
        {
            bool success = RemoteAuthManager.Instance.RevokeDevice(deviceId);
            return Results.Json(new { success });
        });

        // Notifications
        protectedGroup.MapGet("/notifications", () =>
        {
            var cfg = NotificationManager.Instance.Config;
            var notifs = cfg.Notifications.Select(n => new NotificationItemDto
            {
                Id = n.Id,
                RuleId = n.RuleId,
                RuleName = n.RuleName,
                RuleType = n.RuleType.ToString(),
                Title = n.Title,
                Body = n.Body,
                SpotCount = n.SpotCount,
                TimeAgo = n.TimeAgo,
                CreatedAtUtc = n.CreatedAtUtc,
                IsRead = n.IsRead,
                Spots = n.Spots.Select(s => new NotificationSpotDto
                {
                    Id = s.Id,
                    MessageId = s.MessageId,
                    Title = s.Title,
                    Category = s.Category,
                    CategoryName = s.CategoryName,
                    FormattedSize = s.FormattedSize,
                    FormattedDate = s.FormattedDate
                }).ToList()
            }).ToList();

            return Results.Json(new NotificationsResponseDto
            {
                UnreadCount = NotificationManager.Instance.UnreadCount,
                Notifications = notifs
            });
        });

        protectedGroup.MapPost("/notifications/{id}/read", (string id) =>
        {
            NotificationManager.Instance.MarkAsRead(id);
            return Results.Json(new { success = true, unreadCount = NotificationManager.Instance.UnreadCount });
        });

        protectedGroup.MapPost("/notifications/read-all", () =>
        {
            NotificationManager.Instance.MarkAllAsRead();
            return Results.Json(new { success = true, unreadCount = 0 });
        });

        protectedGroup.MapDelete("/notifications/{id}", (string id) =>
        {
            NotificationManager.Instance.DeleteNotification(id);
            return Results.Json(new { success = true, unreadCount = NotificationManager.Instance.UnreadCount });
        });

        protectedGroup.MapDelete("/notifications", () =>
        {
            NotificationManager.Instance.ClearAllNotifications();
            return Results.Json(new { success = true, unreadCount = 0 });
        });
    }

    private string ResolveWebRoot()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spotnet", "Remote", "Web"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Spotnet", "Remote", "Web")
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "index.html")))
            {
                return Path.GetFullPath(c);
            }
        }

        // Fallback: create Web directory if missing
        string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }
        return defaultPath;
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

public class DownloadRequestDto
{
    public string MessageId { get; set; } = "";
}

public class SpeedLimitDto
{
    public int Kbps { get; set; }
}
