using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NLog;
using Spotnet.Helpers;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class ElementsVisibilityMenu : MenuItem
{
    private static readonly Logger logger;
    private VisibilityViewModel VisibilityVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).Visibility;

    static ElementsVisibilityMenu()
    {
        logger = LogManager.GetCurrentClassLogger();
    }

    public ElementsVisibilityMenu()
    {
        InitializeComponent();
        base.Loaded += OnLoaded;
    }

    protected void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            if (base.Parent.GetType() != typeof(ContextMenu))
            {
                base.Style = ((FrameworkElement)base.Parent).Style;
            }
        }
        catch (Exception ex)
        {
            logger.Exception(ex);
        }
    }

    private void VisibilityStatusBar_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.StatusBar, VisibilityStatusBar.IsChecked);
    }

    private void VisibilitySearch_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.Search, VisibilitySearch.IsChecked);
    }

    private void VisibilityFilters_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.Filters, VisibilityFilters.IsChecked);
    }

    private void VisibilityAddFilter_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.AddFilter, VisibilityAddFilter.IsChecked);
    }

    private void VisibilityMainMenu_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.MainMenu, VisibilityMainMenu.IsChecked);
    }

    private void VisibilityLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.LeftPanel, VisibilityLeftPanel.IsChecked);
    }

    private void VisibilityMainToolbar_Click(object sender, RoutedEventArgs e)
    {
        VisibilityVm.UpdateVisibility(HideableElement.MainToolbar, VisibilityMainToolbar.IsChecked);
    }
}