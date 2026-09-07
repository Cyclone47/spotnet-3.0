using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.PostProcessing;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Remote;

namespace Spotnet.Mac.Remote;

/// <summary>
/// De macOS-host van Spotnet Remote: vult de gedeelde <see cref="RemoteWebServer"/>
/// met providers die tegen de eigen database, downloader en voorkeuren praten —
/// het Mac-antwoord op Windows' RemoteServer/RemoteCatalogService/RemoteQueueService.
/// Het /api/v1-contract komt daardoor één-op-één overeen met de Windows-client, zodat
/// de Android-app geen onderscheid ziet.
/// </summary>
public sealed class MacRemoteHost : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly MainWindowViewModel _main;
    private readonly UserPreferencesService _prefsService;
    private readonly RemoteWebServer _server;
    private readonly MacSleepPreventer _sleepPreventer = new();
    private CancellationTokenSource? _syncCts;

    public MacRemoteHost(MainWindowViewModel main, UserPreferencesService prefsService, string webRootOverride = "")
    {
        _main = main;
        _prefsService = prefsService;

        // remote_config.json in dezelfde map als preferences.json, zoals Windows het
        // in zijn instellingenmap (AppHelper.SettingsFolder) bewaart.
        RemoteConfig.ConfigPathProvider = () =>
            Path.Combine(prefsService.SettingsFolder, "remote_config.json");
        RemoteDiscoveryService.VersionProvider = () =>
            typeof(MacRemoteHost).Assembly.GetName().Version?.ToString() ?? "3.0";

        _server = new RemoteWebServer
        {
            WebRootOverride = webRootOverride,
            HostInfo = new MacRemoteHostInfo(main, prefsService),
            Catalog = new MacRemoteCatalog(main),
            Queue = new MacRemoteQueue(main),
            Notifications = new MacRemoteNotifications(main),
            SyncTrigger = TriggerSync,
            SleepPreventer = prevent => _sleepPreventer.UpdateState(prevent)
        };
    }

    public bool IsRunning => _server.IsRunning;
    public int Port => _server.ActivePort;

    public void Start() => _server.Start();
    public void Stop() => _server.Stop();
    public void Restart() => _server.Restart();

    /// <summary>Het koppelscherm: URL met token voor de QR en de bijbehorende PIN.</summary>
    public (string url, string pin, string token) CreatePairing()
    {
        var session = _server.Auth.CreatePairingSession();
        string url = $"{_server.GetRemoteUrl(useLanIp: true)}/?pairToken={session.Token}";
        return (url, session.Pin, session.Token);
    }

    public IReadOnlyList<PairedDevice> PairedDevices =>
        (_server.Auth.Config ?? RemoteConfig.Load()).PairedDevices;

    public void RevokeDevice(string deviceId) => _server.Auth.RevokeDevice(deviceId);

    public void Dispose()
    {
        _server.Stop();
        _syncCts?.Dispose();
    }

    /// <summary>
    /// "Nieuwe spots ophalen" vanaf de telefoon: dezelfde taken als de
    /// Vernieuwen-knop, op een eigen CancellationToken zodat het venster de
    /// sync niet blokkeert en een tweede aanvraag de vorige aflost.
    /// </summary>
    private SyncStatusDto TriggerSync()
    {
        if (_main.IsSyncing)
        {
            return new SyncStatusDto
            {
                Success = true,
                IsSyncing = true,
                Message = "Spots worden momenteel al bijgewerkt..."
            };
        }

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var token = _syncCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _main.RunRemoteSyncAsync(token);
            }
            catch (Exception ex)
            {
                Log.Warn("Remote sync failed: {0}", ex.Message);
            }
        }, CancellationToken.None);

        return new SyncStatusDto
        {
            Success = true,
            IsSyncing = true,
            Message = "Nieuwe spots ophalen gestart op de Mac!"
        };
    }
}

