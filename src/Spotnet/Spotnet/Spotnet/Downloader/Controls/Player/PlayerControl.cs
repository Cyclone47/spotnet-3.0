using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using Spotnet.Mvvm.Threading;
using LibVLCSharp.Shared;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Downloader.Controls.Player;
public partial class PlayerControl : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly object _lockFullScreenSwitch = new object ();
    private readonly object _lockPlayerStop = new object ();
    private readonly object _lockPositionChanging = new object ();
    private readonly object _timerToOverlayingTextDisappearingLock = new object ();
    private Window _fullScreenWindow;
    private bool _isPlaylistVisibleOnFullScreen;
    private bool _isPlaylistVisibleOnNormalScreen = true;
    private bool _positionChanging;
    private Point _previousPositionOfMouseOnFullScreen;
    private System.Timers.Timer _timerToHideControlPanel;
    private System.Timers.Timer _timerToOverlayingTextDisappearing;
    private System.Timers.Timer _timerToPauseVideoAfterMouseClick;
    private readonly PlayerViewModel _vm;
    private int _volumeBeforeTheMute;
    public bool IsInFullScreenMode { get; private set; }

    public int Volume
    {
        get
        {
            return _vm.Player?.Volume ?? 0;
        }

        set
        {
            if (_vm.Player != null && _vm.Player.Volume != value)
            {
                int num = value;
                if (num < 0)
                {
                    num = 0;
                }
                else if (num > 200)
                {
                    num = 200;
                }

                Settings.Default.PlayerVolume = num;
                Settings.Default.Save();
                _vm.Player.Volume = num;
            }
        }
    }

    public event Action OnStartPlaying;
    public event Action OnStopPlaying;
    public PlayerControl()
    {
        if (!Sys.IsShutdownRequested)
        {
            InitializeComponent();
            _vm = new PlayerViewModel();
            MainGrid.DataContext = _vm;
            PlaylistGrid.ItemsSource = _vm.PlaylistItems;
            _vm.Disposed += PlayerDispose;
            _vm.FullStop += FullStop;
            _vm.StartPlaying += Play;
            VideoOverlayingGrid.PreviewMouseLeftButtonDown += VideoControlDockOnMouseLeftButtonDown;
            VideoOverlayingGrid.PreviewMouseMove += VideoControlDockOnMouseMove;
            VideoOverlayingGrid.PreviewMouseWheel += VideoControlDockOnMouseWheel;
            ControlPanel.GotKeyboardFocus += delegate
            {
                RestoreFocus();
            };
            VideoOverlayingGrid.GotKeyboardFocus += delegate
            {
                RestoreFocus();
            };
            SliderVolume.VolumeWithMuteSlider.ValueChanged += SliderVolume_ValueChanged;
            SliderVolume.MuteGrid.PreviewMouseLeftButtonDown += VolumeMuteOnMouseDown;
            RefreshPlaylistVisibility();
        }
    }

    private void VolumeMuteOnMouseDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (_vm.Player != null)
        {
            bool isMute = _vm.Player.IsMute;
            if (!isMute)
            {
                _volumeBeforeTheMute = Volume;
                Volume = 0;
            }
            else if (_volumeBeforeTheMute > 0 && Volume == 0)
            {
                Volume = _volumeBeforeTheMute;
            }

            _vm.Player.IsMute = !isMute;
            _vm.RaiseVolumeChanged();
            mouseButtonEventArgs.Handled = true;
        }
    }

    private void RestoreFocus()
    {
        _vm.Player?.Focus();
    }

    private void VideoControlDockOnMouseMove(object s, MouseEventArgs a)
    {
        if (!_previousPositionOfMouseOnFullScreen.Equals(a.GetPosition(MainGrid)))
        {
            _previousPositionOfMouseOnFullScreen = a.GetPosition(MainGrid);
            ShowControlPanelForTheTimeInterval();
        }
    }

    private void EnableTimerToPauseVideoAfterMouseClick()
    {
        DisableTimerOnControlPanelHide();
        _timerToPauseVideoAfterMouseClick = new System.Timers.Timer
        {
            Interval = AppHelper.GetDoubleClickTime() + 10,
            AutoReset = false
        };
        _timerToPauseVideoAfterMouseClick.Elapsed += delegate
        {
            if (_vm.Player == null || _vm.Player.State == VLCState.Ended)
            {
                Play(null, TimeSpan.Zero, applyAnimation: false).Forget();
            }
            else
            {
                _vm.PauseOrResume();
            }
        };
        _timerToPauseVideoAfterMouseClick.Start();
    }

    private void DisableTimerToPauseVideoAfterMouseClick()
    {
        if (_timerToPauseVideoAfterMouseClick != null)
        {
            _timerToPauseVideoAfterMouseClick.Stop();
            _timerToPauseVideoAfterMouseClick.Dispose();
        }
    }

    private void VideoControlDockOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RestoreFocus();
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            DisableTimerToPauseVideoAfterMouseClick();
            FullScreenSwitch();
            e.Handled = true;
        }
        else
        {
            EnableTimerToPauseVideoAfterMouseClick();
        }

        ShowControlPanelForTheTimeInterval();
    }

    private void VideoControlDockOnMouseWheel(object sender, MouseWheelEventArgs mouseWheelEventArgs)
    {
        ShowControlPanelForTheTimeInterval();
        if (mouseWheelEventArgs.Delta > 0)
        {
            Volume++;
        }
        else
        {
            Volume--;
        }
    }

    private void VlcMediaPlayerOnEndReached(object sender, EventArgs eventArgs)
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            SkipOnStopLock(delegate
            {
                if (_vm.Player != null && (_vm.Player.Length - _vm.Player.Time).TotalSeconds < 1.0)
                {
                    ShowControlPanelForTheTimeInterval();
                    _vm.IsPlaying = false;
                    _vm.Player.VlcMediaPlayer.Pause();
                    _vm.TryToPlayNext();
                }
            });
        });
    }

    private void VideoControlDockOnKeyDown(object sender, KeyEventArgs keyArgs)
    {
        if (_vm.Player == null || keyArgs.KeyboardDevice.Modifiers != 0)
        {
            return;
        }

        ShowControlPanelForTheTimeInterval();
        bool handled = true;
        switch (keyArgs.Key)
        {
            case Key.Up:
                Volume += 5;
                break;
            case Key.Down:
                Volume -= 5;
                break;
            case Key.Right:
                ChangeTime(5);
                break;
            case Key.Left:
                ChangeTime(-5);
                break;
            case Key.Space:
                _vm.PauseOrResume();
                break;
            case Key.P:
                ShowHidePlaylist();
                break;
            case Key.Return:
            case Key.F:
                FullScreenSwitch();
                break;
            case Key.Escape:
                if (IsInFullScreenMode)
                {
                    GoToNormalScreen();
                }
                else
                {
                    handled = false;
                }

                break;
            case Key.S:
                FullStop();
                break;
            default:
                handled = false;
                break;
        }

        keyArgs.Handled = handled;
    }

    private void ChangeTime(int seconds)
    {
        Task.Run(delegate
        {
            SkipOnStopLock(delegate
            {
                if (_vm.Player != null)
                {
                    _vm.Player.Time += TimeSpan.FromSeconds(seconds);
                    DispatcherHelper.UIDispatcher.Invoke(delegate
                    {
                        SliderPosition.Value = _vm.Player.Position;
                    });
                }
            });
        });
    }

    private void ShowOverlayingVolumeLevel()
    {
        if (_vm.Player != null)
        {
            VideoOverlayingText.Text = $"Volume {Volume}%";
            RescheduleOverlayingText();
        }
    }

    private void RescheduleOverlayingText()
    {
        lock (_timerToOverlayingTextDisappearingLock)
        {
            if (_timerToOverlayingTextDisappearing != null)
            {
                _timerToOverlayingTextDisappearing.Dispose();
                _timerToOverlayingTextDisappearing = null;
            }

            _timerToOverlayingTextDisappearing = new System.Timers.Timer
            {
                Interval = 2000.0,
                AutoReset = false
            };
            _timerToOverlayingTextDisappearing.Elapsed += delegate
            {
                DispatcherHelper.CheckBeginInvokeOnUI(delegate
                {
                    VideoOverlayingText.Text = "";
                });
            };
            _timerToOverlayingTextDisappearing.Start();
        }
    }

    private void FullScreenSwitch()
    {
        if (IsInFullScreenMode)
        {
            GoToNormalScreen();
        }
        else
        {
            GoToFullScreen();
        }
    }

    private void FullScreenBtn_OnClick(object sender, RoutedEventArgs e)
    {
        FullScreenSwitch();
        e.Handled = true;
    }

    private void GoToFullScreen()
    {
        if (IsInFullScreenMode)
        {
            return;
        }

        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            lock (_lockFullScreenSwitch)
            {
                if (IsInFullScreenMode)
                {
                    return;
                }

                IsInFullScreenMode = true;
                RefreshPlaylistVisibility();
                ParentGrid.Children.Remove(MainGrid);
                _fullScreenWindow = new PlayerFullScreenWindow(MainGrid)
                {
                    Owner = Sys.MainWindow
                };
                _fullScreenWindow.Closed += delegate
                {
                    GoToNormalScreen();
                };
                _fullScreenWindow.Show();
                ShowControlPanelForTheTimeInterval();
            }

            RestoreFocus();
        });
    }

    private void HideControlPanel()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            ControlPanel.Visibility = Visibility.Collapsed;
            VideoControlDock.Cursor = Cursors.None;
        });
    }

    private void ShowControlPanel()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            ControlPanel.Visibility = Visibility.Visible;
            VideoControlDock.Cursor = null;
        });
    }

    private void ShowControlPanelForTheTimeInterval()
    {
        DisableTimerOnControlPanelHide();
        ShowControlPanel();
        _timerToHideControlPanel = new System.Timers.Timer
        {
            Interval = 3000.0,
            AutoReset = false
        };
        _timerToHideControlPanel.Elapsed += delegate
        {
            if (!_positionChanging)
            {
                HideControlPanel();
            }
        };
        _timerToHideControlPanel.Start();
    }

    private void DisableTimerOnControlPanelHide()
    {
        if (_timerToHideControlPanel != null)
        {
            _timerToHideControlPanel.Dispose();
            _timerToHideControlPanel = null;
        }
    }

    private void GoToNormalScreen()
    {
        if (!IsInFullScreenMode || _fullScreenWindow == null)
        {
            return;
        }

        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            lock (_lockFullScreenSwitch)
            {
                if (IsInFullScreenMode && _fullScreenWindow != null)
                {
                    IsInFullScreenMode = false;
                    _fullScreenWindow.Close();
                    ParentGrid.Children.Add(MainGrid);
                    RefreshPlaylistVisibility();
                    ShowControlPanelForTheTimeInterval();
                    RestoreFocus();
                }
            }
        });
    }

    public async Task Play(PlaylistItemViewModel itemToPlay, TimeSpan timeToStart, bool applyAnimation = true)
    {
        if (itemToPlay == null)
        {
            if (_vm.CurrentPlaylistItemPlayed == null)
            {
                return;
            }

            itemToPlay = _vm.CurrentPlaylistItemPlayed;
        }

        if (!itemToPlay.FileFullPath.IsNullOrWhiteSpace())
        {
            this.OnStartPlaying?.Invoke();
            RefreshPlaylistVisibility();
            ShowControlPanelForTheTimeInterval();
            await PlayerInitialize(applyAnimation);
            Log.Debug("Player initialized");
            try
            {
                _vm.Player.LoadMedia(itemToPlay.FileFullPath);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
                return;
            }

            _vm.Resume();
            _vm.Player.Time = timeToStart;
            UpdateVolumeSlider();
            DispatcherHelper.CheckBeginInvokeOnUI(RestoreFocus);
        }
    }

    private void UpdateVolumeSlider()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            SkipOnStopLock(delegate
            {
                SliderVolume.VolumeWithMuteSlider.Value = Volume;
            });
        });
    }

    private void ShowHidePlaylistBtn_OnClick(object sender, RoutedEventArgs e)
    {
        RestoreFocus();
        ShowHidePlaylist();
    }

    public void ShowHidePlaylist()
    {
        if (IsInFullScreenMode)
        {
            _isPlaylistVisibleOnFullScreen = !_isPlaylistVisibleOnFullScreen;
        }
        else
        {
            _isPlaylistVisibleOnNormalScreen = !_isPlaylistVisibleOnNormalScreen;
        }

        RefreshPlaylistVisibility();
    }

    private void RefreshPlaylistVisibility()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            Visibility visibility = (((IsInFullScreenMode || !_isPlaylistVisibleOnNormalScreen) && (!IsInFullScreenMode || !_isPlaylistVisibleOnFullScreen)) ? Visibility.Collapsed : Visibility.Visible);
            if (PlaylistGrid.Visibility != visibility)
            {
                PlaylistGrid.Visibility = visibility;
                PlaylistSplitter.Visibility = visibility;
                MainGrid.ColumnDefinitions[1].Width = GridLength.Auto;
            }
        });
    }

    private void PlayPauseBtn_OnClick(object sender, RoutedEventArgs e)
    {
        RestoreFocus();
        if (!_vm.IsStopDetected && (_vm.Player == null || _vm.Player.State == VLCState.Ended))
        {
            Play(null, TimeSpan.Zero, applyAnimation: false).Forget();
        }
        else
        {
            _vm.PauseOrResume();
        }
    }

    private void StopBtn_OnClick(object sender, RoutedEventArgs e)
    {
        FullStop();
    }

    public void FullStop()
    {
        GoToNormalScreen();
        _vm.Dispose();
        this.OnStopPlaying?.Invoke();
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SkipOnStopLock(delegate
        {
            RestoreFocus();
            int num = Convert.ToInt32(SliderVolume.VolumeWithMuteSlider.Value);
            _vm.Player.IsMute = num == 0;
            Volume = num;
        });
    }

    private async Task PlayerInitialize(bool applyAnimation)
    {
        ManualResetEventSlim waitForPlayerInitialized = new ManualResetEventSlim();
        if (_vm.Player?.VlcMediaPlayer != null && applyAnimation)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(delegate
            {
                _vm.Player.VlcMediaPlayer.Pause();
                DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = AppHelper.DoubleAnimation(0.0, TimeSpan.FromSeconds(0.3));
                doubleAnimationUsingKeyFrames2.Completed += delegate
                {
                    InitAction();
                };
                _vm.Player.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames2);
            });
        }
        else
        {
            InitAction();
        }

        await Task.Run(() => waitForPlayerInitialized.Wait(TimeSpan.FromSeconds(5.0)));
        void InitAction()
        {
            lock (_lockPlayerStop)
            {
                _vm.IsStopDetected = false;
                _vm.Dispose();
            }

            DispatcherHelper.CheckBeginInvokeOnUI(delegate
            {
                lock (_lockPlayerStop)
                {
                    _vm.Player = new VlcPlayer();
                    Volume = Settings.Default.PlayerVolume;
                    _vm.Player.PositionChanged += VlcControlOnPositionChanged;
                    _vm.Player.LengthChanged += PlayerOnLengthChanged;
                    _vm.Player.VolumeChanged += PlayerOnVolumeChanged;
                    _vm.Player.PreviewKeyDown += VideoControlDockOnKeyDown;
                    _vm.PlayerInitialize();
                    if (applyAnimation)
                    {
                        DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = AppHelper.DoubleAnimation(1.0, TimeSpan.FromSeconds(0.8));
                        doubleAnimationUsingKeyFrames.Completed += delegate
                        {
                            _vm.Player.BeginAnimation(UIElement.OpacityProperty, null);
                            AddPlayerToUi();
                            waitForPlayerInitialized.Set();
                            RestoreFocus();
                        };
                        _vm.Player.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
                    }
                    else
                    {
                        AddPlayerToUi();
                        waitForPlayerInitialized.Set();
                    }
                }
            });
        }
    }

    private bool AddPlayerToUi()
    {
        try
        {
            VideoControlDock.Children.Insert(0, _vm.Player);
            _vm.Player.VlcMediaPlayer.EndReached += VlcMediaPlayerOnEndReached;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to initialize vlc player. Please check vlc lib folder");
            Log.Exception(ex);
            return false;
        }
    }

    private void PlayerOnVolumeChanged(object sender, EventArgs eventArgs)
    {
        UpdateVolumeSlider();
        ShowOverlayingVolumeLevel();
    }

    private void PlayerOnLengthChanged(object sender, EventArgs eventArgs)
    {
        SkipOnStopLock(delegate
        {
            if (_vm.CurrentPlaylistItemPlayed != null && _vm.Player != null)
            {
                _vm.CurrentPlaylistItemPlayed.Length = _vm.Player.Length;
            }
        });
    }

    private void SkipOnStopLock(Action action)
    {
        if (!Monitor.TryEnter(_lockPlayerStop))
        {
            return;
        }

        try
        {
            action?.Invoke();
        }
        finally
        {
            Monitor.Exit(_lockPlayerStop);
        }
    }

    private void PlaylistGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.CurrentItem is PlaylistItemViewModel itemToPlay)
        {
            _vm.Play(itemToPlay, TimeSpan.Zero);
        }
    }

    public void PlayerDispose()
    {
        if (_vm.Player == null)
        {
            return;
        }

        lock (_lockPlayerStop)
        {
            if (_vm.Player == null)
            {
                return;
            }

            VlcPlayer player = _vm.Player;
            DispatcherHelper.UIDispatcher.Invoke(delegate
            {
                try
                {
                    VideoControlDock.Children.Remove(player);
                }
                catch (Exception)
                {
                }
            });
            _vm.Player.PositionChanged -= VlcControlOnPositionChanged;
            if (_vm.Player.VlcMediaPlayer != null)
            {
                _vm.Player.VlcMediaPlayer.EndReached -= VlcMediaPlayerOnEndReached;
            }

            _vm.Player.LengthChanged -= PlayerOnLengthChanged;
            _vm.Player.VolumeChanged -= PlayerOnVolumeChanged;
            _vm.Player.PreviewKeyDown -= VideoControlDockOnKeyDown;
            _vm.Player.Dispose();
            _vm.Player = null;
            Log.Debug("Player disposed");
        }
    }

    private void ControlPanel_OnMouseEnter(object sender, MouseEventArgs e)
    {
        DisableTimerOnControlPanelHide();
        ShowControlPanel();
    }

    private void ControlPanel_OnMouseLeave(object sender, MouseEventArgs e)
    {
        SliderPositionDragStop();
        ShowControlPanelForTheTimeInterval();
    }

    private void ControlPanel_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SliderPositionDragStop();
    }

    private void SliderPositionDragStop()
    {
        if (!_positionChanging)
        {
            return;
        }

        lock (_lockPositionChanging)
        {
            if (_positionChanging)
            {
                _vm.Player.PositionChanged += VlcControlOnPositionChanged;
                _positionChanging = false;
                _vm.RestartTimerToDetectTheStop();
            }
        }
    }

    private void SliderPosition_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RestoreFocus();
        lock (_lockPositionChanging)
        {
            _positionChanging = true;
            _vm.Player.PositionChanged -= VlcControlOnPositionChanged;
            UpdateSliderPosition(e);
        }
    }

    private void ControlPanel_OnMouseMove(object sender, MouseEventArgs e)
    {
        UpdateSliderPosition(e);
    }

    private void UpdateSliderPosition(MouseEventArgs e)
    {
        if (!_positionChanging)
        {
            return;
        }

        lock (_lockPositionChanging)
        {
            if (!_positionChanging)
            {
                return;
            }

            double mousePosX = e.GetPosition(SliderPosition).X;
            if (_vm.Player == null || _vm.Player.State == VLCState.Ended || _vm.IsStopDetected)
            {
                Play(null, TimeSpan.Zero, applyAnimation: false).ContinueWith(delegate
                {
                    SetVideoProgressToMousePoiner(mousePosX);
                });
            }
            else
            {
                SetVideoProgressToMousePoiner(mousePosX);
            }
        }
    }

    private void SetVideoProgressToMousePoiner(double mousePosX)
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            double actualWidth = SliderPosition.ActualWidth;
            if (mousePosX < 0.0)
            {
                mousePosX = 0.0;
            }
            else if (mousePosX > actualWidth)
            {
                mousePosX = actualWidth;
            }

            SliderPosition.Value = SliderPosition.Maximum * mousePosX / actualWidth;
        });
    }

    private void SliderPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SkipOnStopLock(delegate
        {
            lock (_lockPositionChanging)
            {
                if (_positionChanging)
                {
                    float num = (float)e.NewValue;
                    if ((double)Math.Abs(num - 1f) < 0.0001)
                    {
                        _vm.Player.Time = _vm.Player.Length - TimeSpan.FromMilliseconds(500.0);
                    }
                    else
                    {
                        _vm.Player.Position = num;
                    }
                }
            }
        });
        TimeBlockCurrent.Text = _vm.Player.Time.ToShortTimeString();
        TimeBlockTotal.Text = _vm.Player.Length.ToShortTimeString();
    }

    private void VlcControlOnPositionChanged(object sender, EventArgs eventArgs)
    {
        _vm.RestartTimerToDetectTheStop();
        if (_positionChanging)
        {
            return;
        }

        SkipOnStopLock(delegate
        {
            if (_vm.Player != null)
            {
                SliderPosition.Value = _vm.Player.Position;
            }
        });
    }

    private void VideoControlDock_OnMouseLeave(object sender, MouseEventArgs e)
    {
    }

    private void VideoControlDock_OnMouseEnter(object sender, MouseEventArgs e)
    {
    }
}
