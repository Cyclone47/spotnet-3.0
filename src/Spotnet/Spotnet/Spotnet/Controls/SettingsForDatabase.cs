using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Squirrel;

namespace Spotnet.Controls;
public partial class SettingsForDatabase : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    public SettingsForDatabase()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            Settings.Default.DbAutoUpdateEnabled = DbAutoUpdates.IsChecked.GetValueOrDefault();
            Settings.Default.DbUpdateCompressionEnabled = DbUpdateCompression.IsChecked.GetValueOrDefault();
            AppHelper.ClearHeaderPhuse();
            Settings.Default.LoadComments = LoadComments.IsChecked.GetValueOrDefault();
            Settings.Default.InitialFetchDays = SelectedInitialFetchDays();
            int retention = Settings.Default.Retention;
            Settings.Default.Retention = ((!RetentionCheckBox.IsChecked.GetValueOrDefault()) ? (-1) : int.Parse(RetentionTextBox.Text));
            bool num = retention != Settings.Default.Retention;
            Settings.Default.Save();
            if (num)
            {
                Sys.MainWindow.ActionsAfterChangeRetention();
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        DbAutoUpdates.IsChecked = Settings.Default.DbAutoUpdateEnabled;
        DbUpdateCompression.IsChecked = Settings.Default.DbUpdateCompressionEnabled;
        LoadComments.IsChecked = Settings.Default.LoadComments;
        SelectInitialFetchDays(Settings.Default.InitialFetchDays);
        RetentionCheckBox.IsChecked = Settings.Default.Retention > 0;
        RetentionTextBox.IsEnabled = RetentionCheckBox.IsChecked.GetValueOrDefault();
        if (RetentionTextBox.IsEnabled)
        {
            RetentionTextBox.Text = Settings.Default.Retention.ToString();
        }
    }

    /// <summary>
    /// How far back the first synchronisation reaches, in days, with 0 meaning the whole
    /// group. Only applies while the spots database is still empty.
    /// </summary>
    private int SelectedInitialFetchDays()
    {
        if (InitialFetchRangeCombo.SelectedItem is ComboBoxItem selected
            && int.TryParse(Convert.ToString(selected.Tag), out int days)
            && days >= 0)
        {
            return days;
        }
        return Settings.Default.InitialFetchDays;
    }

    /// <remarks>
    /// A value saved by an older build, or one edited by hand, need not be one of the
    /// offered ranges; the nearest offered range is selected rather than leaving the box
    /// blank.
    /// </remarks>
    private void SelectInitialFetchDays(int days)
    {
        ComboBoxItem nearest = null;
        int smallestDistance = int.MaxValue;
        foreach (object entry in InitialFetchRangeCombo.Items)
        {
            if (!(entry is ComboBoxItem item) || !int.TryParse(Convert.ToString(item.Tag), out int value))
            {
                continue;
            }
            if (value == days)
            {
                InitialFetchRangeCombo.SelectedItem = item;
                return;
            }
            // "Everything" is not near anything; it is only chosen by an exact match.
            int distance = (value == 0) ? int.MaxValue : Math.Abs(value - days);
            if (distance < smallestDistance)
            {
                smallestDistance = distance;
                nearest = item;
            }
        }
        InitialFetchRangeCombo.SelectedItem = nearest;
    }

    private void RetentionCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        RetentionTextBox.IsEnabled = RetentionCheckBox.IsChecked.GetValueOrDefault();
        RetentionTextBox.Text = ((!RetentionTextBox.IsEnabled) ? "" : ((Settings.Default.Retention > 0) ? Settings.Default.Retention.ToString() : ""));
        ValidateRetention();
    }

    private void RetentionTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateRetention();
    }

    private void Retention_GotFocus(object sender, RoutedEventArgs e)
    {
        ValidateRetention();
    }

    private bool ValidateRetention()
    {
        bool flag = false;
        int result;
        if (!RetentionTextBox.IsEnabled)
        {
            flag = true;
        }
        else if (int.TryParse(RetentionTextBox.Text, out result))
        {
            flag = result >= 1 && result < 10000;
        }

        RetentionTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    public bool VerifyFields()
    {
        return true;
    }

    private void RecreateDatabase_OnClick(object sender, RoutedEventArgs e)
    {
        if (Interaction.MsgBox(Words.AreYouSureYouWantToRecreateDb, MsgBoxStyle.OkCancel, Words.Attention) == MsgBoxResult.Ok)
        {
            Settings.Default.RecreateDbScheduled = true;
            Settings.Default.Save();
            Log.Debug("Database scheduled to be recreated");
            UpdateManager.RestartApp();
        }
    }
}
