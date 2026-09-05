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
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;

namespace Spotnet.Controls;
public partial class ChangeSpotThemeWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public ChangeSpotThemeWindow()
    {
        base.Closing += Window_Closing;
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    private void OnInitialized(object sender, EventArgs eventArgs)
    {
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
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (e.Cancel)
        {
            return;
        }

        try
        {
            base.Owner.Activate();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }

    private void SaveSettings()
    {
        bool flag = false;
        string text = ((TextBlock)TabThemeCombo.SelectedItem).Text;
        if (!text.Equals(Settings.Default.ActiveTheme))
        {
            Settings.Default.ActiveTheme = text;
            Settings.Default.Save();
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
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
}