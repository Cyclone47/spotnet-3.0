using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Markup;
using System.Windows.Media;
using NLog;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class SettingsForAdvancedDownload : System.Windows.Controls.UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    public Action<string> DownloadFolderChanged;
    private string _lastDownloadFolder;
    private bool IsExternalSelected => ExternalNzbGetRadioButton.IsChecked.GetValueOrDefault();

    public SettingsForAdvancedDownload()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
        _lastDownloadFolder = DownloaderProps.MainDir;
        DownloadFolderChanged = (Action<string>)Delegate.Combine(DownloadFolderChanged, new Action<string>(OnDownloadFolderChanged));
    }

    private void OnDownloadFolderChanged(string s)
    {
        UpdateDownloadFolderRelatedFields(s);
        _lastDownloadFolder = s;
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            Settings.Default.ExternalNzbGet = IsExternalSelected;
            if (IsExternalSelected)
            {
                Settings.Default.NzbGetControlIP = AddressTextBox.Text.ToLower();
                Settings.Default.NzbGetControlPort = PortTextBox.Text;
                Settings.Default.NzbGetControlUsername = UserNameTextBox.Text;
                Settings.Default.NzbGetControlPassword = PasswordTextBox.Password;
            }
            else
            {
                Settings.Default.NzbGetDestDir = (CustomDestDir.IsChecked.GetValueOrDefault() ? DestDirTextBox.Text : "-");
                Settings.Default.NzbGetInterDir = (CustomInterDir.IsChecked.GetValueOrDefault() ? InterDirTextBox.Text : "-");
                Settings.Default.NzbGetQueueDir = (CustomQueueDir.IsChecked.GetValueOrDefault() ? QueueDirTextBox.Text : "-");
                Settings.Default.NzbGetServer1Host = (CustomServer1Host.IsChecked.GetValueOrDefault() ? Server1HostTextBox.Text : "-");
                Settings.Default.NzbGetServer1Port = (CustomServer1Port.IsChecked.GetValueOrDefault() ? Server1PortTextBox.Text : "-");
                Settings.Default.NzbGetServer1Username = (CustomServer1Username.IsChecked.GetValueOrDefault() ? Server1UsernameTextBox.Text : "-");
                Settings.Default.NzbGetServer1Password = (CustomServer1Password.IsChecked.GetValueOrDefault() ? Server1PasswordBox.Password : "-");
                Settings.Default.NzbGetServer1Encryption = ((!CustomServer1Encryption.IsChecked.GetValueOrDefault()) ? "-" : ((Server1EncryptionComboBox.SelectedIndex == 0) ? "yes" : "no"));
            }

            Settings.Default.Save();
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        ExternalNzbGetRadioButton.IsChecked = Settings.Default.ExternalNzbGet;
        AddressTextBox.Text = DownloaderProps.ControlIp;
        PortTextBox.Text = DownloaderProps.ControlPort;
        UserNameTextBox.Text = DownloaderProps.ControlUsername;
        PasswordTextBox.Password = DownloaderProps.ControlPassword;
        InternalNzbGetSection.Visibility = Visibility.Visible;
        InternalNzbGetRadioButton.IsChecked = !Settings.Default.ExternalNzbGet;
        DestDirTextBox.Text = DownloaderProps.DestDir;
        InterDirTextBox.Text = DownloaderProps.InterDir;
        QueueDirTextBox.Text = DownloaderProps.QueueDir;
        Server1HostTextBox.Text = DownloaderProps.Server1Host;
        Server1PortTextBox.Text = DownloaderProps.Server1Port;
        Server1UsernameTextBox.Text = DownloaderProps.Server1Username;
        Server1PasswordBox.Password = DownloaderProps.Server1Password;
        Server1EncryptionComboBox.SelectedIndex = ((!DownloaderProps.Server1Encryption.Equals("yes")) ? 1 : 0);
        CustomDestDir.IsChecked = DownloaderProps.DestDirIsCustom;
        CustomInterDir.IsChecked = DownloaderProps.InterDirIsCustom;
        CustomQueueDir.IsChecked = DownloaderProps.QueueDirIsCustom;
        CustomServer1Host.IsChecked = DownloaderProps.Server1HostIsCustom;
        CustomServer1Port.IsChecked = DownloaderProps.Server1PortIsCustom;
        CustomServer1Username.IsChecked = DownloaderProps.Server1UsernameIsCustom;
        CustomServer1Password.IsChecked = DownloaderProps.Server1PasswordIsCustom;
        CustomServer1Encryption.IsChecked = DownloaderProps.Server1EncryptionIsCustom;
        UpdateFieldsState();
    }

    private void UpdateFieldsState()
    {
        bool valueOrDefault = ExternalNzbGetRadioButton.IsChecked.GetValueOrDefault();
        ExternalNzbGetSection.Visibility = ((!valueOrDefault) ? Visibility.Collapsed : Visibility.Visible);
        InternalNzbGetSection.Visibility = (valueOrDefault ? Visibility.Collapsed : Visibility.Visible);
        if (!valueOrDefault)
        {
            DestDirTextBox.IsEnabled = CustomDestDir.IsChecked.GetValueOrDefault();
            InterDirTextBox.IsEnabled = CustomInterDir.IsChecked.GetValueOrDefault();
            QueueDirTextBox.IsEnabled = CustomQueueDir.IsChecked.GetValueOrDefault();
            Server1HostTextBox.IsEnabled = CustomServer1Host.IsChecked.GetValueOrDefault();
            Server1PortTextBox.IsEnabled = CustomServer1Port.IsChecked.GetValueOrDefault();
            Server1UsernameTextBox.IsEnabled = CustomServer1Username.IsChecked.GetValueOrDefault();
            Server1PasswordBox.IsEnabled = CustomServer1Password.IsChecked.GetValueOrDefault();
            Server1EncryptionComboBox.IsEnabled = CustomServer1Encryption.IsChecked.GetValueOrDefault();
        }
    }

    public bool VerifyFields()
    {
        List<System.Windows.Controls.Control> source = ((!IsExternalSelected) ? new List<System.Windows.Controls.Control>
        {
            DestDirTextBox,
            InterDirTextBox,
            QueueDirTextBox,
            Server1HostTextBox,
            Server1PortTextBox,
            Server1UsernameTextBox,
            Server1PasswordBox,
            Server1EncryptionComboBox
        }

        : new List<System.Windows.Controls.Control>
        {
            AddressTextBox,
            PortTextBox,
            UserNameTextBox,
            PasswordTextBox
        }

        );
        return !source.Any((System.Windows.Controls.Control f) => object.Equals(f.Background, _fieldInvalidBackground));
    }

    private bool ValidateAddress(System.Windows.Controls.TextBox textBox)
    {
        bool flag = !textBox.Text.Trim().IsNullOrEmpty() && (textBox.Text.EqualsIgnoreCase("localhost") || Regex.IsMatch(textBox.Text, " # Rev:2013-03-26\r\n                                                                        # Match DNS host domain having one or more subdomains.\r\n                                                                        # Top level domain subset taken from IANA.ORG. See:\r\n                                                                        # http://data.iana.org/TLD/tlds-alpha-by-domain.txt\r\n                                                                        ^                  # Anchor to start of string.\r\n                                                                        (?!.{256})         # Whole domain must be 255 or less.\r\n                                                                        (?:                # Group for one or more sub-domains.\r\n                                                                          [a-z0-9]         # Either subdomain length from 2-63.\r\n                                                                          [a-z0-9-]{0,61}  # Middle part may have dashes.\r\n                                                                          [a-z0-9]         # Starts and ends with alphanum.\r\n                                                                          \\.               # Dot separates subdomains.\r\n                                                                        | [a-z0-9]         # or subdomain length == 1 char.\r\n                                                                          \\.               # Dot separates subdomains.\r\n                                                                        )+                 # One or more sub-domains.\r\n                                                                        (?:                # Top level domain alternatives.\r\n                                                                          [a-z]{2}         # Either any 2 char country code,\r\n                                                                        | AERO|ARPA|ASIA|BIZ|CAT|COM|COOP|EDU|  # or TLD \r\n                                                                          GOV|INFO|INT|JOBS|MIL|MOBI|MUSEUM|    # from list.\r\n                                                                          NAME|NET|ORG|POST|PRO|TEL|TRAVEL|XXX  # IANA.ORG\r\n                                                                        )                  # End group of TLD alternatives.\r\n                                                                        $                  # Anchor to end of string.", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace) || Regex.IsMatch(textBox.Text, "^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$"));
        textBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private bool ValidatePort(System.Windows.Controls.TextBox textBox)
    {
        int result;
        bool flag = int.TryParse(textBox.Text, out result);
        textBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private bool ValidateUsername(System.Windows.Controls.TextBox control)
    {
        bool flag = control.Text.Length < 200;
        control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private bool ValidatePassword(PasswordBox control)
    {
        bool flag = control.Password.Length < 200;
        control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private void UpdateDownloadFolderRelatedFields(string downloadDir)
    {
        string downloadFolder = Settings.Default.DownloadFolder;
        try
        {
            Settings.Default.DownloadFolder = downloadDir;
            if (!CustomDestDir.IsChecked.GetValueOrDefault())
            {
                DestDirTextBox.Text = DownloaderProps.DefaultDestDir;
            }

            if (!CustomInterDir.IsChecked.GetValueOrDefault())
            {
                InterDirTextBox.Text = DownloaderProps.DefaultInterDir;
            }
        }
        finally
        {
            Settings.Default.DownloadFolder = downloadFolder;
        }
    }

    private void DestDir_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        System.Windows.Controls.TextBox obj = (System.Windows.Controls.TextBox)sender;
        obj.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, (!obj.Text.IsNullOrWhiteSpace()) ? "WhiteColorBrush" : "NoticeBackgroundBrush");
    }

    private void InterDir_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        System.Windows.Controls.TextBox obj = (System.Windows.Controls.TextBox)sender;
        obj.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, (!obj.Text.IsNullOrWhiteSpace()) ? "WhiteColorBrush" : "NoticeBackgroundBrush");
    }

    private void QueueDir_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        System.Windows.Controls.TextBox obj = (System.Windows.Controls.TextBox)sender;
        obj.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, (!obj.Text.IsNullOrWhiteSpace()) ? "WhiteColorBrush" : "NoticeBackgroundBrush");
    }

    private void Server1EncryptionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateAddress(sender as System.Windows.Controls.TextBox);
    }

    private void UserNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateUsername(sender as System.Windows.Controls.TextBox);
    }

    private void PasswordTextBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ValidatePassword(sender as PasswordBox);
    }

    private void PortTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidatePort(sender as System.Windows.Controls.TextBox);
    }

    private void UpdateTextBox(System.Windows.Controls.TextBox textbox, System.Windows.Controls.CheckBox checkbox, string defaultValue)
    {
        if (checkbox != null && textbox != null)
        {
            textbox.IsEnabled = checkbox.IsChecked.GetValueOrDefault();
            if (!textbox.IsEnabled)
            {
                textbox.Text = defaultValue;
            }
        }
    }

    private void CustomDestDir_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(DestDirTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultDestDir);
        UpdateDownloadFolderRelatedFields(_lastDownloadFolder);
    }

    private void CustomInterDir_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(InterDirTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultInterDir);
        UpdateDownloadFolderRelatedFields(_lastDownloadFolder);
    }

    private void CustomQueueDir_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(QueueDirTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultQueueDir);
    }

    private void CustomServer1Host_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(Server1HostTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultServer1Host);
    }

    private void CustomServer1Port_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(Server1PortTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultServer1Port);
    }

    private void CustomServer1Username_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateTextBox(Server1UsernameTextBox, sender as System.Windows.Controls.CheckBox, DownloaderProps.DefaultServer1Username);
    }

    private void CustomServer1Password_OnClick(object sender, RoutedEventArgs e)
    {
        PasswordBox server1PasswordBox = Server1PasswordBox;
        server1PasswordBox.IsEnabled = CustomServer1Password.IsChecked.GetValueOrDefault();
        if (!server1PasswordBox.IsEnabled)
        {
            server1PasswordBox.Password = DownloaderProps.DefaultServer1Password;
        }
    }

    private void CustomServer1Encryption_OnClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Controls.ComboBox server1EncryptionComboBox = Server1EncryptionComboBox;
        server1EncryptionComboBox.IsEnabled = CustomServer1Encryption.IsChecked.GetValueOrDefault();
        if (!server1EncryptionComboBox.IsEnabled)
        {
            server1EncryptionComboBox.SelectedIndex = ((!DownloaderProps.DefaultServer1Encryption.Equals("yes")) ? 1 : 0);
        }
    }

    private string BrowseFolder(string defaultPath)
    {
        FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            SelectedPath = defaultPath
        };
        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
        {
            return folderBrowserDialog.SelectedPath;
        }

        return null;
    }

    private void DestDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        string text = BrowseFolder(DownloaderProps.DestDir);
        if (!text.IsNullOrWhiteSpace())
        {
            DestDirTextBox.Text = text;
        }
    }

    private void InterDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        string text = BrowseFolder(DownloaderProps.InterDir);
        if (!text.IsNullOrWhiteSpace())
        {
            InterDirTextBox.Text = text;
        }
    }

    private void QueueDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        string text = BrowseFolder(DownloaderProps.QueueDir);
        if (!text.IsNullOrWhiteSpace())
        {
            QueueDirTextBox.Text = text;
        }
    }

    private void ExternalNzbGetRadioButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateFieldsState();
    }

    private void InternalDownloaderRadioButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateFieldsState();
    }
}
