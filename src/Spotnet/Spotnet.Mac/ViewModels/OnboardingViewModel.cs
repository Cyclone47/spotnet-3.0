using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Network;
using System.Linq;
using Spotnet.Mac.Services;
using Spotnet.Mac.Models;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.ViewModels;

public sealed class OnboardingViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly UserPreferencesService _prefsService;

    private int _currentStep = 1;
    private AppThemeStyle _selectedStyle = AppThemeStyle.ModernLight;

    // Provider fields
    private string _selectedProvider = "Eweka";
    private string _server = "news.eweka.nl";
    private int _port = 563;
    private bool _ssl = true;
    private int _connections = 8;
    private string _username = "";
    private string _password = "";

    private string _statusMessage = "";
    private bool _isTesting;

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsStep1));
                OnPropertyChanged(nameof(IsStep2));
                OnPropertyChanged(nameof(IsStep3));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < 3;

    public AppThemeStyle SelectedStyle
    {
        get => _selectedStyle;
        set
        {
            if (SetProperty(ref _selectedStyle, value))
            {
                ThemeService.Instance.ApplyTheme(value);
            }
        }
    }

    public bool IsModernLightSelected
    {
        get => SelectedStyle == AppThemeStyle.ModernLight;
        set { if (value) SelectedStyle = AppThemeStyle.ModernLight; }
    }

    public bool IsModernDarkSelected
    {
        get => SelectedStyle == AppThemeStyle.ModernDark;
        set { if (value) SelectedStyle = AppThemeStyle.ModernDark; }
    }

    public bool IsClassicSelected
    {
        get => SelectedStyle == AppThemeStyle.Classic;
        set { if (value) SelectedStyle = AppThemeStyle.Classic; }
    }

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

    public string Server { get => _server; set => SetProperty(ref _server, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public bool Ssl { get => _ssl; set => SetProperty(ref _ssl, value); }
    public int Connections { get => _connections; set => SetProperty(ref _connections, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsTesting { get => _isTesting; set => SetProperty(ref _isTesting, value); }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public event Action? OnboardingFinished;

    public OnboardingViewModel(IAppPaths appPaths, ISecretStore secretStore, UserPreferencesService prefsService)
    {
        _appPaths = appPaths;
        _secretStore = secretStore;
        _prefsService = prefsService;

        _selectedStyle = _prefsService.Current.ThemeStyle;

        NextCommand = new RelayCommand(GoNext);
        BackCommand = new RelayCommand(GoBack);
        FinishCommand = new RelayCommand(FinishOnboarding);
        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());

        SelectedProvider = _prefsService.Current.SelectedProvider ?? "Eweka";
    }

    private void GoNext()
    {
        if (CurrentStep == 2)
        {
            if (string.IsNullOrWhiteSpace(Server))
            {
                StatusMessage = "Vul een serveradres in alvorens verder te gaan.";
                return;
            }
        }
        if (CurrentStep < 3)
        {
            CurrentStep++;
            StatusMessage = "";
        }
    }

    private void GoBack()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            StatusMessage = "";
        }
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

        var (success, message) = await NntpClient.TestConnectionAsync(info);
        IsTesting = false;
        StatusMessage = success ? $"✓ {message}" : $"✗ Fout: {message}";
    }

    public void FinishOnboarding()
    {
        try
        {
            _appPaths.EnsureDirectoriesExist();

            // 1. Save servers.xml with role-based entries
            string configPath = Path.Combine(_appPaths.DataFolder, "servers.xml");
            var provider = SelectedProviderItem;
            var headersHost = Server;
            var downloadHost = provider != null && !provider.IsManual ? provider.Download : Server;
            var uploadHost = provider != null && !provider.IsManual ? provider.Upload : Server;
            var downloadPort = provider != null && !provider.IsManual ? provider.DownloadPort : Port;
            var uploadPort = provider != null && !provider.IsManual ? provider.UploadPort : Port;

            var doc = new XDocument(
                new XElement("Spotnet",
                    new XElement("Server",
                        new XAttribute("Type", "Headers"),
                        new XAttribute("Server", headersHost),
                        new XAttribute("Port", Port),
                        new XAttribute("SSL", Ssl ? "1" : "0"),
                        new XAttribute("Connections", 2),
                        new XAttribute("Username", Username)
                    ),
                    new XElement("Server",
                        new XAttribute("Type", "Downloads"),
                        new XAttribute("Server", downloadHost),
                        new XAttribute("Port", downloadPort),
                        new XAttribute("SSL", Ssl ? "1" : "0"),
                        new XAttribute("Connections", Math.Max(1, Connections - 2)),
                        new XAttribute("Username", Username)
                    ),
                    new XElement("Server",
                        new XAttribute("Type", "Uploads"),
                        new XAttribute("Server", uploadHost),
                        new XAttribute("Port", uploadPort),
                        new XAttribute("SSL", Ssl ? "1" : "0"),
                        new XAttribute("Connections", 1),
                        new XAttribute("Username", Username)
                    )
                )
            );
            doc.Save(configPath);

            // 2. Save password in Keychain
            if (!string.IsNullOrEmpty(Password))
            {
                _secretStore.SetSecret($"Spotnet_{headersHost}_{Username}", Password);
                if (!string.Equals(downloadHost, headersHost, StringComparison.OrdinalIgnoreCase))
                    _secretStore.SetSecret($"Spotnet_{downloadHost}_{Username}", Password);
                if (!string.Equals(uploadHost, headersHost, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uploadHost, downloadHost, StringComparison.OrdinalIgnoreCase))
                    _secretStore.SetSecret($"Spotnet_{uploadHost}_{Username}", Password);
            }

            // 3. Save preferences
            var prefs = _prefsService.Current;
            prefs.ThemeStyle = SelectedStyle;
            prefs.SelectedProvider = SelectedProvider;
            prefs.IsOnboardingCompleted = true;
            _prefsService.Save(prefs);

            Log.Info("Onboarding successfully completed.");
            OnboardingFinished?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to finish onboarding: {0}", ex.Message);
            StatusMessage = $"Fout bij voltooien: {ex.Message}";
        }
    }
}
