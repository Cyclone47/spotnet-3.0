using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spotnet.Remote;

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
    }

    private void LoadConfig(RemoteConfig config)
    {
        _config = config ?? RemoteConfig.Load();
        EnableRemoteCheckBox.IsChecked = _config.Enabled;
        PortTextBox.Text = _config.Port.ToString();
        AllowLanCheckBox.IsChecked = _config.AllowLan;
        RequireAuthCheckBox.IsChecked = _config.RequireAuth;
        KeepAwakeCheckBox.IsChecked = _config.KeepAwake;

        UpdateUiState();
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
            ServerStatusTextBlock.Text = "● Actief";
            ServerStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            string url = RemoteServer.Instance.GetRemoteUrl(_config.AllowLan);
            ServerUrlTextBlock.Text = url;
            OpenBrowserButton.IsEnabled = true;
            PairDeviceButton.IsEnabled = true;
        }
        else if (isEnabled)
        {
            ServerStatusTextBlock.Text = "● Starten mislukt (poort mogelijk bezet)";
            ServerStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            ServerUrlTextBlock.Text = $"http://{RemoteServer.GetLocalIpAddress()}:{_config.Port}";
            OpenBrowserButton.IsEnabled = false;
            PairDeviceButton.IsEnabled = false;
        }
        else
        {
            ServerStatusTextBlock.Text = "Uitgeschakeld";
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

    private void UpdatePasswordHint()
    {
        bool configured = !string.IsNullOrEmpty(_pendingPassword) || !string.IsNullOrEmpty(_config.PasswordHash);
        AuthPasswordHintTextBlock.Text = configured ? "Wachtwoord ingesteld" : "Nog geen wachtwoord ingesteld";
        ChangePasswordButton.Content = configured ? "Wachtwoord wijzigen…" : "Wachtwoord instellen…";
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
            MessageBox.Show("Kon browser niet openen: " + ex.Message, "Fout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PairDeviceButton_Click(object sender, RoutedEventArgs e)
    {
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
            if (MessageBox.Show("Wil je de toegang voor dit apparaat intrekken?", "Apparaat ontkoppelen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                RemoteAuthManager.Instance.RevokeDevice(deviceId);
                RefreshDevicesList();
            }
        }
    }

    private void RevokeAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Weet je zeker dat je ALLE mobiele apparaten wilt ontkoppelen?", "Alle apparaten intrekken", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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
                    MessageBox.Show("Stel eerst een wachtwoord in voor Spotnet Remote.", "Wachtwoord vereist", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        _config.Enabled = EnableRemoteCheckBox.IsChecked == true;
        if (int.TryParse(PortTextBox.Text.Trim(), out int port))
        {
            _config.Port = port;
        }
        _config.AllowLan = AllowLanCheckBox.IsChecked == true;
        _config.RequireAuth = RequireAuthCheckBox.IsChecked == true;
        _config.KeepAwake = KeepAwakeCheckBox.IsChecked == true;
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
                // Server was already running, just update sleep preventer
                SleepPreventer.UpdateState(_config.KeepAwake);
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
        return true;
    }
}
