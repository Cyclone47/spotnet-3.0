using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class SocksProxyTooltip : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public static readonly DependencyProperty TooltipIsVisibleProperty = DependencyProperty.Register("TooltipIsVisible", typeof(bool), typeof(SocksProxyTooltip), new FrameworkPropertyMetadata(false));
    private bool _hiddingScheduled;
    private bool _isPermanent;
    public bool TooltipIsVisible
    {
        get
        {
            return (bool)GetValue(TooltipIsVisibleProperty);
        }

        set
        {
            if (value)
            {
                _hiddingScheduled = false;
            }

            SetValue(TooltipIsVisibleProperty, value);
        }
    }

    public bool IsPermanent
    {
        get
        {
            return _isPermanent;
        }

        set
        {
            _isPermanent = value;
            DispatcherHelper.CheckBeginInvokeOnUI(delegate
            {
                TooltipIsVisible = value;
            });
        }
    }

    public SocksProxyTooltip()
    {
        InitializeComponent();
    }

    private void StartHidding()
    {
        _hiddingScheduled = false;
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            ((Storyboard)FindResource("FadeOut")).Begin();
        });
    }

    private void OnFadeOutCompleted(object sender, EventArgs e)
    {
        ((Popup)base.Parent).IsOpen = false;
    }

    public void ScheduleHide(TimeSpan timeout = default(TimeSpan))
    {
        if (IsPermanent)
        {
            return;
        }

        if (timeout == default(TimeSpan))
        {
            timeout = TimeSpan.FromSeconds(1.0);
        }

        _hiddingScheduled = true;
        EventExtension.RunAfter(delegate
        {
            if (!base.IsMouseOver && _hiddingScheduled)
            {
                StartHidding();
            }
        }, timeout);
    }

    private void LayoutRoot_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hiddingScheduled && !IsPermanent)
        {
            ScheduleHide();
        }
    }

    private void SettingsIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
    }

    private void HelpIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
    }

    private void ToggleSwitch_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        SocksProxy.ChangeState(!Settings.Default.UseSocksProxy);
    }
}