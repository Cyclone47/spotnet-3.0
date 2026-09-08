using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Spotnet.Mac.Updates;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

public partial class MacUpdateWindow : Window
{
    private readonly MacUpdater _updater;
    private readonly MacUpdateManifest _manifest;
    private readonly MacUpdateDecision _decision;
    private readonly MacUpdateWindowViewModel _viewModel;

    public MacUpdateWindow()
    {
        InitializeComponent();
        _updater = null!;
        _manifest = null!;
        _decision = default;
        _viewModel = null!;
    }

    public MacUpdateWindow(MacUpdater updater, MacUpdateManifest manifest, MacUpdateDecision decision)
    {
        InitializeComponent();
        _updater = updater;
        _manifest = manifest;
        _decision = decision;
        _viewModel = new MacUpdateWindowViewModel(updater, manifest, decision);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_decision.Action == MacUpdateAction.Required) return;
        Close();
    }
}

public sealed class MacUpdateWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MacUpdater _updater;
    private readonly MacUpdateManifest _manifest;
    private readonly MacUpdateDecision _decision;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isDownloading;
    private bool _isDownloaded;
    private string _status = "Klaar om te downloaden.";
    private long _received;
    private double _bytesPerSecond;
    private string? _downloadPath;

    public MacUpdateWindowViewModel(MacUpdater updater, MacUpdateManifest manifest, MacUpdateDecision decision)
    {
        _updater = updater;
        _manifest = manifest;
        _decision = decision;
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => !IsDownloading && !IsDownloaded);
        InstallCommand = new RelayCommand(Install, () => IsDownloaded);
        SkipCommand = new RelayCommand(Skip, () => decision.Action != MacUpdateAction.Required);
        CancelCommand = new RelayCommand(Cancel, () => IsDownloading);
        OpenChangelogCommand = new Spotnet.Mac.ViewModels.RelayCommand(OpenChangelog);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public System.Windows.Input.ICommand DownloadCommand { get; }
    public System.Windows.Input.ICommand InstallCommand { get; }
    public System.Windows.Input.ICommand SkipCommand { get; }
    public System.Windows.Input.ICommand CancelCommand { get; }
    public System.Windows.Input.ICommand OpenChangelogCommand { get; }

    public string VersionText => "Spotnet " + _manifest.Version;
    public string CurrentVersionText => "Geïnstalleerde versie: " + MacUpdateVersion.Current;
    public string ReleaseTypeText => _decision.Action == MacUpdateAction.Required ? "Verplichte update" : "Nieuwe versie beschikbaar";
        public IBrush ReleaseTypeBrush => new SolidColorBrush(Color.Parse(_decision.Action == MacUpdateAction.Required ? "#C0392B" : "#2E7D32"));
    public string SizeText => FormatBytes(_manifest.Size);
    public string ChangelogText => _manifest.ReleaseNotesUrl?.ToString() ?? "Geen changeloglink beschikbaar";
    public bool HasChangelog => _manifest.ReleaseNotesUrl != null;
    public bool IsDownloading { get => _isDownloading; private set => Set(ref _isDownloading, value); }
    public bool IsDownloaded { get => _isDownloaded; private set => Set(ref _isDownloaded, value); }
    public bool CanClose => _decision.Action != MacUpdateAction.Required && !IsDownloading;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress => _manifest.Size <= 0 ? 0 : Math.Min(1, (double)_received / _manifest.Size);
    public string ProgressText => $"{FormatBytes(_received)} van {SizeText}";
    public string SpeedText => _bytesPerSecond <= 0 ? "—" : FormatBytes((long)_bytesPerSecond) + "/s";
    public string InstallButtonText => IsDownloaded ? "Spotnet opnieuw starten" : "Downloaden";

    private async Task DownloadAsync()
    {
        IsDownloading = true;
        Status = "Update downloaden...";
        try
        {
            var progress = new Progress<MacUpdateProgress>(value =>
            {
                _received = value.Received;
                _bytesPerSecond = value.BytesPerSecond;
                OnChanged(nameof(Progress));
                OnChanged(nameof(ProgressText));
                OnChanged(nameof(SpeedText));
            });
            _downloadPath = await _updater.DownloadAsync(_manifest, progress, _cancellation.Token);
            IsDownloaded = true;
            Status = "Download gecontroleerd met SHA-256. Klaar om te installeren.";
        }
        catch (OperationCanceledException)
        {
            Status = "Download geannuleerd.";
        }
        catch (Exception ex)
        {
            Status = "Download mislukt: " + ex.Message;
        }
        finally
        {
            IsDownloading = false;
            OnChanged(nameof(CanClose));
            OnChanged(nameof(InstallButtonText));
        }
    }

    private void Install()
    {
        if (!IsDownloaded || string.IsNullOrWhiteSpace(_downloadPath)) return;
        try
        {
            _updater.InstallAndRestart(_downloadPath);
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Status = "Installeren mislukt: " + ex.Message;
        }
    }

    private void Skip()
    {
        _updater.SkipVersion(_manifest);
        CloseWindow();
    }

    private void Cancel() => _cancellation.Cancel();

    private void OpenChangelog()
    {
        if (_manifest.ReleaseNotesUrl == null) return;
        Process.Start(new ProcessStartInfo("open", _manifest.ReleaseNotesUrl.ToString()) { UseShellExecute = false });
    }

    private void CloseWindow()
    {
        foreach (Window window in Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows
            : Array.Empty<Window>())
        {
            if (ReferenceEquals(window.DataContext, this))
            {
                window.Close();
                break;
            }
        }
    }

    public void Dispose() => _cancellation.Cancel();

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnChanged(name!);
    }

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}

internal sealed class AsyncRelayCommand : System.Windows.Input.ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && _canExecute();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _execute(); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