/// <summary>
/// Voorkomt dat de Mac in slaap valt zolang Spotnet Remote actief is en de
/// voorkeur "wakker houden" aanstaat: het macOS-antwoord op Windows'
/// SetThreadExecutionState — hier via <c>caffeinate -i -s</c>.
/// </summary>
public sealed class MacSleepPreventer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private System.Diagnostics.Process? _caffeinate;
    private readonly object _lock = new();

    public void UpdateState(bool preventSleep)
    {
        lock (_lock)
        {
            if (preventSleep)
            {
                if (_caffeinate != null) return;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/caffeinate",
                        // -i: geen idle-sleep, -s: ook niet op netvoeding.
                        Arguments = "-i -s",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    _caffeinate = System.Diagnostics.Process.Start(psi);
                    Log.Info("Slaapvoorkoming gestart (caffeinate).");
                }
                catch (Exception ex)
                {
                    Log.Warn("caffeinate starten mislukt: {0}", ex.Message);
                    _caffeinate = null;
                }
            }
            else
            {
                if (_caffeinate == null) return;
                try
                {
                    _caffeinate.Kill(true);
                    _caffeinate.Dispose();
                    Log.Info("Slaapvoorkoming gestopt.");
                }
                catch (Exception ex)
                {
                    Log.Debug("caffeinate stoppen mislukt (mogelijk al gestopt): {0}", ex.Message);
                }
                _caffeinate = null;
            }
        }
    }
}

/// <summary>
/// Korte cache voor spot-omschrijvingen en covers die de Remote-host van het
/// netwerk haalt: de telefoon vraagt detail en afbeelding in quick succession,
/// en Windows doet hetzelfde via zijn FileCacheManager. Process-breed zodat de
/// host hem ook na een herstart van de server behoudt.
/// </summary>
internal static class RemoteBodyCache
{
    private sealed record Entry(string? Description, byte[]? Image);
    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static (string? description, byte[]? image) GetOrFetch(
        MainWindowViewModel main, string msgId)
    {
        if (string.IsNullOrEmpty(msgId)) return (null, null);
        var entry = Cache.GetOrAdd(msgId, _ =>
        {
            try
            {
                var spot = main.DatabaseService.GetSpotByMsgIdAsync(msgId).GetAwaiter().GetResult();
                if (spot == null) return new Entry(null, null);
                var body = main.BodyService.FetchAsync(spot).GetAwaiter().GetResult();
                return body == null ? new Entry(null, null) : new Entry(body.Description, body.Image);
            }
            catch
            {
                return new Entry(null, null);
            }
        });
        return (entry.Description, entry.Image);
    }
}

/// <summary>/status en de discovery-payload.</summary>
public sealed class MacRemoteHostInfo : IRemoteHostInfo
{
    private readonly MainWindowViewModel _main;
    private readonly UserPreferencesService _prefs;

    public MacRemoteHostInfo(MainWindowViewModel main, UserPreferencesService prefs)
    {
        _main = main;
        _prefs = prefs;
    }

    public string GetVersion() =>
        typeof(MacRemoteHost).Assembly.GetName().Version?.ToString() ?? "3.0";

    public string GetProviderName() => _prefs.Current.SelectedProvider;

    public long GetTotalSpotsInDb()
    {
        var (_, _, count) = _main.DatabaseService.GetDatabaseStatsAsync().GetAwaiter().GetResult();
        return count;
    }

    public bool IsSyncing => _main.IsSyncing;

    public string GetNickname() => _prefs.Current.Nickname;
}

