using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using NLog;
using Spotnet.Browser;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;

namespace Spotnet.Controls;
public partial class SettingsForTabs : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private string _newAvatar;
    public SettingsForTabs()
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

            Settings.Default.ShowComments = ShowCom.IsChecked.GetValueOrDefault();
            bool saveTabs = Settings.Default.SaveTabs;
            Settings.Default.SaveTabs = SetTabs.IsChecked.GetValueOrDefault();
            bool num = Settings.Default.SaveTabs != saveTabs;
            Settings.Default.ExternalBrowser = UseExternalBrowser.IsChecked.GetValueOrDefault();
            Settings.Default.IsEnabledSmiles = EnableSmiles.IsChecked.GetValueOrDefault();
            Settings.Default.LoadImageOnSpotTab = !DoNotLoadImageOnSpotTab.IsChecked.GetValueOrDefault();
            Settings.Default.Avatar = _newAvatar;
            bool flag = false;
            string text = ((TextBlock)TabThemeCombo.SelectedItem).Text;
            if (!text.Equals(Settings.Default.ActiveTheme))
            {
                Settings.Default.ActiveTheme = text;
                SpotParser.ResetThemeFiles();
                flag = true;
                Log.Debug("Tab theme changed to " + Settings.Default.ActiveTheme);
            }

            if (Settings.Default.ShowTabToolbar != ShowTabToolbar.IsChecked.GetValueOrDefault())
            {
                Settings.Default.ShowTabToolbar = ShowTabToolbar.IsChecked.GetValueOrDefault();
                flag = true;
            }

            if (flag)
            {
                Sys.MainWindow.ReloadAllSpotPages();
            }

            Settings.Default.Save();
            if (num)
            {
                if (Settings.Default.SaveTabs)
                {
                    Sys.MainWindow.SaveTabs();
                }
                else
                {
                    Sys.MainWindow.ClearSavedTabs();
                }
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
        ShowCom.IsChecked = Settings.Default.ShowComments;
        SetTabs.IsChecked = Settings.Default.SaveTabs;
        UseExternalBrowser.IsChecked = Settings.Default.ExternalBrowser;
        EnableSmiles.IsChecked = Settings.Default.IsEnabledSmiles;
        DoNotLoadImageOnSpotTab.IsChecked = !Settings.Default.LoadImageOnSpotTab;
        IEnumerable<string> enumerable = (
            from d in Directory.GetDirectories($"{AppHelper.SettingsFolder}\\TabThemes\\")
            where File.Exists(Path.Combine(d, "spot.htm"))select d).Select(Path.GetFileName);
        TextBlock selectedItem = null;
        foreach (string item in enumerable)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = item
            };
            TabThemeCombo.Items.Add(textBlock);
            if (item.Equals(Settings.Default.ActiveTheme))
            {
                selectedItem = textBlock;
            }
        }

        TabThemeCombo.SelectedItem = selectedItem;
        ShowTabToolbar.IsChecked = Settings.Default.ShowTabToolbar;
        _newAvatar = Settings.Default.Avatar;
        UpdateAvatar();
    }

    private void UpdateAvatar()
    {
        BitmapImage bitmapImage = new BitmapImage();
        if (_newAvatar.IsNullOrEmpty())
        {
            string text = AppHelper.MakeMd5(UserKeyHelper.GetModulus()) ?? Words.Unknown;
            string uriString = "http://www.gravatar.com/avatar/" + text + "?s=32&d=identicon";
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(uriString, UriKind.Absolute);
            bitmapImage.EndInit();
        }
        else
        {
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = new MemoryStream(Convert.FromBase64String(_newAvatar));
            bitmapImage.EndInit();
        }

        AvatarImage.Source = bitmapImage;
    }

    public bool VerifyFields()
    {
        return true;
    }

    private void TabThemeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        switch (((TextBlock)TabThemeCombo.SelectedItem).Text.ToLower())
        {
            case "default":
                ShowTabToolbar.IsChecked = true;
                break;
            case "default spotnet 1.9":
            case "dropped":
            case "green theme":
            case "nostalgie":
            case "royaleblue":
            case "simple":
            case "straight":
            case "tabbed":
                ShowTabToolbar.IsChecked = false;
                break;
        }
    }

    private void ChangeAvatar_OnClick(object sender, RoutedEventArgs e)
    {
        if (ImageHelper.ChangeAvatar(out var newAvatar) && !newAvatar.IsNullOrEmpty())
        {
            _newAvatar = newAvatar;
            UpdateAvatar();
        }
    }

    private void ResetAvatar_OnClick(object sender, RoutedEventArgs e)
    {
        _newAvatar = "";
        UpdateAvatar();
    }
}