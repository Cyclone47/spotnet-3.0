using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using NLog;
using System.IO;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class MainToolBarControl : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly double _defaultMenuHeight;
    private static VisibilityViewModel VisibilityVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).Visibility;

    public MainToolBarControl()
    {
        InitializeComponent();
        _defaultMenuHeight = base.Height;
        InputManager.Current.EnterMenuMode += OnEnterMenuMode;
        InputManager.Current.LeaveMenuMode += OnLeaveMenuMode;
        UpdateDownloaderMenuItemsState();
        RefreshLanguageLabel();
    }

    private void OnLeaveMenuMode(object sender, EventArgs e)
    {
        UpdateVisibility();
    }

    private void OnEnterMenuMode(object sender, EventArgs e)
    {
        base.Height = _defaultMenuHeight;
    }

    internal void UpdateVisibility()
    {
        base.Height = (VisibilityVm.IsVisibleMainMenu ? _defaultMenuHeight : 0.0);
    }

    internal void DisableUpdate()
    {
        UpdateMenuItem.IsEnabled = false;
        UpdateMenuItem.ToolTip = Words.DbUpdateInProgress;
        ToolTipService.SetShowOnDisabled(UpdateMenuItem, value: true);
    }

    internal void EnableUpdate()
    {
        UpdateMenuItem.IsEnabled = true;
        UpdateMenuItem.Opacity = 1.0;
        UpdateMenuItem.ToolTip = null;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.Close();
    }

    private void UpdateDownloaderMenuItemsState()
    {
        List<MenuItem> list = new List<MenuItem>
        {
            Map
        };
        List<MenuItem> list2 = new List<MenuItem>();
        if (Settings.Default.DownloadAction > 1)
        {
            list2.Add(Map);
            foreach (MenuItem item in list2)
            {
                item.ToolTip = Words.OptionDisabledBecauseOfDownloadAction;
            }
        }

        foreach (MenuItem item2 in list)
        {
            if (!list2.Contains(item2))
            {
                item2.IsEnabled = true;
                item2.Opacity = 1.0;
                item2.ToolTip = null;
            }
        }

        foreach (MenuItem item3 in list2)
        {
            item3.IsEnabled = false;
            item3.Opacity = 0.5;
            ToolTipService.SetShowOnDisabled(item3, value: true);
        }
    }

    private void LangDutch_Click(object sender, RoutedEventArgs e)
    {
        if (MenuLangEnglish.IsChecked)
        {
            UserLanguageHelper.Initialize("nl", updateCulture: false);
            RefreshLanguageLabel();
        }
    }

    private void LangEnglish_Click(object sender, RoutedEventArgs e)
    {
        if (MenuLangEnglish.IsChecked)
        {
            UserLanguageHelper.Initialize("en", updateCulture: false);
            RefreshLanguageLabel();
        }
    }

    private void Language_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        MenuLangEnglish.IsChecked = false;
        MenuLangDutch.IsChecked = false;
        string language = UserLanguageHelper.Language;
        if (language == "nl")
        {
            MenuLangDutch.IsChecked = true;
        }
        else
        {
            MenuLangEnglish.IsChecked = true;
        }
    }

    private void Let10_Click(object sender, RoutedEventArgs e)
    {
        if (Let10.IsChecked)
        {
            VisibilityVm.FontSize = 10;
        }
    }

    private void Let12_Click(object sender, RoutedEventArgs e)
    {
        if (Let12.IsChecked)
        {
            VisibilityVm.FontSize = 12;
        }
    }

    private void Let14_Click(object sender, RoutedEventArgs e)
    {
        if (Let14.IsChecked)
        {
            VisibilityVm.FontSize = 14;
        }
    }

    private void Let16_Click(object sender, RoutedEventArgs e)
    {
        if (Let16.IsChecked)
        {
            VisibilityVm.FontSize = 16;
        }
    }

    private void Let18_Click(object sender, RoutedEventArgs e)
    {
        if (Let18.IsChecked)
        {
            VisibilityVm.FontSize = 18;
        }
    }

    private void Let20_Click(object sender, RoutedEventArgs e)
    {
        if (Let20.IsChecked)
        {
            VisibilityVm.FontSize = 20;
        }
    }

    private void Let24_Click(object sender, RoutedEventArgs e)
    {
        if (Let24.IsChecked)
        {
            VisibilityVm.FontSize = 24;
        }
    }

    private void Lettergrootte_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        Let10.IsChecked = false;
        Let12.IsChecked = false;
        Let14.IsChecked = false;
        Let16.IsChecked = false;
        Let18.IsChecked = false;
        Let20.IsChecked = false;
        Let24.IsChecked = false;
        switch (Settings.Default.FontSize)
        {
            case 10:
                Let10.IsChecked = true;
                break;
            case 12:
                Let12.IsChecked = true;
                break;
            case 16:
                Let16.IsChecked = true;
                break;
            case 18:
                Let18.IsChecked = true;
                break;
            case 20:
                Let20.IsChecked = true;
                break;
            case 24:
                Let24.IsChecked = true;
                break;
            default:
                Let14.IsChecked = true;
                break;
        }
    }

    private void SpotLet10_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet10.IsChecked)
        {
            VisibilityVm.SpotFontSize = 10;
        }
    }

    private void SpotLet12_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet12.IsChecked)
        {
            VisibilityVm.SpotFontSize = 12;
        }
    }

    private void SpotLet14_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet14.IsChecked)
        {
            VisibilityVm.SpotFontSize = 14;
        }
    }

    private void SpotLet16_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet16.IsChecked)
        {
            VisibilityVm.SpotFontSize = 16;
        }
    }

    private void SpotLet18_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet18.IsChecked)
        {
            VisibilityVm.SpotFontSize = 18;
        }
    }

    private void SpotLet20_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet20.IsChecked)
        {
            VisibilityVm.SpotFontSize = 20;
        }
    }

    private void SpotLet24_Click(object sender, RoutedEventArgs e)
    {
        if (SpotLet24.IsChecked)
        {
            VisibilityVm.SpotFontSize = 24;
        }
    }

    private void SpotFontSize_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        SpotLet10.IsChecked = false;
        SpotLet12.IsChecked = false;
        SpotLet14.IsChecked = false;
        SpotLet16.IsChecked = false;
        SpotLet18.IsChecked = false;
        SpotLet20.IsChecked = false;
        SpotLet24.IsChecked = false;
        switch (Settings.Default.SpotFontSize)
        {
            case 10:
                SpotLet10.IsChecked = true;
                break;
            case 12:
                SpotLet12.IsChecked = true;
                break;
            case 16:
                SpotLet16.IsChecked = true;
                break;
            case 18:
                SpotLet18.IsChecked = true;
                break;
            case 20:
                SpotLet20.IsChecked = true;
                break;
            case 24:
                SpotLet24.IsChecked = true;
                break;
            default:
                SpotLet14.IsChecked = true;
                break;
        }
    }

    private void MainToolBar_Initialized(object sender, EventArgs e)
    {
        ToolBarTray.SetIsLocked(MainToolBar, value: true);
        foreach (DependencyObject item in (IEnumerable)MainToolBar.Items)
        {
            ToolBar.SetOverflowMode(item, OverflowMode.Never);
        }
    }

    private void RefreshLanguageLabel()
    {
        string language = UserLanguageHelper.Language;
        string text = ((!(language == "nl")) ? "en".ToUpper() : "nl".ToUpper());
        LanguageLabel.Text = text;
    }

    private void NZBOpslaan_Click(object sender, RoutedEventArgs e)
    {
        if (NZBOpslaan.IsChecked)
        {
            Settings.Default.DownloadAction = 3;
            Settings.Default.Save();
            Sys.MainWindow.ShowDownloads(bVisible: false);
            UpdateDownloaderMenuItemsState();
            Sys.MainWindow.RefreshSpotsList(force: true);
        }
    }

    private void ViaSpotnet_Click(object sender, RoutedEventArgs e)
    {
        if (ViaSpotnet.IsChecked)
        {
            Settings.Default.DownloadAction = 1;
            Settings.Default.Save();
            Sys.MainWindow.ShowDownloads(bVisible: true);
            UpdateDownloaderMenuItemsState();
            Sys.MainWindow.RefreshSpotsList(force: true);
        }
    }

    private void ViaStandaard_Click(object sender, RoutedEventArgs e)
    {
        if (ViaStandaard.IsChecked)
        {
            Settings.Default.DownloadAction = 2;
            Settings.Default.Save();
            Sys.MainWindow.ShowDownloads(bVisible: false);
            UpdateDownloaderMenuItemsState();
            Sys.MainWindow.RefreshSpotsList(force: true);
        }
    }

    private void sCol_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        sCol.Items.Clear();
        Sys.MainWindow.LoadHeaderMenu();
        foreach (MenuItem item in (IEnumerable)Sys.MainWindow.HeaderMenu.Items)
        {
            MenuItem menuItem2 = new MenuItem
            {
                Header = RuntimeHelpers.GetObjectValue(item.Header),
                IsChecked = item.IsChecked
            };
            menuItem2.AddHandler(UIElement.PreviewMouseDownEvent, new RoutedEventHandler(Sys.MainWindow.HeaderMenu_PreviewMouseDown), handledEventsToo: true);
            sCol.Items.Add(menuItem2);
        }
    }

    private void DownloadKnop_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        ViaSpotnet.IsChecked = false;
        ViaStandaard.IsChecked = false;
        NZBOpslaan.IsChecked = false;
        switch (Settings.Default.DownloadAction)
        {
            case 0:
            case 1:
                ViaSpotnet.IsChecked = true;
                break;
            case 2:
                ViaStandaard.IsChecked = true;
                break;
            case 3:
                NZBOpslaan.IsChecked = true;
                break;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.OpenAbout();
    }

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.OpenPage(PageTypeEnum.ReleaseNotes);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Sys.MainWindow.CheckForUpdatesInteractivelyAsync();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void AssociateNzbFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet\\");
            string iconPath = Path.Combine(path, "app.ico");
            string openWith = string.Format("\"{0}\" --processStart Spotnet.exe --process-start-args", Path.Combine(path, "Update.exe"));
            Version appVersion = AppHelper.AppVersion;
            string appFriendlyName = $"Spotnet {appVersion.Major}.{appVersion.Minor}";
            FileAssociator.SetAssociation(".nzb", "Spotnet.nzb", openWith, "NZB File", iconPath, appFriendlyName);
            AppHelper.ShowPopupMessage(Words.NzbFilesAssociated);
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void RemoteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AdvancedSettings advancedSettings = new AdvancedSettings();
        advancedSettings.Owner = Sys.MainWindow;
        advancedSettings.HeaderItemIndex = 6;
        advancedSettings.ShowDialog();
    }

    private void AdvancedSettings_OnClick(object sender, RoutedEventArgs e)
    {
        AdvancedSettings advancedSettings = new AdvancedSettings();
        advancedSettings.Owner = Sys.MainWindow;
        advancedSettings.ShowDialog();
    }

    private void SpotsListNoDetails_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.ShowSpotsListAs(SpotsListTypeEnum.NoDetails);
    }

    private void SpotsListWithDetails_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.ShowSpotsListAs(SpotsListTypeEnum.WithDetails);
    }

    private void SpotsListThumbs_Click(object sender, RoutedEventArgs e)
    {
        Sys.MainWindow.ShowSpotsListAs(SpotsListTypeEnum.Thumbs);
    }

    private void SpotsThemeChange_Click(object sender, RoutedEventArgs e)
    {
        ChangeSpotThemeWindow changeSpotThemeWindow = new ChangeSpotThemeWindow();
        changeSpotThemeWindow.Owner = Sys.MainWindow;
        changeSpotThemeWindow.ShowDialog();
    }

    private void FiltersListChange_Click(object sender, RoutedEventArgs e)
    {
        ChangeFiltersListWindow changeFiltersListWindow = new ChangeFiltersListWindow();
        changeFiltersListWindow.Owner = Sys.MainWindow;
        changeFiltersListWindow.ShowDialog();
    }

    private void Style_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        ShowCheckedStyle(ThemeHelper.CurrentTheme);
    }

    private void StyleClassic_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyle(ThemeHelper.Classic);
    }

    private void StyleModernLight_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyle(ThemeHelper.ModernLight);
    }

    private void StyleModernDark_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyle(ThemeHelper.ModernDark);
    }

    private void ApplyStyle(string theme)
    {
        ThemeHelper.ApplyTheme(theme);
        ShowCheckedStyle(theme);
    }

    /// <summary>
    /// Exactly one style is ticked. The items are checkable, so clicking one would
    /// otherwise leave two ticks behind.
    /// </summary>
    private void ShowCheckedStyle(string theme)
    {
        StyleClassic.IsChecked = theme == ThemeHelper.Classic;
        StyleModernLight.IsChecked = theme == ThemeHelper.ModernLight;
        StyleModernDark.IsChecked = theme == ThemeHelper.ModernDark;
    }
}
