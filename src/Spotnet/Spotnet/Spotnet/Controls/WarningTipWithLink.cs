using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Navigation;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Deployment;
using Spotnet.Helpers;

namespace Spotnet.Controls;
public partial class WarningTipWithLink : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public static readonly DependencyProperty TextProperty1 = DependencyProperty.Register("Text1", typeof(string), typeof(WarningTip), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TextProperty2 = DependencyProperty.Register("Text2", typeof(string), typeof(WarningTip), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TextProperty3 = DependencyProperty.Register("Text3", typeof(string), typeof(WarningTip), new PropertyMetadata(string.Empty));
    public string Text1
    {
        get
        {
            return (string)GetValue(TextProperty1);
        }

        set
        {
            SetValue(TextProperty1, value);
        }
    }

    public string Text2
    {
        get
        {
            return (string)GetValue(TextProperty2);
        }

        set
        {
            SetValue(TextProperty2, value);
        }
    }

    public string Text3
    {
        get
        {
            return (string)GetValue(TextProperty3);
        }

        set
        {
            SetValue(TextProperty3, value);
        }
    }

    public string Text
    {
        set
        {
            if (value.Contains("<link>") && value.Contains("</link>"))
            {
                DispatcherHelper.CheckBeginInvokeOnUI(delegate
                {
                    int num = value.IndexOf("<link>", StringComparison.Ordinal);
                    int num2 = value.IndexOf("</link>", StringComparison.Ordinal);
                    Text1 = value.Substring(0, num);
                    Text2 = value.Substring(num + 6, num2 - num - 6);
                    Text3 = value.Substring(num2 + 7);
                });
            }
            else
            {
                SetValue(TextProperty1, value);
            }
        }
    }

    public WarningTipWithLink()
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

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        SquirrelStuff.RestartApplication();
    }
}