/// <summary>
/// De spots-catalogus, favorieten en reacties achter de /spots- en /favorites-
/// endpoints: gelezen uit de eigen SQLite-database met dezelfde query-semantiek
/// als de spotlijst zelf (zelfde filtertaal, blacklist- en eroticagebruik).
/// </summary>
public sealed class MacRemoteCatalog : IRemoteSpotCatalog
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly MainWindowViewModel _main;
    private readonly Dictionary<string, string> _filterQueries;

    public MacRemoteCatalog(MainWindowViewModel main)
    {
        _main = main;
        _filterQueries = main.FilterTree
            .SelectMany(f => new[] { f }.Concat(f.Children))
            .Where(f => !string.IsNullOrEmpty(f.Query))
            .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Query, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<FilterDto> GetFilters() => _main.FilterTree
        .Select(MapFilter)
        .Where(f => f != null)
        .Cast<FilterDto>()
        .ToList();

    private static FilterDto? MapFilter(FilterItem f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        Query = f.Query,
        Icon = f.Icon,
        Children = f.Children.Select(MapFilter).Where(c => c != null).Cast<FilterDto>().ToList()
    };

    public IReadOnlyList<SpotDto> GetSpots(string query, int? category, string filterId, int page, int pageSize, string sort)
    {
        var (filterQuery, favoritesOnly) = ResolveFilter(filterId, category);
        return QueryPage(filterQuery, query, favoritesOnly, page, pageSize, sort);
    }

    public IReadOnlyList<SpotDto> GetFavorites(int page, int pageSize) =>
        QueryPage(null, null, favoritesOnly: true, page, pageSize, "date_desc");

    public SpotDetailDto? GetSpotDetail(long id)
    {
        var spot = FindSpot(id).GetAwaiter().GetResult();
        if (spot == null) return null;

        var dto = MapSpot(spot);
        var (description, image) = RemoteBodyCache.GetOrFetch(_main, spot.MsgId);
        return new SpotDetailDto
        {
            Id = dto.Id,
            MessageId = dto.MessageId,
            Title = dto.Title,
            Poster = dto.Poster,
            Tag = dto.Tag,
            Category = dto.Category,
            CategoryName = dto.CategoryName,
            FileSize = dto.FileSize,
            FormattedSize = dto.FormattedSize,
            Date = dto.Date,
            FormattedDate = dto.FormattedDate,
            SpamReports = dto.SpamReports,
            IsFavorite = dto.IsFavorite,
            Description = string.IsNullOrEmpty(description) ? "" : Sanitize(description!),
            HasImage = image is { Length: > 0 },
            HasNzb = true,
            NntpGroup = "free.pt"
        };
    }

    public byte[]? GetSpotImage(long id, string messageId)
    {
        var spot = FindSpot(id, messageId).GetAwaiter().GetResult();
        if (spot == null) return null;
        var (_, image) = RemoteBodyCache.GetOrFetch(_main, spot.MsgId);
        return image;
    }

    public IReadOnlyList<SpotCommentDto> GetSpotComments(long id, string messageId)
    {
        var spot = FindSpot(id, messageId).GetAwaiter().GetResult();
        if (spot == null) return Array.Empty<SpotCommentDto>();
        var comments = _main.DatabaseService.GetCommentsAsync(spot.MsgId).GetAwaiter().GetResult();
        return comments.Select(c => new SpotCommentDto
        {
            Id = c.Id,
            SpotMessageId = c.SpotMsgId,
            Sender = c.Sender,
            DateFormatted = c.FormattedDate,
            RawBody = c.Body,
            BodyHtml = "<p>" + System.Net.WebUtility.HtmlEncode(c.DisplayBody).Replace("\n", "<br/>") + "</p>",
            IsVerified = !string.IsNullOrEmpty(c.Modulus)
        }).ToList();
    }

    public (bool success, string error, SpotCommentDto comment) PostComment(long id, string messageId, string nickname, string body)
    {
        var spot = FindSpot(id, messageId).GetAwaiter().GetResult();
        if (spot == null) return (false, "Spot niet gevonden.", new SpotCommentDto());
        var (success, comment, message) = _main.PostRemoteCommentAsync(spot, nickname, body).GetAwaiter().GetResult();
        if (!success) return (false, message, new SpotCommentDto());
        return (true, "", new SpotCommentDto
        {
            SpotMessageId = spot.MsgId,
            Sender = comment?.Sender ?? nickname,
            DateFormatted = comment?.FormattedDate ?? "",
            RawBody = body,
            BodyHtml = "<p>" + System.Net.WebUtility.HtmlEncode(body) + "</p>",
            IsVerified = true
        });
    }

    public void ToggleFavorite(string messageId, bool favorite)
    {
        var db = _main.DatabaseService;
        if (favorite) db.AddFavoriteAsync(messageId).GetAwaiter().GetResult();
        else db.RemoveFavoriteAsync(messageId).GetAwaiter().GetResult();
    }

    // ── gedeelde hulpjes ─────────────────────────────────────────────────────

    private (string? filterQuery, bool favoritesOnly) ResolveFilter(string? filterId, int? category)
    {
        bool favoritesOnly = false;
        string? filterQuery = null;

        if (!string.IsNullOrEmpty(filterId))
        {
            if (string.Equals(filterId, "def_Favorieten", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filterId, "favorites", StringComparison.OrdinalIgnoreCase))
            {
                favoritesOnly = true;
            }
            else if (_filterQueries.TryGetValue(filterId, out var q))
            {
                filterQuery = q;
            }
            else if (filterId.StartsWith("cat_", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(filterId.AsSpan(4), out int cat))
            {
                filterQuery = $"cat = {cat}";
            }
        }
        else if (category is > 0)
        {
            filterQuery = $"cat = {category}";
        }

        return (filterQuery, favoritesOnly);
    }

    private List<SpotDto> QueryPage(string? filterQuery, string? search, bool favoritesOnly, int page, int pageSize, string sort)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        if (page < 1) page = 1;
        var (dir, column) = MapSort(sort);

        try
        {
            var prefs = _main.PreferencesService.Current;
            var rows = _main.DatabaseService.QueryByFilterAsync(
                filterQuery: filterQuery,
                searchText: string.IsNullOrWhiteSpace(search) ? null : search,
                skip: (page - 1) * pageSize,
                take: pageSize,
                sortDirection: dir,
                sortColumn: column,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide,
                searchField: "subject",
                extensiveSearch: true,
                favoritesOnly: favoritesOnly).GetAwaiter().GetResult();

            return rows.Select(MapSpot).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Remote GetSpots query failed: {0}", ex.Message);
            return new List<SpotDto>();
        }
    }

    /// <summary>Zelfde sorteinsleutels als de Windows-Remote (/spots?sort=…).</summary>
    private static (string direction, string column) MapSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        "filesize_desc" => ("DESC", "FormattedSize"),
        "filesize_asc" => ("ASC", "FormattedSize"),
        "date_asc" => ("ASC", "Age"),
        "subject_asc" => ("ASC", "Subject"),
        _ => ("DESC", "Age")
    };

    private static SpotDto MapSpot(SpotItem s) => new()
    {
        Id = s.Id,
        MessageId = s.MsgId,
        Title = s.Subject,
        Poster = s.Sender,
        Tag = s.Tag,
        Category = s.Category,
        CategoryName = s.SpotnetCategoryName,
        FileSize = s.Filesize,
        FormattedSize = s.FormattedSize,
        Date = s.Date,
        FormattedDate = s.FormattedDate,
        SpamReports = s.NumberOfSpamReports,
        IsFavorite = s.IsFavorite
    };

    private Task<SpotItem?> FindSpot(long id, string? messageId = null)
        => _main.FindRemoteSpotAsync(id, messageId);

    /// <summary>Tekst naar afgeveegde HTML-alinea's; niets anders vertrouwd dan tekst,
    /// zoals Windows' SanitizeDescriptionToHtml dat ook alleen tekst overlaat.</summary>
    private static string Sanitize(string raw) =>
        "<p>" + System.Net.WebUtility.HtmlEncode(raw)
            .Replace("\r\n", "\n")
            .Replace("\n\n", "</p><p>")
            .Replace("\n", "<br/>") + "</p>";
}

