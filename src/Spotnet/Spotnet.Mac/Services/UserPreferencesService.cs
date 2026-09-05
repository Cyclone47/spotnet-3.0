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
    SaveNzb
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
