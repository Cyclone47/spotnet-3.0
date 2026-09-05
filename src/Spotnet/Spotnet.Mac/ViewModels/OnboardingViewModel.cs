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

        ApplyProviderPreset(_selectedProvider);
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

    public void FinishOnboarding()
    {
        try
        {
            _appPaths.EnsureDirectoriesExist();

            // 1. Save servers.xml
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

            // 2. Save password in Keychain
            if (!string.IsNullOrEmpty(Password))
            {
                _secretStore.SetSecret($"Spotnet_{Server}_{Username}", Password);
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
