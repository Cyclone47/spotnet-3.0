using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Network;
using Spotnet.Mac.Services;
using Spotnet.Mac.Models;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly ISecretStore _secretStore;
    private readonly IAppPaths _appPaths;

    private string _server = "";
    private int _port = 563;
    private bool _ssl = true;
    private int _connections = 4;
    private string _username = "";
    private string _password = "";
    private string _selectedProvider = "Eweka";
    private string _statusMessage = "";
    private bool _isTesting;

    public string Server { get => _server; set => SetProperty(ref _server, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public bool Ssl { get => _ssl; set => SetProperty(ref _ssl, value); }
    public int Connections { get => _connections; set => SetProperty(ref _connections, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsTesting { get => _isTesting; set => SetProperty(ref _isTesting, value); }

    public IReadOnlyList<ProviderItem> ProviderList { get; } = UsenetProviders.All;

    private ProviderItem? _selectedProviderItem;
    public ProviderItem? SelectedProviderItem
    {
        get => _selectedProviderItem;
        set
        {
            if (SetProperty(ref _selectedProviderItem, value))
            {
                if (value != null)
                {
                    _selectedProvider = value.Name;
                    OnPropertyChanged(nameof(SelectedProvider));
                    ApplyProviderItem(value);
                }
            }
        }
    }

    public string SelectedProvider
    {
        get => _selectedProviderItem?.Name ?? _selectedProvider;
        set
        {
            if (_selectedProvider != value)
            {
                _selectedProvider = value;
                var match = ProviderList.FirstOrDefault(p => string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    SelectedProviderItem = match;
                }
                else
                {
                    OnPropertyChanged();
                    ApplyProviderPreset(value);
                }
            }
        }
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand PickDownloadFolderCommand { get; }

    public event Action? RequestClose;
    public event Action? RequestPickFolder;

    private readonly UserPreferencesService _prefsService;
    private AppThemeStyle _selectedTheme = AppThemeStyle.ModernLight;

    public List<string> ThemeList { get; } = new()
    {
        "Modern Licht",
        "Modern Donker",
        "Klassiek"
    };

    public string SelectedTheme
    {
        get => _selectedTheme switch
        {
            AppThemeStyle.ModernLight => "Modern Licht",
            AppThemeStyle.ModernDark => "Modern Donker",
            AppThemeStyle.Classic => "Klassiek",
            _ => "Modern Licht"
        };
        set
        {
            _selectedTheme = value switch
            {
                "Modern Donker" => AppThemeStyle.ModernDark,
                "Klassiek" => AppThemeStyle.Classic,
                _ => AppThemeStyle.ModernLight
            };
            OnPropertyChanged();
            ThemeService.Instance.ApplyTheme(_selectedTheme);
        }
    }

    // ── Download Settings ──────────────────────────────────────────────────────
    private string _downloadFolder = "";
    public string DownloadFolder { get => _downloadFolder; set => SetProperty(ref _downloadFolder, value); }

    private int _maxDownloadConnections = 4;
    public int MaxDownloadConnections { get => _maxDownloadConnections; set => SetProperty(ref _maxDownloadConnections, value); }

    public List<string> DownloadModeList { get; } = new()
    {
        "Downloaden (ingebouwd)",
        "NZB Openen met app",
        "Alleen NZB opslaan"
    };

    private DownloadMode _downloadMode = DownloadMode.Integrated;
    public string SelectedDownloadMode
    {
        get => _downloadMode switch
        {
            DownloadMode.Integrated => "Downloaden (ingebouwd)",
            DownloadMode.OpenNzb => "NZB Openen met app",
            DownloadMode.SaveNzb => "Alleen NZB opslaan",
            _ => "Downloaden (ingebouwd)"
        };
        set
        {
            _downloadMode = value switch
            {
                "NZB Openen met app" => DownloadMode.OpenNzb,
                "Alleen NZB opslaan" => DownloadMode.SaveNzb,
                _ => DownloadMode.Integrated
            };
            OnPropertyChanged();
        }
    }

    // ── Synchronisation & Database Settings ────────────────────────────────────
    private int _initialFetchDays = 90;
    public List<string> InitialFetchRangeList { get; } = new()
    {
        "30 dagen",
        "90 dagen (aanbevolen)",
        "365 dagen (1 jaar)",
        "Alles (volledig archief)"
    };

    public string SelectedInitialFetchRange
    {
        get => _initialFetchDays switch
        {
            30 => "30 dagen",
            90 => "90 dagen (aanbevolen)",
            365 => "365 dagen (1 jaar)",
            0 => "Alles (volledig archief)",
            _ => "90 dagen (aanbevolen)"
        };
        set
        {
            _initialFetchDays = value switch
            {
                "30 dagen" => 30,
                "365 dagen (1 jaar)" => 365,
                "Alles (volledig archief)" => 0,
                _ => 90
            };
            OnPropertyChanged();
        }
    }

    private bool _dbAutoUpdateEnabled = true;
    public bool DbAutoUpdateEnabled
    {
        get => _dbAutoUpdateEnabled;
        set => SetProperty(ref _dbAutoUpdateEnabled, value);
    }

    private int _dbAutoUpdateIntervalMin = 10;
    public int DbAutoUpdateIntervalMin
    {
        get => _dbAutoUpdateIntervalMin;
        set => SetProperty(ref _dbAutoUpdateIntervalMin, value);
    }

    private bool _retentionEnabled;
    public bool RetentionEnabled
    {
        get => _retentionEnabled;
        set
        {
            if (SetProperty(ref _retentionEnabled, value))
            {
                OnPropertyChanged(nameof(IsRetentionInputEnabled));
            }
        }
    }

    private int _retention = 30;
    public int Retention
    {
        get => _retention;
        set => SetProperty(ref _retention, value);
    }

    public bool IsRetentionInputEnabled => _retentionEnabled;

    private bool _showDesktopNotifications = true;
    public bool ShowDesktopNotifications
    {
        get => _showDesktopNotifications;
        set => SetProperty(ref _showDesktopNotifications, value);
    }

    private bool _externalBrowser = true;
    private bool _allowInvalidServerCertificate;
    private bool _checkSignatures = true;

    /// <summary>
    /// Whether cryptographic RSA signatures on spots are verified before storing them.
    /// Matches Windows Settings.Default.CheckSignatures.
    /// </summary>
    public bool CheckSignatures
    {
        get => _checkSignatures;
        set => SetProperty(ref _checkSignatures, value);
    }

    /// <summary>
    /// Accept a TLS certificate that fails validation. Off by default, as on Windows.
    /// Without this escape hatch a provider with a self-signed certificate would be
    /// unreachable now that certificates are actually checked.
    /// </summary>
    public bool AllowInvalidServerCertificate
    {
        get => _allowInvalidServerCertificate;
        set => SetProperty(ref _allowInvalidServerCertificate, value);
    }

    private bool _useSocksProxy;
    private string _socksProxyHost = "";
    private int _socksProxyPort = 1080;
    private string _socksProxyUsername = "";
    private string _socksProxyPassword = "";

    /// <summary>Route news traffic through a SOCKS5 proxy (Windows: UseSocksProxy).</summary>
    public bool UseSocksProxy
    {
        get => _useSocksProxy;
        set => SetProperty(ref _useSocksProxy, value);
    }

    public string SocksProxyHost
    {
        get => _socksProxyHost;
        set => SetProperty(ref _socksProxyHost, value);
    }

    public int SocksProxyPort
    {
        get => _socksProxyPort;
        set => SetProperty(ref _socksProxyPort, value);
    }

    public string SocksProxyUsername
    {
        get => _socksProxyUsername;
        set => SetProperty(ref _socksProxyUsername, value);
    }

    /// <summary>Kept out of preferences.json; stored in the keychain on save.</summary>
    public string SocksProxyPassword
    {
        get => _socksProxyPassword;
        set => SetProperty(ref _socksProxyPassword, value);
    }

    public bool ExternalBrowser
    {
        get => _externalBrowser;
        set => SetProperty(ref _externalBrowser, value);
    }

    private readonly DAL.SpotDatabaseService? _dbService;

    public ICommand QuickRepairCommand { get; }
    public ICommand TestNotificationCommand { get; }

    public SettingsViewModel(ISecretStore secretStore, IAppPaths appPaths, UserPreferencesService? prefsService = null, DAL.SpotDatabaseService? dbService = null)
    {
        _secretStore = secretStore;
        _appPaths = appPaths;
        _prefsService = prefsService ?? new UserPreferencesService(_appPaths);
        _dbService = dbService;
        _selectedTheme = _prefsService.Current.ThemeStyle;

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());
        SaveCommand = new RelayCommand(SaveSettings);
        PickDownloadFolderCommand = new RelayCommand(() => RequestPickFolder?.Invoke());

        QuickRepairCommand = new RelayCommand(async () =>
        {
            if (_dbService == null)
            {
                StatusMessage = "Geen actieve database service beschikbaar.";
                return;
            }
            StatusMessage = "Database herstellen en optimaliseren...";
            var (success, msg) = await _dbService.QuickRepairAsync();
            StatusMessage = msg;
            var notifier = new Platform.MacNotificationService(_prefsService);
            notifier.NotifyDatabaseRepairFinished(success, msg);
        });

        TestNotificationCommand = new RelayCommand(() =>
        {
            var notifier = new Platform.MacNotificationService(_prefsService);
            notifier.ShowNotification("Dit is een testmelding van Spotnet.", title: "Spotnet", subtitle: "Test geslaagd", force: true);
            StatusMessage = "Testmelding verzonden naar macOS Berichtencentrum.";
        });

        LoadSettings();
    }

    private void ApplyProviderItem(ProviderItem item)
    {
        if (item.IsManual)
        {
            Port = 563;
            Ssl = true;
            return;
        }

        Server = item.Headers;
        Port = item.HeadersPort;
        Ssl = item.HeadersPort == 563 || item.HeadersPort == 443;
        if (item.Name.Equals("Newshosting", StringComparison.OrdinalIgnoreCase) ||
            item.Name.Equals("Giganews", StringComparison.OrdinalIgnoreCase))
        {
            Connections = 20;
        }
        else if (item.Name.Equals("Astraweb", StringComparison.OrdinalIgnoreCase) ||
                 item.Name.Equals("Tweaknews", StringComparison.OrdinalIgnoreCase))
        {
            Connections = 10;
        }
        else
        {
            Connections = 8;
        }
    }

    private void ApplyProviderPreset(string provider)
    {
        var match = ProviderList.FirstOrDefault(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            ApplyProviderItem(match);
            return;
        }

        switch (provider)
        {
            case "PureUsenet":
                Server = "news.pureusenet.nl";
                Port = 563;
                Ssl = true;
                Connections = 8;
                break;
            case "ViperNews":
                Server = "news.vipernews.com";
                Port = 563;
                Ssl = true;
                Connections = 8;
                break;
        }
    }

    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            StatusMessage = "Vul een serveradres in.";
            return;
        }

        IsTesting = true;
        StatusMessage = "Verbinding controleren...";

        var info = new ServerInfo
        {
            Server = Server,
            Port = Port,
            SSL = Ssl,
            Username = Username,
            Password = Password,
            Connections = Connections
        };

        // Test against the setting as it stands in the dialog, not the saved one, so the
        // checkbox can be tried before committing it.
        // Test what is on screen, not what was last saved.
        var proxy = Network.ProxySettings.Create(
            UseSocksProxy, SocksProxyHost, SocksProxyPort, SocksProxyUsername, SocksProxyPassword);

        var (success, message) = await NntpClient.TestConnectionAsync(info, AllowInvalidServerCertificate, proxy);
        IsTesting = false;
        StatusMessage = success ? $"✓ {message}" : $"✗ Fout: {message}";
    }

    public void LoadSettings()
    {
        try
        {
            var profile = Network.ServerProfile.Load(_appPaths, _secretStore);
            var headersServer = profile.Get(Network.ServerRole.Headers);
            if (headersServer != null && !string.IsNullOrEmpty(headersServer.Server))
            {
                Server = headersServer.Server;
                Port = headersServer.Port;
                Ssl = headersServer.SSL;
                Connections = headersServer.Connections;
                Username = headersServer.Username;
                Password = headersServer.Password;
            }
            else
            {
                string configPath = Path.Combine(_appPaths.DataFolder, "servers.xml");
                if (File.Exists(configPath))
                {
                    var doc = XDocument.Load(configPath);
                    var root = doc.Root;
                    var serverNode = root?.Element("Server");
                    if (serverNode != null)
                    {
                        Server = (string?)serverNode.Attribute("Server") ?? "";
                        if (int.TryParse((string?)serverNode.Attribute("Port"), out var p)) Port = p;
                        Ssl = (string?)serverNode.Attribute("SSL") == "1";
                        if (int.TryParse((string?)serverNode.Attribute("Connections"), out var c)) Connections = c;
                        Username = (string?)serverNode.Attribute("Username") ?? "";
                    }
                }

                // Retrieve password from macOS Keychain
                string? secret = _secretStore.GetSecret($"Spotnet_{Server}_{Username}");
                if (!string.IsNullOrEmpty(secret))
                {
                    Password = secret;
                }
            }

            var match = UsenetProviders.Match(ProviderList, Server)
                ?? ProviderList.FirstOrDefault(p => string.Equals(p.Name, _prefsService.Current.SelectedProvider, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _selectedProviderItem = match;
                _selectedProvider = match.Name;
                OnPropertyChanged(nameof(SelectedProviderItem));
                OnPropertyChanged(nameof(SelectedProvider));
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load settings: {0}", ex.Message);
        }

        var prefs = _prefsService.Current;
        _allowInvalidServerCertificate = prefs.AllowInvalidServerCertificate;
        _useSocksProxy = prefs.UseSocksProxy;
        _socksProxyHost = prefs.SocksProxyHost;
        _socksProxyPort = prefs.SocksProxyPort;
        _socksProxyUsername = prefs.SocksProxyUsername;
        _socksProxyPassword = _secretStore.GetSecret(Network.ProxySettings.SecretKey) ?? "";
        _downloadMode = prefs.DownloadMode;
        DownloadFolder = string.IsNullOrWhiteSpace(prefs.DownloadFolder) ? _appPaths.DownloadsFolder : prefs.DownloadFolder;
        MaxDownloadConnections = prefs.MaxDownloadConnections > 0 ? prefs.MaxDownloadConnections : 4;
        _initialFetchDays = prefs.InitialFetchDays;
        ShowDesktopNotifications = prefs.ShowDesktopNotifications;
        ExternalBrowser = prefs.ExternalBrowser;
        _dbAutoUpdateEnabled = prefs.DbAutoUpdateEnabled;
        _dbAutoUpdateIntervalMin = prefs.DbAutoUpdateIntervalMin > 0 ? prefs.DbAutoUpdateIntervalMin : 10;
        _retentionEnabled = prefs.Retention >= 1;
        _retention = prefs.Retention >= 1 ? prefs.Retention : 30;
        _checkSignatures = prefs.CheckSignatures;

        OnPropertyChanged(nameof(SelectedDownloadMode));
        OnPropertyChanged(nameof(SelectedInitialFetchRange));
        OnPropertyChanged(nameof(DbAutoUpdateEnabled));
        OnPropertyChanged(nameof(DbAutoUpdateIntervalMin));
        OnPropertyChanged(nameof(RetentionEnabled));
        OnPropertyChanged(nameof(Retention));
        OnPropertyChanged(nameof(IsRetentionInputEnabled));
        OnPropertyChanged(nameof(CheckSignatures));

        if (string.IsNullOrEmpty(Server))
        {
            ApplyProviderPreset("Eweka");
        }
    }

    public void SaveSettings()
    {
        try
        {
            _appPaths.EnsureDirectoriesExist();
            string configPath = Path.Combine(_appPaths.DataFolder, "servers.xml");

            XDocument doc;
            XElement root;
            try
            {
                doc = File.Exists(configPath) ? XDocument.Load(configPath) : new XDocument(new XElement("Spotnet"));
                root = doc.Root ?? new XElement("Spotnet");
                if (doc.Root == null) doc.Add(root);
            }
            catch (System.Xml.XmlException ex)
            {
                Log.Warn(ex, "servers.xml is unreadable and is being replaced: {0}", ex.Message);
                root = new XElement("Spotnet");
                doc = new XDocument(root);
            }

            var provider = SelectedProviderItem;
            var headersHost = Server;
            var downloadHost = provider != null && !provider.IsManual ? provider.Download : Server;
            var uploadHost = provider != null && !provider.IsManual ? provider.Upload : Server;
            var downloadPort = provider != null && !provider.IsManual ? provider.DownloadPort : Port;
            var uploadPort = provider != null && !provider.IsManual ? provider.UploadPort : Port;

            SetOrUpdateServerElement(root, Network.ServerRole.Headers, headersHost, Port, Ssl, 2, Username);
            SetOrUpdateServerElement(root, Network.ServerRole.Download, downloadHost, downloadPort, Ssl, Math.Max(1, Connections - 2), Username);
            SetOrUpdateServerElement(root, Network.ServerRole.Upload, uploadHost, uploadPort, Ssl, 1, Username);

            doc.Save(configPath);

            // Store password in macOS Keychain securely
            if (!string.IsNullOrEmpty(Password))
            {
                _secretStore.SetSecret(Network.ServerProfile.SecretKey(headersHost, Username), Password);
                if (!string.Equals(downloadHost, headersHost, StringComparison.OrdinalIgnoreCase))
                    _secretStore.SetSecret(Network.ServerProfile.SecretKey(downloadHost, Username), Password);
                if (!string.Equals(uploadHost, headersHost, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uploadHost, downloadHost, StringComparison.OrdinalIgnoreCase))
                    _secretStore.SetSecret(Network.ServerProfile.SecretKey(uploadHost, Username), Password);
            }

            var prefs = _prefsService.Current;
            int oldRetention = prefs.Retention;
            int newRetention = RetentionEnabled && Retention >= 1 ? Retention : -1;

            prefs.ThemeStyle = _selectedTheme;
            prefs.DownloadMode = _downloadMode;
            prefs.DownloadFolder = DownloadFolder;
            prefs.MaxDownloadConnections = MaxDownloadConnections;
            prefs.InitialFetchDays = _initialFetchDays;
            prefs.ShowDesktopNotifications = ShowDesktopNotifications;
            prefs.ExternalBrowser = ExternalBrowser;
            prefs.AllowInvalidServerCertificate = AllowInvalidServerCertificate;
            prefs.UseSocksProxy = UseSocksProxy;
            prefs.SocksProxyHost = SocksProxyHost;
            prefs.SocksProxyPort = SocksProxyPort;
            prefs.SocksProxyUsername = SocksProxyUsername;
            prefs.DbAutoUpdateEnabled = DbAutoUpdateEnabled;
            prefs.DbAutoUpdateIntervalMin = DbAutoUpdateIntervalMin > 0 ? DbAutoUpdateIntervalMin : 10;
            prefs.SelectedProvider = SelectedProvider;
            prefs.Retention = newRetention;
            prefs.CheckSignatures = CheckSignatures;
            _prefsService.Save(prefs);

            if (newRetention >= 1 && (oldRetention < 1 || newRetention < oldRetention) && _dbService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _dbService.RemoveOutOfRetentionSpotsAsync(newRetention);
                        await _dbService.UpdateDatabaseStatsAsync(_prefsService);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to remove out-of-retention spots after settings change: {0}", ex.Message);
                    }
                });
            }

            if (!string.IsNullOrEmpty(SocksProxyPassword))
            {
                _secretStore.SetSecret(Network.ProxySettings.SecretKey, SocksProxyPassword);
            }

            StatusMessage = "Instellingen opgeslagen in Sleutelhanger (Keychain)!";
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fout bij opslaan: {ex.Message}";
        }
    }

    private static void SetOrUpdateServerElement(XElement root, Network.ServerRole role, string host, int port, bool ssl, int connections, string username)
    {
        var entry = root.Elements("Server")
            .FirstOrDefault(e => Network.ServerProfile.ParseRole((string?)e.Attribute("Type")) == role);
        if (entry == null)
        {
            entry = new XElement("Server");
            root.Add(entry);
        }
        entry.SetAttributeValue("Type", Network.ServerProfile.RoleAttribute(role));
        entry.SetAttributeValue("Server", host);
        entry.SetAttributeValue("Port", port);
        entry.SetAttributeValue("SSL", ssl ? "1" : "0");
        entry.SetAttributeValue("Connections", connections);
        entry.SetAttributeValue("Username", username);
    }
}
