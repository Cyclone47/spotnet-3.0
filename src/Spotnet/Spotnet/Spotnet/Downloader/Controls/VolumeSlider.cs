using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Downloader.Controls;
public partial class VolumeSlider : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Track _track;
    public VolumeSlider()
    {
        InitializeComponent();
        VolumeWithMuteSlider.IsMoveToPointEnabled = true;
        VolumeWithMuteSlider.Loaded += delegate
        {
            _track = base.Template.FindName("PART_Track", this) as Track;
        };
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_track == null)
        {
            _track = VolumeWithMuteSlider.FindChildByType<Track>();
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _ = _track;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        ((UIElement)e.OriginalSource).CaptureMouse();
        Log.Debug("Mouse down");
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        ((UIElement)e.OriginalSource).ReleaseMouseCapture();
    }
}