using System;
using System.IO;
using Newtonsoft.Json;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>Where a double-clicked spot opens. Windows uses tabs; both are offered here.</summary>
public enum SpotOpenMode
{
    Tab,
    Window
}

public enum AppThemeStyle
{
    ModernLight,
    ModernDark,
    Classic
}

/// <summary>
/// What happens when the user clicks Download on a spot — mirrors the Windows
/// "Bewerken › Downloadknop" submenu (Downloaden / NZB Openen / NZB Opslaan).
/// </summary>
public enum DownloadMode
{
    /// <summary>Download the actual binary files from Usenet directly inside Spotnet (default).</summary>
    Integrated,
    /// <summary>Save the .nzb file and open it with the OS default handler (SABnzbd, NZBGet, …).</summary>
    OpenNzb,
    /// <summary>Only save the .nzb file to the downloads folder, nothing else.</summary>
    SaveNzb,
    /// <summary>
    /// Forward the NZB to the external NZBGet installation over JSON-RPC, as Windows'
    /// Settings.Default.ExternalNzbGet does.
    /// </summary>
    ExternalNzbGet
}

public sealed class UserPreferences
{
    public AppThemeStyle ThemeStyle { get; set; } = AppThemeStyle.ModernLight;
    public bool IsOnboardingCompleted { get; set; }
    public string SelectedProvider { get; set; } = "Eweka";

    /// <summary>Tab, like the Windows client, or a separate window.</summary>
    public SpotOpenMode SpotOpenMode { get; set; } = SpotOpenMode.Tab;

    /// <summary>
    /// What the Download button does — mirrors Windows "Bewerken › Downloadknop".
    /// Defaults to integrated (built-in downloader).
    /// </summary>
    public DownloadMode DownloadMode { get; set; } = DownloadMode.Integrated;

    /// <summary>
    /// User-chosen binary download folder. Empty string means use the OS default
    /// ~/Downloads/Spotnet/ path.
    /// </summary>
    public string DownloadFolder { get; set; } = "";

    /// <summary>
    /// Number of parallel NNTP connections for the integrated binary downloader.
    /// 0 means use the server's Connections setting from servers.xml.
    /// </summary>
    public int MaxDownloadConnections { get; set; }

    /// <summary>
    /// Days back to fetch on an initial sync (0 = everything). Defaults to 90.
    /// </summary>
    public int InitialFetchDays { get; set; } = 90;

    /// <summary>
    /// Whether web links in spot descriptions open in the macOS default browser.
    /// </summary>
    public bool ExternalBrowser { get; set; } = true;

    /// <summary>
    /// Whether desktop notifications are shown when downloads complete or db repairs finish.
    /// </summary>
    public bool ShowDesktopNotifications { get; set; } = true;

    /// <summary>
    /// Which spot-list column the list is ordered by, as the grid column's
    /// SortMemberPath. Validated against <see cref="DAL.SpotSort"/> on read, so an
    /// unknown value from another build falls back to the default rather than failing.
    /// Windows keeps the same two settings (SortColumn, SortDirection).
    /// </summary>
    public string SortColumn { get; set; } = DAL.SpotSort.DefaultColumn;

    /// <summary>"ASC" or "DESC". Newest first by default, as on Windows.</summary>
    public string SortDirection { get; set; } = DAL.SpotSort.DefaultDirection;

    /// <summary>
    /// Posting nickname/alias for comments and complaints. Matches Windows Settings.Default.Nickname (default "Spotter").
    /// </summary>
    public string Nickname { get; set; } = "Spotter";

    /// <summary>
    /// Accept a news server's TLS certificate even when it fails validation. Off by
    /// default, as on Windows. Only turn this on for a provider using a self-signed
    /// certificate: it removes the protection against another machine impersonating the
    /// server and reading the credentials sent in AUTHINFO.
    /// </summary>
    public bool AllowInvalidServerCertificate { get; set; }

    /// <summary>
    /// Route news traffic through a SOCKS5 proxy, as the Windows client's UseSocksProxy
    /// setting does. The proxy password lives in the keychain, not here.
    /// </summary>
    public bool UseSocksProxy { get; set; }

    public string SocksProxyHost { get; set; } = "";

    public int SocksProxyPort { get; set; } = 1080;

    public string SocksProxyUsername { get; set; } = "";

    /// <summary>
    /// Whether automatic periodic background synchronization is enabled.
    /// Matches Windows DbAutoUpdateEnabled (default true).
    /// </summary>
    public bool DbAutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// Interval in minutes between automatic periodic synchronizations.
    /// Matches Windows DbAutoUpdateIntervalMin (default 10).
    /// </summary>
    public int DbAutoUpdateIntervalMin { get; set; } = 10;

    /// <summary>
    /// Spot retention period in days. Matches Windows Retention (default -1).
    /// -1 or 0 means unlimited (keep all spots); >= 1 removes spots older than this many days.
    /// </summary>
    public int Retention { get; set; } = -1;

