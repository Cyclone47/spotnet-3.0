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
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SettingsForSpotsList : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

    public static event Action ColoringForSpotsChanged;
    public static event Action ColoringForFiltersChanged;
    public SettingsForSpotsList()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    public string GetSettingsToRefreshSpotsList()
    {
        return $"{Settings.Default.ShowEroticaInSearchResults}{Settings.Default.NumOfSpamReportsToSpotHide}{Settings.Default.HideBlacklistedSpots}";
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            string settingsToRefreshSpotsList = GetSettingsToRefreshSpotsList();
            Settings.Default.ShowEroticaInSearchResults = ShowErotica.IsChecked.GetValueOrDefault();
            switch (SpamReportsThresholdCombo.SelectedIndex)
            {
                case 0:
                    Settings.Default.NumOfSpamReportsToSpotHide = 1;
                    break;
                case 1:
                    Settings.Default.NumOfSpamReportsToSpotHide = 2;
                    break;
                case 2:
                    Settings.Default.NumOfSpamReportsToSpotHide = 3;
                    break;
                case 3:
                    Settings.Default.NumOfSpamReportsToSpotHide = 5;
                    break;
                case 4:
                    Settings.Default.NumOfSpamReportsToSpotHide = 7;
                    break;
                case 5:
                    Settings.Default.NumOfSpamReportsToSpotHide = -1;
                    break;
            }

            Settings.Default.HideBlacklistedSpots = HideBlacklistedSpots.IsChecked.GetValueOrDefault();
            MainWindowVm.ShowTrustedOnlyMode = ShowTrustedOnly.IsChecked.GetValueOrDefault();
            Settings.Default.AutoShowNewSpotsInTheList = AutoShowNewSpotsInTheList.IsChecked.GetValueOrDefault();
            bool coloringSpots = Settings.Default.ColoringSpots;
            Settings.Default.ColoringSpots = ColoringSpots.IsChecked.GetValueOrDefault();
            bool flag = coloringSpots != Settings.Default.ColoringSpots;
            bool coloringFilters = Settings.Default.ColoringFilters;
            Settings.Default.ColoringFilters = ColoringFilters.IsChecked.GetValueOrDefault();
            bool num = coloringFilters != Settings.Default.ColoringFilters;
            bool num2 = !settingsToRefreshSpotsList.Equals(GetSettingsToRefreshSpotsList());
            Settings.Default.Save();
            if (num2)
            {
                Sys.MainWindow.RefreshSpotsList(force: true);
            }

            if (flag)
            {
                SettingsForSpotsList.ColoringForSpotsChanged?.Invoke();
            }

            if (num)
            {
                SettingsForSpotsList.ColoringForFiltersChanged?.Invoke();
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
        ShowErotica.IsChecked = Settings.Default.ShowEroticaInSearchResults;
        AutoShowNewSpotsInTheList.IsChecked = Settings.Default.AutoShowNewSpotsInTheList;
        HideBlacklistedSpots.IsChecked = Settings.Default.HideBlacklistedSpots;
        ShowTrustedOnly.IsChecked = Settings.Default.ShowTrustedOnlyEnabled;
        ColoringSpots.IsChecked = Settings.Default.ColoringSpots;
        ColoringFilters.IsChecked = Settings.Default.ColoringFilters;
        switch (Settings.Default.NumOfSpamReportsToSpotHide)
        {
            case 1:
                SpamReportsThresholdCombo.SelectedIndex = 0;
                break;
            case 2:
                SpamReportsThresholdCombo.SelectedIndex = 1;
                break;
            case 3:
                SpamReportsThresholdCombo.SelectedIndex = 2;
                break;
            case 5:
                SpamReportsThresholdCombo.SelectedIndex = 3;
                break;
            case 7:
                SpamReportsThresholdCombo.SelectedIndex = 4;
                break;
            case -1:
                SpamReportsThresholdCombo.SelectedIndex = 5;
                break;
            default:
                SpamReportsThresholdCombo.SelectedIndex = 3;
                break;
        }
    }

    public bool VerifyFields()
    {
        return true;
    }
}