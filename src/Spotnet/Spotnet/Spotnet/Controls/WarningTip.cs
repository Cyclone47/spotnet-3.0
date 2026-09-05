using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Controls;
public partial class WarningTip : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(WarningTip), new PropertyMetadata(string.Empty));
    public string Text
    {
        get
        {
            return (string)GetValue(TextProperty);
        }

        set
        {
            SetValue(TextProperty, value);
        }
    }

    public WarningTip()
    {
        InitializeComponent();
    }

    private void CloseMe(object sender, RoutedEventArgs e)
    {
        CloseMe();
    }

    public void CloseMe()
    {
        try
        {
            base.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }
}