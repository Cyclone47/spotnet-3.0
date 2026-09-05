using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Downloader.Controls.Player;
using Spotnet.Downloader.PostProcessing;
using Spotnet.Helpers;
using Spotnet.Model;

namespace Spotnet.Downloader.Controls;
public partial class DownloadsControl : UserControl, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public DownloadsControl()
    {
        if (!Sys.IsShutdownRequested)
        {
            InitializeComponent();
            base.DataContext = Sys.Downloader;
            PlayerControlItem.OnStartPlaying += ShowPlayerPanel;
            PlayerControlItem.OnStopPlaying += PlayerControlItemOnOnStopPlaying;
        }
    }

    private void PlayerControlItemOnOnStopPlaying()
    {
        Sys.Downloader.SetPlayInactiveToAllItems();
        HidePlayerPanel();
        PreUnpack.PauseAll();
    }

    public void ShowPlayerPanel()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            if (PlayerControlParent.Visibility != 0)
            {
                PlayerControlParent.Height = 0.0;
                PlayerControlParent.Visibility = Visibility.Visible;
                PlayerControlRowSplitter.Visibility = Visibility.Visible;
                DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = AppHelper.DoubleAnimation(ParentGrid.RowDefinitions[1].ActualHeight / 2.0 - 2.0, TimeSpan.FromSeconds(0.5));
                doubleAnimationUsingKeyFrames.Completed += delegate
                {
                    ParentGrid.RowDefinitions[0].Height = new GridLength(5.0, GridUnitType.Star);
                    ParentGrid.RowDefinitions[1].Height = new GridLength(5.0, GridUnitType.Star);
                    PlayerControlParent.BeginAnimation(FrameworkElement.HeightProperty, null);
                    PlayerControlParent.Height = double.NaN;
                };
                PlayerControlParent.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimationUsingKeyFrames);
            }
        });
    }

    public void HidePlayerPanel()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            if (PlayerControlParent.Visibility != Visibility.Collapsed)
            {
                PlayerControlParent.Height = PlayerControlParent.ActualHeight;
                ParentGrid.RowDefinitions[0].Height = GridLength.Auto;
                DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = AppHelper.DoubleAnimation(0.0, TimeSpan.FromSeconds(0.5));
                doubleAnimationUsingKeyFrames.Completed += delegate
                {
                    PlayerControlParent.Visibility = Visibility.Collapsed;
                    PlayerControlRowSplitter.Visibility = Visibility.Collapsed;
                };
                PlayerControlParent.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimationUsingKeyFrames);
            }
        });
    }

    public void Dispose()
    {
        PlayerControlItem.OnStartPlaying -= ShowPlayerPanel;
        PlayerControlItem.OnStopPlaying -= PlayerControlItemOnOnStopPlaying;
        DownloadsGrid.Dispose();
    }
}