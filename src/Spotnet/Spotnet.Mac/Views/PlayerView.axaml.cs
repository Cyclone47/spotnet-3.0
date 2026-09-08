using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Spotnet.Mac.Media;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

/// <summary>
/// Het mediavoorbeeld — de Avalonia-port van Windows' <c>PlayerControl</c>:
/// video-oppervlak, bedieningspaneel met positie- en volumeschuif, playlist en
/// sneltoetsen (spatie, pijlen, P, S, F/Enter voor fullscreen, Escape).
/// </summary>
public partial class PlayerView : UserControl
{
    private PlayerViewModel? Vm => DataContext as PlayerViewModel;

    /// <summary>De host-window vraagt full-screen aan/uit (F of Escape).</summary>
    public event Action? FullScreenToggled;

    private bool _wired;
    private bool _draggingPosition;

    public PlayerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
        AttachedToVisualTree += (_, _) => Wire();
        DetachedFromVisualTree += (_, _) => _wired = false;
    }

    private void Wire()
    {
        var vm = Vm;
        if (vm == null || _wired)
        {
            return;
        }

        _wired = true;
        vm.VideoHandleCreated += OnVideoHandleCreated;
        vm.PlaybackStopped += OnPlaybackStopped;
        vm.PropertyChanged += OnVmPropertyChanged;

        PositionSlider.PointerPressed += OnPositionPointerPressed;
        PositionSlider.PointerMoved += OnPositionPointerMoved;
        PositionSlider.PointerReleased += OnPositionPointerReleased;
        PositionSlider.PointerCaptureLost += OnPositionPointerReleased;
        PlaylistList.DoubleTapped += OnPlaylistDoubleTapped;
        SizeChanged += OnPlayerSizeChanged;
    }

    private void OnPlayerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Vm?.Resize(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.Position) && !_draggingPosition)
        {
            PositionSlider.Value = Vm!.Position;
        }
    }

    private void OnVideoHandleCreated(IntPtr viewHandle)
    {
        VideoPresenter.Content = new MacNativeVideoHost(viewHandle);
        Vm?.Resize(VideoPresenter.Bounds.Width, VideoPresenter.Bounds.Height);
    }

    private void OnPlaybackStopped()
    {
        VideoPresenter.Content = null;
    }

    // ── Positieschuif: zoeken tijdens het slepen, zoals Windows' SliderPosition ──

    private void OnPositionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Vm?.SeekStarted();
        _draggingPosition = true;
        SeekFromPointer(e);
        e.Pointer.Capture(PositionSlider);
    }

    private void OnPositionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingPosition && e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            SeekFromPointer(e);
        }
    }

    private void OnPositionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishSeek(e);
    }

    private void OnPositionPointerReleased(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishSeek(null);
    }

    private void SeekFromPointer(PointerEventArgs e)
    {
        var vm = Vm;
        if (vm == null)
        {
            return;
        }

        var point = e.GetPosition(PositionSlider);
        var fraction = PositionSlider.Bounds.Width <= 0
            ? 0
            : Math.Clamp(point.X / PositionSlider.Bounds.Width, 0, 1);
        vm?.SeekPreview(fraction);
        vm?.SeekTo(fraction);
    }
    private void FinishSeek(PointerReleasedEventArgs? e)
    {
        if (!_draggingPosition)
        {
            return;
        }

        _draggingPosition = false;
        Vm?.SeekCompleted();
    }

    // ── Playlist: dubbelklik speelt het nummer ──────────────────────────────

    private void OnPlaylistDoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = FindDataContext<MediaPlaylistItem>(e.Source as Visual);
        if (item != null && Vm != null)
        {
            Vm.PlayFromPlaylistCommand.Execute(item);
        }
    }

    private static T? FindDataContext<T>(Visual? visual) where T : class
    {
        while (visual != null)
        {
            if (visual.DataContext is T typed)
            {
                return typed;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void ToggleFullScreen()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = window.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }

        FullScreenToggled?.Invoke();
    }

    private void OnPlayerKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                vm.Volume += 5;
                break;
            case Key.Down:
                vm.Volume -= 5;
                break;
            case Key.Right:
                vm.ChangeTime(5);
                break;
            case Key.Left:
                vm.ChangeTime(-5);
                break;
            case Key.Space:
                vm.PlayPause();
                break;
            case Key.P:
                vm.TogglePlaylistCommand.Execute(null);
                break;
            case Key.F:
            case Key.Enter:
                ToggleFullScreen();
                break;
            case Key.Escape:
                if (TopLevel.GetTopLevel(this) is Window fullScreenWindow
                    && fullScreenWindow.WindowState == WindowState.FullScreen)
                {
                    fullScreenWindow.WindowState = WindowState.Normal;
                    FullScreenToggled?.Invoke();
                }
                break;
            case Key.S:
                vm.FullStop();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
