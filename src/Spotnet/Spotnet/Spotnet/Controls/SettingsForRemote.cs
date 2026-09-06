using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spotnet.Remote;
using Spotnet.Properties;

namespace Spotnet.Controls;

public partial class SettingsForRemote : UserControl, IAdvancedSettingsControl
{
    private RemoteConfig _config;
    private string _pendingPassword;
    private readonly Func<string> _requestPassword;

    public SettingsForRemote() : this(null, null) { }

    internal SettingsForRemote(RemoteConfig config, Func<string> requestPassword)
    {
        _requestPassword = requestPassword ?? ShowPasswordDialog;
        InitializeComponent();
        LoadConfig(config);

        CloudflareTunnelService.Instance.StateChanged += OnTunnelStateChanged;
        CloudflareTunnelService.Instance.DownloadProgressChanged += OnTunnelDownloadProgressChanged;
        Unloaded += (s, e) =>
        {
            CloudflareTunnelService.Instance.StateChanged -= OnTunnelStateChanged;
            CloudflareTunnelService.Instance.DownloadProgressChanged -= OnTunnelDownloadProgressChanged;
        };
    }

    private void OnTunnelStateChanged(TunnelState state, string msg)
    {
        Dispatcher.InvokeAsync(UpdateCloudflareUiState);
    }

