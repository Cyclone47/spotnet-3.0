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
public partial class SpotsListTypeMenu : MenuItem
{
    private static readonly Logger Log;
    static SpotsListTypeMenu()
    {
        Log = LogManager.GetCurrentClassLogger();
    }

    public SpotsListTypeMenu()
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
            Log.Exception(ex);
        }
    }

    private void SpotsListNoDetails_Click(object sender, RoutedEventArgs e)
    {
        ((SpotsListViewModel)base.DataContext).UpdateSpotsListType(SpotsListTypeEnum.NoDetails);
    }

    private void SpotsListWithDetails_Click(object sender, RoutedEventArgs e)
    {
        ((SpotsListViewModel)base.DataContext).UpdateSpotsListType(SpotsListTypeEnum.WithDetails);
    }

    private void SpotsListPicsOnly_Click(object sender, RoutedEventArgs e)
    {
        ((SpotsListViewModel)base.DataContext).UpdateSpotsListType(SpotsListTypeEnum.Thumbs);
    }
}