    /// <summary>Minimum rowid in the spots table (Windows: DatabaseMin).</summary>
    public long DatabaseMin { get; set; }

    /// <summary>Maximum rowid in the spots table (Windows: DatabaseMax).</summary>
    public long DatabaseMax { get; set; }

    /// <summary>Total spots count in the database (Windows: DatabaseCount).</summary>
    public long DatabaseCount { get; set; }

    /// <summary>
    /// Whether spots from blacklisted posters or spots on the spot blacklist are hidden.
    /// Matches Windows Settings.Default.HideBlacklistedSpots (default false).
    /// </summary>
    public bool HideBlacklistedSpots { get; set; }

    /// <summary>
    /// Whether only spots from whitelist or verified posters are shown.
    /// Matches Windows MainWindowVm.ShowTrustedOnlyMode (default false).
    /// </summary>
    public bool ShowTrustedOnlyMode { get; set; }

    /// <summary>
    /// Whether erotica spots (category 9) are included in search results.
    /// Matches Windows Settings.Default.ShowEroticaInSearchResults (default false).
    /// </summary>
    public bool ShowEroticaInSearchResults { get; set; }

    /// <summary>
    /// Whether external blacklist and whitelist lists should be updated from the net.
    /// Matches Windows Settings.Default.DownloadExternalLists (default true).
    /// </summary>
    public bool DownloadExternalLists { get; set; } = true;

    /// <summary>
    /// Interval in minutes between downloads of external lists.
    /// Matches Windows Settings.Default.ExternalListsUpdateInterval (default 60).
    /// </summary>
    public int ExternalListsUpdateInterval { get; set; } = 60;

    /// <summary>URL for poster blacklist CSV.</summary>
    public string BlacklistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/blacklist.csv";

    /// <summary>URL for poster whitelist CSV.</summary>
    public string WhitelistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/whitelist.csv";

    /// <summary>URL for spot blacklist CSV.</summary>
    public string SpotBlacklistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_blacklist.csv";

    /// <summary>URL for spot whitelist CSV.</summary>
    public string SpotWhitelistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_whitelist.csv";

    /// <summary>
    /// Whether cryptographic RSA signatures on spots are verified before storing them.
    /// Matches Windows Settings.Default.CheckSignatures (default true).
    /// </summary>
    public bool CheckSignatures { get; set; } = true;

    /// <summary>
    /// Optional URL for external keys.xml. Matches Windows Settings.Default.KeysURL.
    /// </summary>
    public string KeysUrl { get; set; } = "";

    /// <summary>
    /// The newsgroup where spam reports are fetched and posted.
    /// Matches Windows Settings.Default.ReportGroup (default free.willey).
    /// </summary>
    public string ReportGroup { get; set; } = "free.willey";

    /// <summary>
    /// Number of spam reports at which a spot is hidden from the spot list.
    /// Matches Windows Settings.Default.NumOfSpamReportsToSpotHide (default 5).
    /// 0 or negative means never hide based on spam reports.
    /// </summary>
    public int NumOfSpamReportsToSpotHide { get; set; } = 5;

    // ── Downloader (fase 3) — names and defaults follow the Windows client ────

    /// <summary>
    /// Download speed limit in KB/s. Matches Windows Settings.Default.SpeedLimit.
    /// -1 or 0 means unlimited; a positive value is throttled in the downloader.
    /// </summary>
    public int SpeedLimit { get; set; } = -1;

    /// <summary>
    /// How often a failed segment is retried before it is marked as failed.
    /// Matches Windows Settings.Default.DownloaderRetries (default 3).
    /// </summary>
    public int DownloaderRetries { get; set; } = 3;

    /// <summary>
    /// Seconds between two retry attempts on a failed segment.
    /// Matches Windows Settings.Default.DownloaderRetryIntervalSec (default 10).
    /// </summary>
    public int DownloaderRetryIntervalSec { get; set; } = 10;

    /// <summary>
    /// Milliseconds to wait for a connection to the news server to come up.
    /// Matches Windows Settings.Default.ConnectionTimeout (default 10000).
    /// </summary>
    public int ConnectionTimeout { get; set; } = 10000;

    /// <summary>
    /// Milliseconds without incoming data after which a read is considered dead.
    /// Matches Windows Settings.Default.DataReceivingTimeout (default 60000).
    /// </summary>
    public int DataReceivingTimeout { get; set; } = 60000;

    /// <summary>
    /// Whether the cache servers of specific providers are used for downloads.
    /// Matches Windows Settings.Default.IsCachingEnabled (default true). Those
    /// providers use a host whose name ends in the cache suffixes below; for all
    /// other providers the setting has no effect, as on Windows.
    /// </summary>
    public bool IsCachingEnabled { get; set; } = true;

