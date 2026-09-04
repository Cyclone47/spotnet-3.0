using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class SettingsForCommon : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public SettingsForCommon()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            Settings.Default.SystemTray = SystemTray.IsChecked.GetValueOrDefault();
            Settings.Default.ShowDesktopNotifications = ShowDesktopNotifications.IsChecked.GetValueOrDefault();
            Settings.Default.GoogleSuggest = ShowSug.IsChecked.GetValueOrDefault();
            if (Settings.Default.UseSocksProxy != UseSocks5Proxy.IsChecked.GetValueOrDefault())
            {
                SocksProxy.ChangeState(UseSocks5Proxy.IsChecked.GetValueOrDefault());
            }

            bool downloadExternalLists = Settings.Default.DownloadExternalLists;
            Settings.Default.DownloadExternalLists = DownloadExternalLists.IsChecked.GetValueOrDefault();
            Settings.Default.Save();
            if (Settings.Default.DownloadExternalLists != downloadExternalLists && Settings.Default.DownloadExternalLists)
            {
                BlackAndWhite.UpdateExternalListsAsync();
            }

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
        ShowSug.IsChecked = Settings.Default.GoogleSuggest;
        SystemTray.IsChecked = Settings.Default.SystemTray;
        ShowDesktopNotifications.IsChecked = Settings.Default.ShowDesktopNotifications;
        DownloadExternalLists.IsChecked = Settings.Default.DownloadExternalLists;
        UseSocks5Proxy.IsChecked = Settings.Default.UseSocksProxy;
        UseSocks5Proxy.Visibility = ((!SocksProxy.GlobalyEnabled) ? Visibility.Collapsed : Visibility.Visible);
    }

    public bool VerifyFields()
    {
        return true;
    }

    /// <summary>
    /// Raises one notification now, so the delivery path can be checked without waiting for
    /// a download to finish.
    /// </summary>
    private void TestNotificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        NotificationHelper.ShowTest();
    }
}