    private void OnTunnelDownloadProgressChanged(int pct)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (CloudflareDownloadProgressBar.Visibility == Visibility.Visible)
            {
                CloudflareDownloadProgressBar.Value = pct;
            }
        });
    }

    private void LoadConfig(RemoteConfig config)
    {
        _config = config ?? RemoteConfig.Load();
        EnableRemoteCheckBox.IsChecked = _config.Enabled;
        PortTextBox.Text = _config.Port.ToString();
        AllowLanCheckBox.IsChecked = _config.AllowLan;
        RequireAuthCheckBox.IsChecked = _config.RequireAuth;
        KeepAwakeCheckBox.IsChecked = _config.KeepAwake;
        EnableCloudflareTunnelCheckBox.IsChecked = _config.EnableCloudflareTunnel;

        UpdateUiState();
        UpdateCloudflareUiState();
        UpdateAuthUiState();
        UpdatePasswordHint();
        PairedDevicesListBox.ItemsSource = _config.PairedDevices.OrderByDescending(d => d.LastSeenAt).ToList();
    }

    private void UpdateUiState()
    {
        bool isEnabled = EnableRemoteCheckBox.IsChecked == true;
        RemoteConfigPanel.IsEnabled = true; // Allow configuration while Remote is off.

        if (isEnabled && RemoteServer.Instance.IsRunning)
        {
            ServerStatusTextBlock.Text = Words.RemoteActive;
            ServerStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            string url = RemoteServer.Instance.GetRemoteUrl(_config.AllowLan);
            ServerUrlTextBlock.Text = url;
            OpenBrowserButton.IsEnabled = true;
            PairDeviceButton.IsEnabled = true;
        }
        else if (isEnabled)
        {
            ServerStatusTextBlock.Text = Words.RemoteStartFailed;
            ServerStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            ServerUrlTextBlock.Text = $"http://{RemoteServer.GetLocalIpAddress()}:{_config.Port}";
            OpenBrowserButton.IsEnabled = false;
            PairDeviceButton.IsEnabled = false;
        }
        else
        {
            ServerStatusTextBlock.Text = Words.RemoteDisabled;
            ServerStatusTextBlock.Foreground = Brushes.Gray;
            ServerUrlTextBlock.Text = "-";
            OpenBrowserButton.IsEnabled = false;
            PairDeviceButton.IsEnabled = false;
        }
    }

    private void UpdateAuthUiState()
    {
        bool authRequired = RequireAuthCheckBox.IsChecked == true;
        AuthCredentialsPanel.Visibility = authRequired ? Visibility.Visible : Visibility.Collapsed;
        NoAuthWarningTextBlock.Visibility = authRequired ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateCloudflareUiState()
    {
        bool isTunnelChecked = EnableCloudflareTunnelCheckBox.IsChecked == true;
        CloudflareTunnelPanel.Visibility = isTunnelChecked ? Visibility.Visible : Visibility.Collapsed;

        if (!isTunnelChecked)
        {
            CloudflareStatusTextBlock.Text = Words.RemoteDisabled;
            CloudflareStatusTextBlock.Foreground = Brushes.Gray;
            CloudflareDownloadProgressBar.Visibility = Visibility.Collapsed;
            CloudflareUrlRow.Visibility = Visibility.Collapsed;
            return;
        }

        var state = CloudflareTunnelService.Instance.State;
        switch (state)
        {
            case TunnelState.Downloading:
                CloudflareStatusTextBlock.Text = $"Component downloaden... {CloudflareTunnelService.Instance.DownloadPercentage}%";
                CloudflareStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                CloudflareDownloadProgressBar.Visibility = Visibility.Visible;
                CloudflareDownloadProgressBar.Value = CloudflareTunnelService.Instance.DownloadPercentage;
                CloudflareUrlRow.Visibility = Visibility.Collapsed;
                break;

            case TunnelState.Starting:
                CloudflareStatusTextBlock.Text = "Verbinden met Cloudflare...";
                CloudflareStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                CloudflareDownloadProgressBar.Visibility = Visibility.Collapsed;
                CloudflareUrlRow.Visibility = Visibility.Collapsed;
                break;

            case TunnelState.Running:
                CloudflareStatusTextBlock.Text = Words.RemoteActive;
                CloudflareStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                CloudflareDownloadProgressBar.Visibility = Visibility.Collapsed;
                CloudflareUrlRow.Visibility = Visibility.Visible;
                CloudflareUrlTextBox.Text = CloudflareTunnelService.Instance.TunnelUrl;
                break;

            case TunnelState.Failed:
                CloudflareStatusTextBlock.Text = $"● {CloudflareTunnelService.Instance.StatusMessage}";
                CloudflareStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                CloudflareDownloadProgressBar.Visibility = Visibility.Collapsed;
                CloudflareUrlRow.Visibility = Visibility.Collapsed;
                break;

            case TunnelState.Stopped:
            default:
                if (EnableRemoteCheckBox.IsChecked == true && RemoteServer.Instance.IsRunning)
                {
                    CloudflareStatusTextBlock.Text = Words.RemoteNotStarted;
                }
                else
                {
                    CloudflareStatusTextBlock.Text = Words.RemoteWaitingForServer;
                }
                CloudflareStatusTextBlock.Foreground = Brushes.LightGray;
                CloudflareDownloadProgressBar.Visibility = Visibility.Collapsed;
                CloudflareUrlRow.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void EnableCloudflareTunnelCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateCloudflareUiState();

        if (EnableCloudflareTunnelCheckBox.IsChecked == true)
        {
            // Auto-enable Spotnet Remote if disabled
            if (EnableRemoteCheckBox.IsChecked != true)
            {
                EnableRemoteCheckBox.IsChecked = true;
                UpdateUiState();
            }
        }

        if (EnableRemoteCheckBox.IsChecked == true)
        {
            Save();
        }
    }

    private void UpdatePasswordHint()
    {
        bool configured = !string.IsNullOrEmpty(_pendingPassword) || !string.IsNullOrEmpty(_config.PasswordHash);
        AuthPasswordHintTextBlock.Text = configured ? Words.RemotePasswordConfigured : Words.RemotePasswordNotConfigured;
        ChangePasswordButton.Content = configured ? Words.RemoteChangePassword : Words.RemoteSetPassword;
    }

    private string ShowPasswordDialog()
    {
        var dialog = new RemotePasswordWindow { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    private bool AskForPassword()
    {
        string password = _requestPassword();
        if (password == null) return false;
        _pendingPassword = password;
        UpdatePasswordHint();
        return true;
    }

    private bool EnsurePassword()
    {
        return !string.IsNullOrEmpty(_pendingPassword) ||
            (!string.IsNullOrEmpty(_config.PasswordHash) && !string.IsNullOrEmpty(_config.PasswordSalt)) || AskForPassword();
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (AskForPassword()) Save();
    }
    private void RefreshDevicesList()
    {
        _config = RemoteConfig.Load();
        PairedDevicesListBox.ItemsSource = null;
        PairedDevicesListBox.ItemsSource = _config.PairedDevices.OrderByDescending(d => d.LastSeenAt).ToList();
    }

    private void EnableRemoteCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (EnableRemoteCheckBox.IsChecked == true)
        {
            if ((RequireAuthCheckBox.IsChecked == true && !EnsurePassword()) || !VerifyFields())
            {
                EnableRemoteCheckBox.IsChecked = false;
                UpdateUiState();
                return;
            }
        }
        // Save and start/stop server immediately without waiting for OK
        Save();
    }

    private void AllowLanCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (EnableRemoteCheckBox.IsChecked == true)
        {
            Save();
        }
    }

    private void RequireAuthCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateAuthUiState();
        if (RequireAuthCheckBox.IsChecked == true && !EnsurePassword())
        {
            RequireAuthCheckBox.IsChecked = _config.RequireAuth;
            UpdateAuthUiState();
            return;
        }
        if (VerifyFields())
        {
            Save();
        }
    }

    private void KeepAwakeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        Save();
    }

    private void PortTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (EnableRemoteCheckBox.IsChecked == true && VerifyFields())
        {
            Save();
        }
    }

    private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string url = RemoteServer.Instance.GetRemoteUrl(_config.AllowLan);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Words.RemoteBrowserOpenFailed + ex.Message, Words.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PairDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (EnableRemoteCheckBox.IsChecked != true)
        {
            EnableRemoteCheckBox.IsChecked = true;
            UpdateUiState();
        }

        // Save first so port & settings are up to date and server is active
        if (!VerifyFields()) return;
        Save();

        var pairingWindow = new RemotePairingWindow();
        pairingWindow.Owner = Window.GetWindow(this);
        pairingWindow.ShowDialog();

        RefreshDevicesList();
    }

    private void RevokeDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string deviceId)
        {
            if (MessageBox.Show(Words.RemoteRevokeQuestion, Words.RemoteRevokeTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RemoteAuthManager.Instance.RevokeDevice(deviceId);
                RefreshDevicesList();
            }
        }
    }

    private void RevokeAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Words.RemoteRevokeAllQuestion, Words.RemoteRevokeAllTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            RemoteAuthManager.Instance.RevokeAllDevices();
            RefreshDevicesList();
        }
    }

    public bool VerifyFields()
    {
        if (EnableRemoteCheckBox.IsChecked == true)
        {
            if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port < 1024 || port > 65535)
            {
                MessageBox.Show("Voer een geldig poortnummer in tussen 1024 en 65535.", "Ongeldige poort", MessageBoxButton.OK, MessageBoxImage.Warning);
                PortTextBox.Focus();
                return false;
            }

            if (RequireAuthCheckBox.IsChecked == true)
            {
                if (string.IsNullOrEmpty(_pendingPassword) &&
                    (string.IsNullOrEmpty(_config.PasswordHash) || string.IsNullOrEmpty(_config.PasswordSalt)))
                {
                    MessageBox.Show(Words.RemoteSetPasswordFirst, Words.RemotePasswordRequired, MessageBoxButton.OK, MessageBoxImage.Warning);
                    ChangePasswordButton.Focus();
                    return false;
                }
            }
        }
        return true;
    }

    public bool Save()
    {
        if (!VerifyFields())
        {
            return false;
        }

        bool wasEnabled = _config.Enabled;
        int oldPort = _config.Port;
        bool oldLan = _config.AllowLan;
        bool oldTunnel = _config.EnableCloudflareTunnel;

        _config.Enabled = EnableRemoteCheckBox.IsChecked == true;
        if (int.TryParse(PortTextBox.Text.Trim(), out int port))
        {
            _config.Port = port;
        }
        _config.AllowLan = AllowLanCheckBox.IsChecked == true;
        _config.RequireAuth = RequireAuthCheckBox.IsChecked == true;
        _config.KeepAwake = KeepAwakeCheckBox.IsChecked == true;
        _config.EnableCloudflareTunnel = EnableCloudflareTunnelCheckBox.IsChecked == true;
        if (!string.IsNullOrEmpty(_pendingPassword))
        {
            _config.SetPassword(_pendingPassword);
            _pendingPassword = null;
        }
        _config.Save();
        RemoteAuthManager.Instance.ReloadConfig();

        UpdatePasswordHint();

        // Start, stop or restart server if settings changed
        if (_config.Enabled)
        {
            if (!wasEnabled || oldPort != _config.Port || oldLan != _config.AllowLan)
            {
                RemoteServer.Instance.Restart();
            }
            else if (!RemoteServer.Instance.IsRunning)
            {
                RemoteServer.Instance.Start();
            }
            else
            {
                // Server was already running, just update sleep preventer & tunnel
                SleepPreventer.UpdateState(_config.KeepAwake);
                if (_config.EnableCloudflareTunnel && (!oldTunnel || CloudflareTunnelService.Instance.State == TunnelState.Stopped))
                {
                    _ = CloudflareTunnelService.Instance.StartAsync(_config.Port);
                }
                else if (!_config.EnableCloudflareTunnel && oldTunnel)
                {
                    CloudflareTunnelService.Instance.Stop();
                }
            }
        }
        else if (wasEnabled || RemoteServer.Instance.IsRunning)
        {
            RemoteServer.Instance.Stop();
        }
        else
        {
            SleepPreventer.UpdateState(false);
        }

        UpdateUiState();
        UpdateCloudflareUiState();
        return true;
    }
}
