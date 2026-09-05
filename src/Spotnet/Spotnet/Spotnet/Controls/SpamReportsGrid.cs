using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SpamReportsGrid : UserControl
{
    private readonly SpamReportsViewModel _listBoxVm;
    public static readonly DependencyProperty MessageIdProperty = DependencyProperty.Register("MessageId", typeof(string), typeof(SpamReportsGrid), new FrameworkPropertyMetadata(OnMessageIdPropertyChanged));
    public string MessageId
    {
        get
        {
            return (string)GetValue(MessageIdProperty);
        }

        set
        {
            SetValue(MessageIdProperty, value);
        }
    }

    public SpamReportsGrid()
    {
        InitializeComponent();
        _listBoxVm = new SpamReportsViewModel();
        MainStackPanel.DataContext = _listBoxVm;
        base.IsVisibleChanged += delegate
        {
            if (base.IsVisible)
            {
                _listBoxVm.StartLoadSpamReports();
            }
            else
            {
                _listBoxVm.StopLoadSpamReports();
            }
        };
    }

    private static void OnMessageIdPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpamReportsGrid spamReportsGrid)
        {
            spamReportsGrid._listBoxVm.MessageId = e.NewValue as string;
        }
    }
}