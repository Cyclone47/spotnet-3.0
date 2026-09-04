using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Network;
using Spotnet.Mac.Services;
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

    public List<string> ProviderList { get; } = new()
    {
        "Eweka",
        "Newshosting",
        "Giganews",
        "Astraweb",
        "PureUsenet",
        "ViperNews",
        "Tweaknews",
        "Aangepast (Custom)"
    };

    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                ApplyProviderPreset(value);
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

    private bool _showDesktopNotifications = true;
    public bool ShowDesktopNotifications
    {
        get => _showDesktopNotifications;
        set => SetProperty(ref _showDesktopNotifications, value);
    }

    private bool _externalBrowser = true;
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

    private void ApplyProviderPreset(string provider)
    {
        switch (provider)
        {
            case "Eweka":
                Server = "news.eweka.nl";
                Port = 563;
                Ssl = true;
                Connections = 8;
                break;
            case "Newshosting":
                Server = "news.newshosting.com";
                Port = 563;
                Ssl = true;
                Connections = 20;
                break;
            case "Giganews":
                Server = "news.giganews.com";
                Port = 563;
                Ssl = true;
                Connections = 20;
                break;
            case "Astraweb":
                Server = "ssl.astraweb.com";
                Port = 563;
                Ssl = true;
                Connections = 10;
                break;
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
            case "Tweaknews":
                Server = "news.tweaknews.eu";
                Port = 563;
                Ssl = true;
                Connections = 10;
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

        var (success, message) = await NntpClient.TestConnectionAsync(info);
        IsTesting = false;
        StatusMessage = success ? $"✓ {message}" : $"✗ Fout: {message}";
    }

    public void LoadSettings()
    {
        try
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
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load settings: {0}", ex.Message);
        }

        var prefs = _prefsService.Current;
        _downloadMode = prefs.DownloadMode;
        DownloadFolder = string.IsNullOrWhiteSpace(prefs.DownloadFolder) ? _appPaths.DownloadsFolder : prefs.DownloadFolder;
        MaxDownloadConnections = prefs.MaxDownloadConnections > 0 ? prefs.MaxDownloadConnections : 4;
        _initialFetchDays = prefs.InitialFetchDays;
        ShowDesktopNotifications = prefs.ShowDesktopNotifications;
        ExternalBrowser = prefs.ExternalBrowser;
        OnPropertyChanged(nameof(SelectedDownloadMode));
        OnPropertyChanged(nameof(SelectedInitialFetchRange));

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

            var doc = new XDocument(
                new XElement("Spotnet",
                    new XElement("Server",
                        new XAttribute("Type", "Headers"),
                        new XAttribute("Server", Server),
                        new XAttribute("Port", Port),
                        new XAttribute("SSL", Ssl ? "1" : "0"),
                        new XAttribute("Connections", Connections),
                        new XAttribute("Username", Username)
                    )
                )
            );
            doc.Save(configPath);

            // Store password in macOS Keychain securely
            if (!string.IsNullOrEmpty(Password))
            {
                _secretStore.SetSecret($"Spotnet_{Server}_{Username}", Password);
            }

            var prefs = _prefsService.Current;
            prefs.ThemeStyle = _selectedTheme;
            prefs.DownloadMode = _downloadMode;
            prefs.DownloadFolder = DownloadFolder;
            prefs.MaxDownloadConnections = MaxDownloadConnections;
            prefs.InitialFetchDays = _initialFetchDays;
            prefs.ShowDesktopNotifications = ShowDesktopNotifications;
            prefs.ExternalBrowser = ExternalBrowser;
            _prefsService.Save(prefs);

            StatusMessage = "Instellingen opgeslagen in Sleutelhanger (Keychain)!";
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fout bij opslaan: {ex.Message}";
        }
    }
}
