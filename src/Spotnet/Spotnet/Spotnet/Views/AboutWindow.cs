using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Deployment;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Views;

/// <summary>
/// Help &gt; About. A dialog rather than a tab, so it behaves like Settings: it opens
/// over the window, takes no space in the tab strip, and closes with Escape.
/// </summary>
public partial class AboutWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    internal AboutWindow()
    {
        InitializeComponent();
        VersionLabel.Text = string.Format(Words.AboutVersion, AppHelper.AppVersion);
        UpdateLabel.Text = SquirrelStuff.LastVersion == AppHelper.AppVersion
            ? Words.LatestVersionIsUsed
            : string.Format(Words.NewVersionWillBeInstalledOnNextStart, SquirrelStuff.LastVersion);

        // The headline changes only. The full per-release list stays in Help > What's new,
        // which is generated from the release notes and would go stale if duplicated here.
        ChangesList.ItemsSource = new[]
        {
            Words.AboutChangeRuntime,
            Words.AboutChangeBrowser,
            Words.AboutChangeSearch,
            Words.AboutChangeRemote,
            Words.AboutChangeNotifications,
            Words.AboutChangeUpdates,
            Words.AboutChangeStyles,
            Words.AboutChangeVpn,
            Words.AboutChangeSetup,
        };
    }

    private void ProjectLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            // UseShellExecute so the user's default browser handles it; the application
            // never launches a browser binary itself.
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
