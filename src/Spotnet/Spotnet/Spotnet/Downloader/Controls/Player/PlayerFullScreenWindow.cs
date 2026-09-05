using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NLog;
using Spotnet.Model;

namespace Spotnet.Downloader.Controls.Player;
public partial class PlayerFullScreenWindow : Window
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly Grid _mainGrid;
    public PlayerFullScreenWindow(Grid mainGrid)
    {
        if (!Sys.IsShutdownRequested)
        {
            _mainGrid = mainGrid;
            InitializeComponent();
            ParentGrid.Children.Add(_mainGrid);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ParentGrid.Children.Remove(_mainGrid);
    }
}