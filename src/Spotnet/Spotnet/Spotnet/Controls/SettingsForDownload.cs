using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Markup;
using System.Windows.Media;
using NLog;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class SettingsForDownload : System.Windows.Controls.UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    private readonly Action<string> _onDownloadFolderChanged;
    private DateTime DownloaderScheduleStartDateTime
    {
        get
        {
            try
            {
                return DateTime.ParseExact(DownloaderScheduleStartTime.Text, "HH:mm", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }

    private DateTime DownloaderScheduleEndDateTime
    {
        get
        {
            try
            {
                return DateTime.ParseExact(DownloaderScheduleEndTime.Text, "HH:mm", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }

    public SettingsForDownload(Action<string> onDownloadFolderChanged)
    {
        base.Initialized += SettingsForDownload_Initialized;
        InitializeComponent();
        _onDownloadFolderChanged = onDownloadFolderChanged;
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            int num = ((!SpeedLimitCheckBox.IsChecked.GetValueOrDefault()) ? (-1) : int.Parse(SpeedLimitTextBox.Text));
            if (!Sys.Downloader.UpdateDownloadSpeedLimit(num))
            {
                return false;
            }

            Settings.Default.SpeedLimit = num;
            Settings.Default.DownloaderSchedule = EnableScheduleCheckBox.IsChecked.GetValueOrDefault();
            if (Settings.Default.DownloaderSchedule)
            {
                Settings.Default.DownloaderStartTime = DownloaderScheduleStartDateTime;
                Settings.Default.DownloaderEndTime = DownloaderScheduleEndDateTime;
            }

            switch (RemoveFilesCombo.SelectedIndex)
            {
                case 0:
                    Settings.Default.RemoveFilesOnDownloadRemove = 1;
                    break;
                case 1:
                    Settings.Default.RemoveFilesOnDownloadRemove = 0;
                    break;
                case 2:
                    Settings.Default.RemoveFilesOnDownloadRemove = -1;
                    break;
            }

            string text = DownloadFolderTextBox.Text.Trim();
            if (!Settings.Default.DownloadFolder.Equals(text) && AppHelper.EnsureDirectoryExist(text))
            {
                Settings.Default.DownloadFolder = text;
            }

            Settings.Default.RemovePar2FilesAfterDownload = RemovePar2Files.IsChecked.GetValueOrDefault();
            Sys.ShutdownPCAfterDownloads = ShutdownPcAfterDownloads.IsChecked.GetValueOrDefault();
            Settings.Default.NotifyAboutDownloadComplete = NotifyAboutDownloadComplete.IsChecked.GetValueOrDefault();
            Settings.Default.Save();
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    private void SettingsForDownload_Initialized(object sender, EventArgs e)
    {
        SpeedLimitCheckBox.IsChecked = Settings.Default.SpeedLimit > 0;
        SpeedLimitTextBox.IsEnabled = SpeedLimitCheckBox.IsChecked.GetValueOrDefault();
        if (SpeedLimitTextBox.IsEnabled)
        {
            SpeedLimitTextBox.Text = Settings.Default.SpeedLimit.ToString();
        }

        ValidateSpeedLimit();
        ShutdownPcAfterDownloads.IsChecked = Sys.ShutdownPCAfterDownloads;
        RemovePar2Files.IsChecked = Settings.Default.RemovePar2FilesAfterDownload;
        NotifyAboutDownloadComplete.IsChecked = Settings.Default.NotifyAboutDownloadComplete;
        DownloadFolderTextBox.Text = DownloaderProps.MainDir;
        ValidateDownloadFolder();
        EnableScheduleCheckBox.IsChecked = Settings.Default.DownloaderSchedule;
        if (!Settings.Default.DownloaderSchedule)
        {
            DownloaderScheduleStartTime.Text = "00:00";
            DownloaderScheduleEndTime.Text = "00:00";
        }
        else
        {
            DownloaderScheduleStartTime.Text = Settings.Default.DownloaderStartTime.ToString("HH:mm");
            DownloaderScheduleEndTime.Text = Settings.Default.DownloaderEndTime.ToString("HH:mm");
        }

        DownloaderScheduleUpdateStates();
        if (Settings.Default.RemoveFilesOnDownloadRemove == 1)
        {
            RemoveFilesCombo.SelectedIndex = 0;
        }

        if (Settings.Default.RemoveFilesOnDownloadRemove == 0)
        {
            RemoveFilesCombo.SelectedIndex = 1;
        }

        if (Settings.Default.RemoveFilesOnDownloadRemove == -1)
        {
            RemoveFilesCombo.SelectedIndex = 2;
        }
    }

    public bool VerifyFields()
    {
        List<System.Windows.Controls.Control> source = new List<System.Windows.Controls.Control>
        {
            DownloaderScheduleStartTime,
            DownloaderScheduleEndTime,
            SpeedLimitTextBox,
            DownloadFolderTextBox
        };
        if (!DownloadFolderTextBox.Text.IsNullOrEmpty())
        {
            return !source.Any((System.Windows.Controls.Control f) => object.Equals(f.Background, _fieldInvalidBackground));
        }

        return false;
    }

    private void BrowseDownloaderPath_OnClick(object sender, RoutedEventArgs e)
    {
        string downloadFolder = Settings.Default.DownloadFolder;
        PreviewKeyDownEventArgs e2 = new PreviewKeyDownEventArgs(Keys.D | Keys.Control);
        Sys.MainWindow.OnKeyDown(e2, updateDownloadFolder: false);
        DownloadFolderTextBox.Text = Settings.Default.DownloadFolder;
        Settings.Default.DownloadFolder = downloadFolder;
        Settings.Default.Save();
    }

    private void DownloaderScheduleUpdateStates()
    {
        if (DownloaderScheduleStartTime != null && DownloaderScheduleEndTime != null)
        {
            new List<System.Windows.Controls.Control>
            {
                DownloaderScheduleStartTime,
                DownloaderScheduleEndTime
            };
            DownloaderScheduleValidateTime();
            DownloaderScheduleStartTime.IsEnabled = EnableScheduleCheckBox.IsChecked.GetValueOrDefault();
            DownloaderScheduleEndTime.IsEnabled = EnableScheduleCheckBox.IsChecked.GetValueOrDefault();
        }
    }

    private bool DownloaderScheduleValidateTime()
    {
        if (DownloaderScheduleStartTime == null || DownloaderScheduleEndTime == null)
        {
            return false;
        }

        bool flag;
        bool flag2;
        if (!EnableScheduleCheckBox.IsChecked.GetValueOrDefault())
        {
            flag = true;
            flag2 = true;
        }
        else
        {
            flag = DownloaderScheduleStartDateTime != DateTime.MinValue;
            flag2 = DownloaderScheduleEndDateTime != DateTime.MinValue;
        }

        DownloaderScheduleStartTime.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        DownloaderScheduleEndTime.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag2 ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag && flag2;
    }

    private void DownloaderScheduleTime_GotFocus(object sender, RoutedEventArgs e)
    {
        DownloaderScheduleUpdateStates();
    }

    private void DownloaderScheduleOnTimeChanged(object sender, TextChangedEventArgs args)
    {
        DownloaderScheduleUpdateStates();
    }

    private void EnableScheduleCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        DownloaderScheduleUpdateStates();
    }

    private bool ValidateSpeedLimit()
    {
        bool flag = false;
        int result;
        if (!SpeedLimitTextBox.IsEnabled)
        {
            flag = true;
        }
        else if (int.TryParse(SpeedLimitTextBox.Text, out result))
        {
            flag = result >= 50 && result < 200000;
        }

        SpeedLimitTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private void SpeedLimit_GotFocus(object sender, RoutedEventArgs e)
    {
        ValidateSpeedLimit();
    }

    private void SpeedLimitTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateSpeedLimit();
    }

    private void SpeedLimitCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        SpeedLimitTextBox.IsEnabled = SpeedLimitCheckBox.IsChecked.GetValueOrDefault();
        SpeedLimitTextBox.Text = ((!SpeedLimitTextBox.IsEnabled) ? "" : ((Settings.Default.SpeedLimit > 0) ? Settings.Default.SpeedLimit.ToString() : ""));
        ValidateSpeedLimit();
    }

    private void DownloadFolderTextBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        ValidateDownloadFolder();
    }

    private void ValidateDownloadFolder()
    {
        if (DownloadFolderTextBox.Text.IsNullOrEmpty())
        {
            DownloadFolderTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "NoticeBackgroundBrush");
            return;
        }

        DownloadFolderTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WhiteColorBrush");
        _onDownloadFolderChanged?.Invoke(DownloadFolderTextBox.Text);
    }

    private void DownloadFolderTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateDownloadFolder();
    }
}
