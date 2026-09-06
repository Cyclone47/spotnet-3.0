using System;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using Spotnet.Remote;
using Spotnet.Properties;

namespace Spotnet.Controls;

public partial class RemotePairingWindow : MetroWindow
{
    private readonly PendingPairing _pairing;
    private string _currentPairingUrl;
    private readonly DispatcherTimer _timer;
    private readonly int _initialDevicesCount;
    private bool _hasTunnel;

    public RemotePairingWindow()
    {
        InitializeComponent();

        _pairing = RemoteAuthManager.Instance.CreatePairingSession();
        _initialDevicesCount = RemoteConfig.Load().PairedDevices.Count;

        CloudflareTunnelService.Instance.StateChanged += OnTunnelStateChanged;
        CloudflareTunnelService.Instance.DownloadProgressChanged += OnTunnelDownloadProgressChanged;

        var config = RemoteConfig.Load();
        var tunnelState = CloudflareTunnelService.Instance.State;
        _hasTunnel = tunnelState == TunnelState.Running &&
                     !string.IsNullOrEmpty(CloudflareTunnelService.Instance.TunnelUrl);

        if (config.EnableCloudflareTunnel || _hasTunnel)
        {
            TunnelModeRadio.IsChecked = true;
            if (tunnelState == TunnelState.Stopped)
            {
                int port = RemoteServer.Instance.ActivePort > 0 ? RemoteServer.Instance.ActivePort : config.Port;
                _ = CloudflareTunnelService.Instance.StartAsync(port);
            }
        }
        else
        {
            LanModeRadio.IsChecked = true;
        }

        UpdatePairingUrlAndQr();

        // Timer for countdown & pairing detection
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void OnTunnelStateChanged(TunnelState state, string msg)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (state == TunnelState.Running && !string.IsNullOrEmpty(CloudflareTunnelService.Instance.TunnelUrl))
            {
                _hasTunnel = true;
                if (TunnelModeRadio.IsChecked == true)
                {
                    UpdatePairingUrlAndQr();
                }
            }
            else if (state == TunnelState.Downloading || state == TunnelState.Starting)
            {
                if (TunnelModeRadio.IsChecked == true)
                {
                    UpdatePairingUrlAndQr();
                }
            }
            else if (state == TunnelState.Failed)
            {
                if (TunnelModeRadio.IsChecked == true)
                {
                    TunnelConnectingBanner.Visibility = Visibility.Visible;
                    TunnelConnectingText.Text = string.Format(Words.PairTunnelError, msg);
                }
            }
        });
    }

    private void OnTunnelDownloadProgressChanged(int pct)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (TunnelModeRadio.IsChecked == true)
            {
                if (TunnelConnectingBanner.Visibility == Visibility.Visible)
                {
                    TunnelConnectingText.Text = $"Component downloaden ({pct}%)...";
                }
                if (QrLoadingOverlay.Visibility == Visibility.Visible)
                {
                    QrLoadingText.Text = $"Component downloaden ({pct}%)...";
                    QrLoadingProgressBar.IsIndeterminate = false;
                    QrLoadingProgressBar.Value = pct;
                }
            }
        });
    }

    private void UpdatePairingUrlAndQr()
    {
        bool isTunnelSelected = TunnelModeRadio.IsChecked == true;
        var tunnelState = CloudflareTunnelService.Instance.State;
        _hasTunnel = tunnelState == TunnelState.Running && !string.IsNullOrEmpty(CloudflareTunnelService.Instance.TunnelUrl);

        if (isTunnelSelected)
        {
            ConnectionModeHintTextBlock.Text = "Verbind overal buitenshuis (4G/5G). Geen router-instellingen nodig.";

            if (_hasTunnel)
            {
                QrLoadingOverlay.Visibility = Visibility.Collapsed;
                QrCodeBorder.Visibility = Visibility.Visible;
                TunnelNoticeBanner.Visibility = Visibility.Visible;
                TunnelConnectingBanner.Visibility = Visibility.Collapsed;
                CopyLinkButton.IsEnabled = true;

                string baseUrl = CloudflareTunnelService.Instance.TunnelUrl;
                _currentPairingUrl = baseUrl;
                ActiveUrlTextBlock.Text = _currentPairingUrl;

                var qrBitmap = QrCodeHelper.GenerateQrCodeBitmap(_currentPairingUrl, pixelsPerModule: 8);
                QrCodeImage.Source = qrBitmap;

                string rawPin = _pairing.Pin;
                PinCodeTextBlock.Text = rawPin.Length == 6 ? $"{rawPin.Substring(0, 3)} {rawPin.Substring(3)}" : rawPin;
            }
            else
            {
                QrLoadingOverlay.Visibility = Visibility.Visible;
                QrCodeBorder.Visibility = Visibility.Collapsed;
                TunnelNoticeBanner.Visibility = Visibility.Collapsed;
                TunnelConnectingBanner.Visibility = Visibility.Visible;
                CopyLinkButton.IsEnabled = false;

                if (tunnelState == TunnelState.Downloading)
                {
                    string downloadText = $"Component downloaden ({CloudflareTunnelService.Instance.DownloadPercentage}%)...";
                    QrLoadingText.Text = downloadText;
                    QrLoadingProgressBar.IsIndeterminate = false;
                    QrLoadingProgressBar.Value = CloudflareTunnelService.Instance.DownloadPercentage;
                    TunnelConnectingText.Text = downloadText;
                }
                else
                {
                    QrLoadingText.Text = "Cloudflare tunnel verbinden... Even geduld.";
                    QrLoadingProgressBar.IsIndeterminate = true;
                    TunnelConnectingText.Text = Words.PairTunnelStarting;
                }

                _currentPairingUrl = "";
                ActiveUrlTextBlock.Text = "Verbinding maken met Cloudflare Quick Tunnel...";
                PinCodeTextBlock.Text = "--- ---";
            }
        }
        else
        {
            ConnectionModeHintTextBlock.Text = Words.PairModeLanHint;
            QrLoadingOverlay.Visibility = Visibility.Collapsed;
            QrCodeBorder.Visibility = Visibility.Visible;
            TunnelNoticeBanner.Visibility = Visibility.Collapsed;
            TunnelConnectingBanner.Visibility = Visibility.Collapsed;
            CopyLinkButton.IsEnabled = true;

            string baseUrl = RemoteServer.Instance.GetRemoteUrl(useLanIp: true);
            _currentPairingUrl = baseUrl;
            ActiveUrlTextBlock.Text = _currentPairingUrl;

            var qrBitmap = QrCodeHelper.GenerateQrCodeBitmap(_currentPairingUrl, pixelsPerModule: 8);
            QrCodeImage.Source = qrBitmap;

            string rawPin = _pairing.Pin;
            PinCodeTextBlock.Text = rawPin.Length == 6 ? $"{rawPin.Substring(0, 3)} {rawPin.Substring(3)}" : rawPin;
        }
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            if (TunnelModeRadio.IsChecked == true && CloudflareTunnelService.Instance.State == TunnelState.Stopped)
            {
                int port = RemoteServer.Instance.ActivePort > 0 ? RemoteServer.Instance.ActivePort : RemoteConfig.Load().Port;
                _ = CloudflareTunnelService.Instance.StartAsync(port);
            }
            UpdatePairingUrlAndQr();
        }
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        var remaining = _pairing.ExpiresAt - DateTime.UtcNow;
        if (remaining.TotalSeconds <= 0)
        {
            _timer.Stop();
            ExpiryTextBlock.Text = "Koppelcode is verlopen. Sluit en heropen dit venster.";
            return;
        }

        ExpiryTextBlock.Text = $"Code is nog {remaining.Minutes:D2}:{remaining.Seconds:D2} minuten geldig";

        // Check if a new device has been paired
        var currentConfig = RemoteConfig.Load();
        if (currentConfig.PairedDevices.Count > _initialDevicesCount)
        {
            _timer.Stop();
            var latest = currentConfig.PairedDevices[^1];
            PairedSuccessBanner.Visibility = Visibility.Visible;
            PairedSuccessText.Text = string.Format(Words.PairSucceededNamed, latest.Name);

            // Auto-close after 2 seconds
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            closeTimer.Tick += (s, args) =>
            {
                closeTimer.Stop();
                Close();
            };
            closeTimer.Start();
        }
    }

    private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentPairingUrl))
        {
            MessageBox.Show(Words.PairLinkNotReady, Words.PairLinkNotReadyTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Clipboard.SetText(_currentPairingUrl);
            MessageBox.Show("Koppelingslink gekopieerd naar klembord!", "Gekopieerd", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Words.PairCopyFailed + ex.Message, Words.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        CloudflareTunnelService.Instance.StateChanged -= OnTunnelStateChanged;
        CloudflareTunnelService.Instance.DownloadProgressChanged -= OnTunnelDownloadProgressChanged;
        _timer?.Stop();
        base.OnClosed(e);
    }
}