/// <summary>
/// De downloadwachtrij achter /queue en de downloadknop: gekoppeld aan de
/// Downloads-tab, zodat de telefoon exact ziet wat de Mac zelf ziet.
/// </summary>
public sealed class MacRemoteQueue : IRemoteDownloadQueue
{
    private readonly MainWindowViewModel _main;

    public MacRemoteQueue(MainWindowViewModel main) => _main = main;

    public QueueStatusDto GetQueue()
    {
        var items = _main.DownloadsTab.Downloads;
        DownloadItem? active = items.FirstOrDefault(d => d.IsDownloading);
        return new QueueStatusDto
        {
            IsPaused = active?.IsPaused ?? false,
            OverallSpeedFormatted = active?.SpeedText ?? "0 B/s",
            OverallProgress = items.Count == 0 ? 0 : items.Average(d => d.Progress),
            ActiveCount = items.Count(d => !d.IsCompleted && !d.IsFailed),
            Items = items.Select(MapItem).ToList()
        };
    }

    public (bool success, string error) EnqueueSpot(long id, string? messageId)
    {
        var spot = _main.FindRemoteSpotAsync(id, messageId).GetAwaiter().GetResult();
        if (spot == null) return (false, "Spot niet gevonden (mogelijk buiten retentie).");
        var (success, _, message, _) = _main.DownloadRemoteSpotAsync(spot).GetAwaiter().GetResult();
        return (success, success ? "" : message);
    }

