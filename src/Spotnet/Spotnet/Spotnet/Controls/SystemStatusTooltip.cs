using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;

namespace Spotnet.Controls;
public partial class SystemStatusTooltip : UserControl, INotifyPropertyChanged
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public static readonly DependencyProperty TooltipTextProperty = DependencyProperty.Register("TooltipText", typeof(string), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty TooltipTitleProperty = DependencyProperty.Register("TooltipTitle", typeof(string), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty TooltipTypeProperty = DependencyProperty.Register("TooltipType", typeof(ModernTooltipType), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(ModernTooltipType.Info, OnTooltipTypeChanged));
    public static readonly DependencyProperty TooltipColorProperty = DependencyProperty.Register("TooltipColor", typeof(Color), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(Color.FromRgb(0, 0, 0)));
    public static readonly DependencyProperty TooltipCloseButtonShowProperty = DependencyProperty.Register("TooltipCloseButtonShow", typeof(bool), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty TooltipIsVisibleProperty = DependencyProperty.Register("TooltipIsVisible", typeof(bool), typeof(SystemStatusTooltip), new FrameworkPropertyMetadata(false));
    private readonly HashSet<SystemStateProblemEnum> _problemsWithPermanentStatusSet;
    private bool _hiddingScheduled;
    private bool _isPermanent;
    public string TooltipText
    {
        get
        {
            return (string)GetValue(TooltipTextProperty);
        }

        set
        {
            SetValue(TooltipTextProperty, value);
        }
    }

    public string TooltipTitle
    {
        get
        {
            return (string)GetValue(TooltipTitleProperty);
        }

        set
        {
            SetValue(TooltipTitleProperty, value);
        }
    }

    public ModernTooltipType TooltipType
    {
        get
        {
            return (ModernTooltipType)GetValue(TooltipTypeProperty);
        }

        set
        {
            SetValue(TooltipTypeProperty, value);
        }
    }

    public Color TooltipColor
    {
        get
        {
            return (Color)GetValue(TooltipColorProperty);
        }

        set
        {
            SetValue(TooltipColorProperty, value);
        }
    }

    public bool TooltipCloseButtonShow
    {
        get
        {
            return (bool)GetValue(TooltipCloseButtonShowProperty);
        }

        set
        {
            SetValue(TooltipCloseButtonShowProperty, value);
        }
    }

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
                TooltipCloseButtonShow = value;
                TooltipIsVisible = value;
            });
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private static void OnTooltipTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        SystemStatusTooltip systemStatusTooltip = (SystemStatusTooltip)d;
        systemStatusTooltip.UpdateColor();
        if (systemStatusTooltip.TooltipType == ModernTooltipType.Info)
        {
            systemStatusTooltip.IsPermanent = false;
        }
    }

    public void UpdateColor()
    {
        TooltipColor = ((TooltipType == ModernTooltipType.Info) ? ((Color)ColorConverter.ConvertFromString("#EE30CC50")) : ((Color)ColorConverter.ConvertFromString("#EEFFD283")));
    }

    public SystemStatusTooltip()
    {
        InitializeComponent();
        UpdateColor();
        _problemsWithPermanentStatusSet = new HashSet<SystemStateProblemEnum>();
        SystemStateChecker.StateChanged += delegate (SystemStateEventTypeEnum a, SystemStateProblemEnum e)
        {
            if (a == SystemStateEventTypeEnum.Add)
            {
                if (_problemsWithPermanentStatusSet.Add(e))
                {
                    IsPermanent = true;
                }
            }
            else
            {
                _problemsWithPermanentStatusSet.Remove(e);
            }
        };
    }

    private void ImgClose_MouseDown(object sender, MouseButtonEventArgs e)
    {
        IsPermanent = false;
        StartHidding();
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

    protected virtual void OnPropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ScheduleHide(TimeSpan timeout)
    {
        if (IsPermanent)
        {
            return;
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
            ScheduleHide(TimeSpan.FromSeconds(1.0));
        }
    }
}