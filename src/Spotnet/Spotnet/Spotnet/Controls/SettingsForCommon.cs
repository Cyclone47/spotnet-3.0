using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Localization;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SettingsForCommon : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

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

            if (!SaveStartupBehaviour())
            {
                return false;
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

    /// <summary>
    /// Writes the "Opstartgedrag" fields. The registration in Windows happens before the setting
    /// is stored, so a refused change leaves the stored value telling the truth instead of
    /// promising a start that will never happen. The start screen and filter are read on the next
    /// launch only - there is nothing to apply to the running window.
    /// </summary>
    private bool SaveStartupBehaviour()
    {
        bool wantRunAtStartup = RunAtStartupCheck.IsChecked.GetValueOrDefault();
        if (wantRunAtStartup != Settings.Default.RunAtStartup && !StartupHelper.SetEnabled(wantRunAtStartup))
        {
            RunAtStartupCheck.IsChecked = Settings.Default.RunAtStartup;
            AppHelper.Error(Words.RunAtStartupFailed);
            return false;
        }

        Settings.Default.RunAtStartup = wantRunAtStartup;
        Settings.Default.DefaultStartScreen = SelectedStartScreen();
        // Stored even when the picker is disabled by a Downloads start screen, so switching
        // back to Overzicht restores the filter the user had chosen rather than losing it.
        Settings.Default.DefaultFilterPath = (DefaultFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return true;
    }

    private string SelectedStartScreen()
    {
        return (StartScreenCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? StartScreen.Spots;
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        ShowSug.IsChecked = Settings.Default.GoogleSuggest;
        SystemTray.IsChecked = Settings.Default.SystemTray;
        ShowDesktopNotifications.IsChecked = Settings.Default.ShowDesktopNotifications;
        UseSocks5Proxy.IsChecked = Settings.Default.UseSocksProxy;
        UseSocks5Proxy.Visibility = ((!SocksProxy.GlobalyEnabled) ? Visibility.Collapsed : Visibility.Visible);
        RunAtStartupCheck.IsChecked = Settings.Default.RunAtStartup;
        PopulateStartScreen();
        PopulateFilters();
        UpdateFilterPickerEnabled();
    }

    private void PopulateStartScreen()
    {
        string wanted = Settings.Default.DefaultStartScreen;
        ComboBoxItem match = null;
        foreach (ComboBoxItem item in StartScreenCombo.Items)
        {
            if ((item.Tag as string).EqualsIgnoreCase(wanted))
            {
                match = item;
                break;
            }
        }

        // Anything unrecognized - a hand-edited config, or a value from a future version -
        // falls back to Overzicht, which is how the app has always started.
        StartScreenCombo.SelectedItem = match ?? StartScreenCombo.Items[0];
    }

    /// <summary>
    /// Offers every filter of the loaded filter set, indented to mirror the tree in the left
    /// panel, behind a first entry meaning "no default filter". Values are stored as
    /// <see cref="FilterViewModel.FullPathString" /> - the same identity the saved expansion
    /// state uses - because names alone are only unique among siblings.
    /// </summary>
    private void PopulateFilters()
    {
        DefaultFilterCombo.Items.Clear();
        DefaultFilterCombo.Items.Add(new ComboBoxItem
        {
            Tag = "",
            Content = Words.DefaultFilterNone
        });

        string stored = Settings.Default.DefaultFilterPath;
        ComboBoxItem selected = null;
        foreach (FilterViewModel filter in CurrentFilters())
        {
            // A node the left panel could not apply - hidden, or a group with no query of its
            // own - would change nothing at startup, so it is not offered here either.
            if (!filter.IsVisible || filter.Name.IsNullOrWhiteSpace() || filter.Query.IsNullOrWhiteSpace())
            {
                continue;
            }

            var item = new ComboBoxItem
            {
                Tag = filter.FullPathString,
                Content = Indent(filter.NestingLevel) + FilterTranslationHelper.GetTranslatedName(filter.Name).Trim()
            };
            DefaultFilterCombo.Items.Add(item);
            if (selected == null && stored.Equals(filter.FullPathString, StringComparison.Ordinal))
            {
                selected = item;
            }
        }

        // A stored filter that is gone - renamed, deleted, or from a different filter set -
        // shows as "no filter" rather than silently starting on something the user never picked.
        DefaultFilterCombo.SelectedItem = selected ?? DefaultFilterCombo.Items[0];
    }

    /// <summary>
    /// Top-level filters already sit at nesting level 1, so the first level gets no indent.
    /// </summary>
    private static string Indent(int nestingLevel)
    {
        return new string(' ', Math.Max(0, nestingLevel - 1) * 3);
    }

    private static IEnumerable<FilterViewModel> CurrentFilters()
    {
        try
        {
            return MainWindowVm?.GetCompleteFiltersList() ?? Enumerable.Empty<FilterViewModel>();
        }
        catch (Exception ex)
        {
            // A missing or half-built view model leaves the picker with just its "no filter"
            // entry; that is a usable dialog, so it is logged rather than raised.
            Log.Warn(ex, "Filter list unavailable while building the default filter picker");
            return Enumerable.Empty<FilterViewModel>();
        }
    }

    /// <summary>The default filter only has an effect on the Overzicht screen.</summary>
    private void UpdateFilterPickerEnabled()
    {
        if (DefaultFilterPanel != null)
        {
            DefaultFilterPanel.IsEnabled = !StartScreen.IsDownloads(SelectedStartScreen());
        }
    }

    private void StartScreenCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFilterPickerEnabled();
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