    public bool PauseItem(string id)
    {
        var item = FindItem(id);
        if (item == null || !item.IsDownloading || item.IsPaused) return false;
        item.IsPaused = true;
        item.PauseGate?.Reset();
        item.SetStage(DownloadStage.Paused);
        item.SpeedText = "0 B/s";
        return true;
    }

    public bool ResumeItem(string id)
    {
        var item = FindItem(id);
        if (item == null || !item.IsPaused) return false;
        item.IsPaused = false;
        item.PauseGate?.Set();
        item.SetStage(DownloadStage.Downloading, $"{item.ProgressPercent}%");
        return true;
    }

    public bool CancelItem(string id)
    {
        var item = FindItem(id);
        if (item == null) return false;
        if (item.JobCts != null && !item.JobCts.IsCancellationRequested)
        {
            item.JobCts.Cancel();
            item.PauseGate?.Set();
            item.SetStage(DownloadStage.Cancelled);
            item.IsDownloading = false;
            return true;
        }
        // Alleen nog in de historie: uit de lijst halen zoals de verwijderknop.
        return _main.RemoveRemoteDownload(item);
    }

    public bool SetSpeedLimit(int kbps)
    {
        DownloadSpeedLimiter.Shared.LimitKbps = kbps;
        return true;
    }

    private DownloadItem? FindItem(string id)
    {
        // Het dto-id is de positie in de lijst, zoals Windows dat ook teruggeeft.
        return int.TryParse(id, out int index) && index >= 0 && index < _main.DownloadsTab.Downloads.Count
            ? _main.DownloadsTab.Downloads[index]
            : null;
    }

    private static DownloadItemDto MapItem(DownloadItem d) => new()
    {
        Id = d.Index.ToString(),
        Title = d.Title,
        MessageId = d.MsgId,
        Status = d.Status,
        Progress = d.Progress,
        SpeedFormatted = d.SpeedText,
        TotalBytes = d.BytesTotal,
        DownloadedBytes = d.BytesDone,
        TotalSizeFormatted = d.FormattedSize,
        EtaFormatted = d.EtaText,
        IsPaused = d.IsPaused,
        IsComplete = d.IsCompleted,
        CanPause = d.IsDownloading && !d.IsPaused,
        CanResume = d.IsDownloading && d.IsPaused
    };
}

/// <summary>De /notifications-endpoints, gekoppeld aan de NotificationManager van de app.</summary>
public sealed class MacRemoteNotifications : IRemoteNotifications
{
    private readonly MainWindowViewModel _main;

    public MacRemoteNotifications(MainWindowViewModel main) => _main = main;

    public NotificationsResponseDto GetNotifications()
    {
        var engine = _main.Notifications;
        var cfg = engine.Config;
        return new NotificationsResponseDto
        {
            UnreadCount = engine.UnreadCount,
            Notifications = cfg.Notifications.Select(n => new NotificationItemDto
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
            }).ToList()
        };
    }

    public int MarkAsRead(string id)
    {
        _main.Notifications.MarkAsRead(id);
        return _main.Notifications.UnreadCount;
    }

    public int MarkAllAsRead()
    {
        _main.Notifications.MarkAllAsRead();
        return 0;
    }

    public int DeleteNotification(string id)
    {
        _main.Notifications.DeleteNotification(id);
        return _main.Notifications.UnreadCount;
    }

    public int ClearAll()
    {
        _main.Notifications.ClearAllNotifications();
        return 0;
    }
}