    /// <summary>
    /// Reserved cache buffer in megabytes for provider cache servers.
    /// Matches Windows Settings.Default.DownloaderCacheSizeMb (default 20).
    /// Windows only uses it for its own downloader bookkeeping; the Mac client
    /// applies it as a soft cap on bytes buffered per download job.
    /// </summary>
    public int DownloaderCacheSizeMb { get; set; } = 20;

    /// <summary>
    /// Whether downloads only run inside a time window. Matches Windows
    /// Settings.Default.DownloaderSchedule (default false).
    /// </summary>
    public bool DownloaderSchedule { get; set; }

    /// <summary>
    /// Start of the download window, of which only the time of day is used.
    /// Matches Windows Settings.Default.DownloaderStartTime.
    /// </summary>
    public DateTime DownloaderStartTime { get; set; } = new(2016, 10, 26, 14, 52, 0);

    /// <summary>
    /// End of the download window, of which only the time of day is used.
    /// Matches Windows Settings.Default.DownloaderEndTime.
    /// </summary>
    public DateTime DownloaderEndTime { get; set; } = new(2016, 10, 26, 14, 52, 0);

    /// <summary>
    /// Whether the par2 recovery files are deleted after a successful download.
    /// Matches Windows Settings.Default.RemovePar2FilesAfterDownload (default true).
    /// </summary>
    public bool RemovePar2FilesAfterDownload { get; set; } = true;

    /// <summary>
    /// What happens to the downloaded files when the user removes a row from the
    /// download list. Matches Windows Settings.Default.RemoveFilesOnDownloadRemove:
    /// -1 = ask every time (and remember a "don't ask again" answer),
    ///  1 = always delete the files from disk,
    ///  0 = always keep the files.
    /// </summary>
    public int RemoveFilesOnDownloadRemove { get; set; } = -1;

    /// <summary>
    /// Whether the computer is shut down when the last download has finished.
    /// Windows keeps this in a static Sys.ShutdownPCAfterDownloads that the settings
    /// screen loads into at startup; the Mac client reads the preference directly.
    /// The dialog lets the user cancel within 60 seconds, as on Windows.
    /// </summary>
    public bool ShutdownPcAfterDownloads { get; set; }

    /// <summary>
    /// Whether the external NZBGet downloader handles all binary downloads instead of
    /// the built-in downloader. Matches Windows Settings.Default.ExternalNzbGet.
    /// </summary>
    public bool ExternalNzbGet { get; set; }

    /// <summary>NZBGet RPC host. Matches Windows Settings.Default.NzbGetControlIP (default "-").</summary>
    public string NzbGetControlIP { get; set; } = "-";

    /// <summary>NZBGet RPC port. Matches Windows Settings.Default.NzbGetControlPort (default "-").</summary>
    public string NzbGetControlPort { get; set; } = "-";

    /// <summary>NZBGet RPC username. Matches Windows Settings.Default.NzbGetControlUsername (default "-").</summary>
    public string NzbGetControlUsername { get; set; } = "-";
    /// <summary>NZBGet RPC password. Matches Windows Settings.Default.NzbGetControlPassword (default "-").</summary>
    public string NzbGetControlPassword { get; set; } = "-";

    /// <summary>
    /// A host is a provider cache server when it ends in one of these suffixes.
    /// Windows: CachingSystem.MasterHostnameSnelNl / MasterHostname5Euro.
    /// </summary>
    public static readonly string[] CacheHostSuffixes = { "cache.snelnl.com", "cache.usenetsys.com" };
    /// <summary>
    /// Whether this host is a provider cache server the built-in downloader may use.
    /// Windows gates that on IsCachingEnabled plus the provider being Snelnl or one of
    /// the "5 euro" providers (CachingSystem.IsEnabled + DownloadQueue.IsCachingEnabled);
    /// this is the same check in one place.
    /// </summary>
    public static bool IsCacheServer(string host, bool isCachingEnabled)
    {
        if (!isCachingEnabled || string.IsNullOrWhiteSpace(host))
        {
            return false;
        }
        foreach (string suffix in CacheHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class UserPreferencesService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _settingsFilePath;
    private UserPreferences _current;

    public UserPreferences Current => _current;

    public UserPreferencesService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _settingsFilePath = Path.Combine(appPaths.DataFolder, "preferences.json");
        _current = Load();
    }

    public UserPreferences Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var prefs = JsonConvert.DeserializeObject<UserPreferences>(json);
                if (prefs != null)
                {
                    _current = prefs;
                    return _current;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load preferences from {0}, using defaults.", _settingsFilePath);
        }

        _current = new UserPreferences();
        return _current;
    }

    public void Save(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _current = preferences;
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(_current, Formatting.Indented);
            File.WriteAllText(_settingsFilePath, json);
            Log.Info("Saved preferences to {0}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save preferences to {0}", _settingsFilePath);
        }
    }
}
