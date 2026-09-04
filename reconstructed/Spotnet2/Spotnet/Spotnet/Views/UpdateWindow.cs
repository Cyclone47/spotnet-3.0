using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Deployment;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Views;

/// <summary>
/// The update prompt and the download that follows it. The window stays put for the whole
/// download so there is always something to cancel, then hands over to Setup, which shows
/// its own progress while it replaces the files and starts Spotnet again.
/// </summary>
public partial class UpdateWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly UpdateManifest _manifest;
    private readonly bool _required;
    private CancellationTokenSource _cancellation;
    private bool _working;
    private bool _handedOver;

    internal UpdateWindow(UpdateManifest manifest, UpdateDecision decision)
    {
        InitializeComponent();
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _required = decision.Action == UpdateAction.Required;

        HeadlineLabel.Text = _required ? Words.UpdateRequiredHeadline : Words.UpdateAvailableHeadline;
        VersionLabel.Text = string.Format(Words.UpdateVersionLine, _manifest.Version, AppHelper.AppVersion);
        SizeLabel.Text = string.Format(Words.UpdateDownloadSize,
            AppHelper.FormatSizeMegaBytes(_manifest.Size / 1048576.0));

        if (_manifest.ReleaseNotesUrl != null)
        {
            NotesLink.NavigateUri = _manifest.ReleaseNotesUrl;
            NotesPanel.Visibility = Visibility.Visible;
        }
        // A required update can be postponed to the next start, never dismissed for good.
        SkipButton.Visibility = _required ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NotesLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (Owner is not MainWindow mainWindow)
            {
                Log.Warn("The update release-notes link has no Spotnet main window owner.");
                return;
            }

            // A modal update prompt keeps its owner disabled, so dismiss it before
            // selecting (or creating) the same tab as Help > Release Notes.
            Close();
            mainWindow.OpenPage(PageTypeEnum.ReleaseNotes).Forget();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working)
        {
            // The button reads Cancel while the download runs.
            _cancellation?.Cancel();
            return;
        }
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        AppUpdater.SkipVersion(_manifest.Version);
        Close();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        await RunUpdateAsync().ConfigureAwait(true);
    }

    private async Task RunUpdateAsync()
    {
        _working = true;
        _cancellation = new CancellationTokenSource();
        InstallButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        LaterButton.Content = Words.Cancel;
        ErrorLabel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressLabel.Text = Words.UpdateStarting;

        try
        {
            AppUpdater.CleanupOldDownloads(_manifest.Version);
            var progress = new Progress<UpdateProgress>(Report);
            using var client = new UpdateClient(AppUpdater.ManifestUrl);
            string setup = await client
                .DownloadAsync(_manifest, AppUpdater.DownloadDirectory, progress, _cancellation.Token)
                .ConfigureAwait(true);

            DownloadProgress.Value = 1.0;
            ProgressLabel.Text = Words.UpdateHandingOver;
            LaterButton.IsEnabled = false;

            // From here Setup owns the screen. Spotnet closes so its files can be replaced.
            _handedOver = true;
            AppUpdater.InstallAndRestart(setup);
        }
        catch (OperationCanceledException)
        {
            Log.Info("The user cancelled the update download.");
            ResetAfterFailure(null);
        }
        catch (UpdateVerificationException ex)
        {
            Log.Error("Update download rejected: {0}", ex.Message);
            ResetAfterFailure(Words.UpdateDownloadCorrupt);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Update download failed: {0}", ex.Message);
            ResetAfterFailure(string.Format(Words.UpdateFailed, ex.Message));
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            ResetAfterFailure(string.Format(Words.UpdateFailed, ex.Message));
        }
    }

    private void Report(UpdateProgress progress)
    {
        DownloadProgress.Value = progress.Fraction;
        string received = AppHelper.FormatSizeMegaBytes(progress.Received / 1048576.0);
        string total = AppHelper.FormatSizeMegaBytes(progress.Total / 1048576.0);
        string speed = progress.BytesPerSecond > 0.0
            ? AppHelper.FormatSizeMegaBytes(progress.BytesPerSecond / 1048576.0) + "/s"
            : string.Empty;
        ProgressLabel.Text = string.Format(Words.UpdateDownloading, received, total, speed).TrimEnd();
    }

    private void ResetAfterFailure(string message)
    {
        _working = false;
        _cancellation?.Dispose();
        _cancellation = null;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DownloadProgress.Value = 0.0;
        InstallButton.IsEnabled = true;
        SkipButton.IsEnabled = true;
        LaterButton.IsEnabled = true;
        LaterButton.Content = Words.UpdateLater;
        if (string.IsNullOrEmpty(message)) return;
        ErrorLabel.Text = message;
        ErrorLabel.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Closing mid-download would leave the transfer running with nothing to show it.
        if (_working && !_handedOver)
        {
            e.Cancel = true;
            _cancellation?.Cancel();
            return;
        }
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
