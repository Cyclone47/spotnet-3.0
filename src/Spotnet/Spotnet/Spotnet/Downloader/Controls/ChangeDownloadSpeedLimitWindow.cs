using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Views;

namespace Spotnet.Downloader.Controls;
public partial class ChangeDownloadSpeedLimitWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private bool _initializationFinished;
    private string _lastSettingsString;
    public bool BSuc;
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    public static bool IsRunning => DispatcherHelper.UIDispatcher.Invoke(() => Application.Current.Windows.OfType<SelectProviderWindow>().Any());
    private string CurrentSettingsString => SpeedLimitTextBox.Text;
    private bool AreSettingsChanged => _lastSettingsString != CurrentSettingsString;

    public ChangeDownloadSpeedLimitWindow()
    {
        base.Closing += ProviderSelectie_Closing;
        base.Initialized += ProviderSelectie_Initialized;
        InitializeComponent();
    }

    private void ProviderSelectie_Closing(object sender, CancelEventArgs e)
    {
        if (e.Cancel)
        {
            return;
        }

        try
        {
            base.Owner.Activate();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    private void ProviderSelectie_Initialized(object sender, EventArgs e)
    {
        try
        {
            SpeedLimitCheckBox.IsChecked = Settings.Default.SpeedLimit > 0;
            SpeedLimitTextBox.IsEnabled = SpeedLimitCheckBox.IsChecked.GetValueOrDefault();
            if (SpeedLimitTextBox.IsEnabled)
            {
                SpeedLimitTextBox.Text = Settings.Default.SpeedLimit.ToString();
            }

            _lastSettingsString = CurrentSettingsString;
            _initializationFinished = true;
            Activate();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            Close();
        }
    }

    private void UpdateOkButtonState()
    {
        if (_initializationFinished)
        {
            List<Control> source = new List<Control>
            {
                SpeedLimitTextBox
            };
            OkButton.IsEnabled = AreSettingsChanged && !source.Any((Control f) => object.Equals(f.Background, _fieldInvalidBackground));
        }
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

        SpeedLimitTextBox.SetResourceReference(Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        OkButton.Focus();
        UpdateLayout();
        Settings.Default.SpeedLimit = ((!SpeedLimitCheckBox.IsChecked.GetValueOrDefault()) ? (-1) : int.Parse(SpeedLimitTextBox.Text));
        Settings.Default.Save();
        BSuc = Sys.Downloader.UpdateDownloadSpeedLimit(Settings.Default.SpeedLimit);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        BSuc = true;
        Close();
    }

    private void SpeedLimit_GotFocus(object sender, RoutedEventArgs e)
    {
        ValidateSpeedLimit();
        UpdateOkButtonState();
    }

    private void SpeedLimitTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateSpeedLimit();
        UpdateOkButtonState();
    }

    private void SpeedLimitCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        SpeedLimitTextBox.IsEnabled = SpeedLimitCheckBox.IsChecked.GetValueOrDefault();
        SpeedLimitTextBox.Text = ((!SpeedLimitTextBox.IsEnabled) ? "" : ((Settings.Default.SpeedLimit > 0) ? Settings.Default.SpeedLimit.ToString() : ""));
        ValidateSpeedLimit();
        UpdateOkButtonState();
    }
}
