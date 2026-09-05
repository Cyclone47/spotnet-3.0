using System;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using Spotnet.Remote;

namespace Spotnet.Controls;

public partial class RemotePairingWindow : MetroWindow
{
    private readonly PendingPairing _pairing;
    private readonly string _pairingUrl;
    private readonly DispatcherTimer _timer;
    private readonly int _initialDevicesCount;

    public RemotePairingWindow()
    {
        InitializeComponent();

        _pairing = RemoteAuthManager.Instance.CreatePairingSession();
        _initialDevicesCount = RemoteConfig.Load().PairedDevices.Count;

        string baseUrl = RemoteServer.Instance.GetRemoteUrl(useLanIp: true);
        _pairingUrl = $"{baseUrl}/?pairToken={_pairing.Token}";

        // Format PIN as 123-456
        string rawPin = _pairing.Pin;
        if (rawPin.Length == 6)
        {
            PinCodeTextBlock.Text = $"{rawPin.Substring(0, 3)} {rawPin.Substring(3)}";
        }
        else
        {
            PinCodeTextBlock.Text = rawPin;
        }

        // Generate QR code
        var qrBitmap = QrCodeHelper.GenerateQrCodeBitmap(_pairingUrl, pixelsPerModule: 8);
        QrCodeImage.Source = qrBitmap;

        // Timer for countdown & pairing detection
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
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
            PairedSuccessText.Text = $"✓ Apparaat '{latest.Name}' succesvol gekoppeld!";

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
        try
        {
            Clipboard.SetText(_pairingUrl);
            MessageBox.Show("Koppelingslink gekopieerd naar klembord!", "Gekopieerd", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kon link niet kopiëren: " + ex.Message, "Fout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}
