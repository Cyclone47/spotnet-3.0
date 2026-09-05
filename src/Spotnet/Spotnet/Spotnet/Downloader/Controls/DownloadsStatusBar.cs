using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Downloader.Controls;
public partial class DownloadsStatusBar : UserControl, INotifyPropertyChanged
{
    private Timer _timerToUpdateFreeSpace;
    public string SpeedLimitValue
    {
        get
        {
            if (Settings.Default.SpeedLimit <= 0)
            {
                return Words.NoneWord;
            }

            return Settings.Default.SpeedLimit + " KB/s";
        }
    }

    public string Space => AppHelper.FormatSizeMegaBytes((double)AppHelper.GetDiskSpace(DownloaderProps.MainDir) / 1024.0 / 1024.0);
    public string SpaceTooltip => DownloaderProps.MainDir;

    public event PropertyChangedEventHandler PropertyChanged;
    public DownloadsStatusBar()
    {
        InitializeComponent();
        _timerToUpdateFreeSpace = new Timer(30000.0)
        {
            AutoReset = true
        };
        _timerToUpdateFreeSpace.Elapsed += delegate
        {
            OnPropertyChanged("Space");
            OnPropertyChanged("SpaceTooltip");
        };
        _timerToUpdateFreeSpace.Start();
    }

    private void UIElement_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ChangeDownloadSpeedLimitWindow changeDownloadSpeedLimitWindow = new ChangeDownloadSpeedLimitWindow();
        changeDownloadSpeedLimitWindow.Owner = Sys.MainWindow;
        changeDownloadSpeedLimitWindow.ShowDialog();
        OnPropertyChanged("SpeedLimitValue");
    }

    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        if (this.PropertyChanged != null)
        {
            this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}