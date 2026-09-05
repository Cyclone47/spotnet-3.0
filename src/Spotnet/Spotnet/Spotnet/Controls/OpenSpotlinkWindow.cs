using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Controls;
public partial class OpenSpotlinkWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    public string Link = "";
    public OpenSpotlinkWindow()
    {
        base.Closing += OpenSpotlink_Closing;
        InitializeComponent();
    }

    private void OpenSpotlink_Closing(object sender, CancelEventArgs e)
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

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        Link = LinkTextBox.Text.Trim();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LinkTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
    }

    private void LinkTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
    }
}
