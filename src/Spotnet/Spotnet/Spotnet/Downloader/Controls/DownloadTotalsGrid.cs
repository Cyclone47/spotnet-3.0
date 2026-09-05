using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using NLog;
using Spotnet.Model;

namespace Spotnet.Downloader.Controls;
public partial class DownloadTotalsGrid : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public ObservableCollection<DataGridColumn> Columns => DownloadsTotal.Columns;

    public DownloadTotalsGrid()
    {
        if (!Sys.IsShutdownRequested)
        {
            InitializeComponent();
            CollectionViewSource collectionViewSource = new CollectionViewSource
            {
                Source = Sys.Downloader.TotalItems
            };
            DownloadsTotal.ItemsSource = collectionViewSource.View;
        }
    }
}