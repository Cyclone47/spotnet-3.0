using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class ChangeFiltersListWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

    public ChangeFiltersListWindow()
    {
        base.Closing += Window_Closing;
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    private void OnInitialized(object sender, EventArgs eventArgs)
    {
        string selectedItem = "";
        foreach (string unchangableFilterNames in Filters.GetUnchangableFilterNamesList())
        {
            FiltersListCombo.Items.Add(unchangableFilterNames);
            if (unchangableFilterNames.Equals(Settings.Default.Filter))
            {
                selectedItem = unchangableFilterNames;
            }
        }

        foreach (string changableFilterNames in Filters.GetChangableFilterNamesList())
        {
            FiltersListCombo.Items.Add(changableFilterNames);
            if (changableFilterNames.Equals(Settings.Default.Filter))
            {
                selectedItem = changableFilterNames;
            }
        }

        FiltersListCombo.SelectedItem = selectedItem;
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
        string text = (string)FiltersListCombo.SelectedItem;
        if (!text.Equals(Settings.Default.Filter))
        {
            MainWindowVm.FilterSelectedName = text;
            Sys.LeftPanel.ReloadFilters();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}