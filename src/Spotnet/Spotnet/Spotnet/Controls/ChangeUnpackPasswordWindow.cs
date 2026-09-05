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
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Views;

namespace Spotnet.Controls;
public partial class ChangeUnpackPasswordWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private bool _initializationFinished;
    private string _lastSettingsString;
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    private string _oldUnpackPassword;
    public string Password;
    public bool BSuc;
    public static bool IsRunning => DispatcherHelper.UIDispatcher.Invoke(() => Application.Current.Windows.OfType<SelectProviderWindow>().Any());
    private string CurrentSettingsString => PasswordTextBox.Text;
    private bool AreSettingsChanged => _lastSettingsString != CurrentSettingsString;

    public ChangeUnpackPasswordWindow(string oldUnpackPassword)
    {
        _oldUnpackPassword = oldUnpackPassword;
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
            PasswordCheckBox.IsChecked = !_oldUnpackPassword.IsNullOrEmpty();
            PasswordTextBox.IsEnabled = PasswordCheckBox.IsChecked.GetValueOrDefault();
            if (PasswordTextBox.IsEnabled)
            {
                PasswordTextBox.Text = _oldUnpackPassword;
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
                PasswordTextBox
            };
            OkButton.IsEnabled = AreSettingsChanged && !source.Any((Control f) => object.Equals(f.Background, _fieldInvalidBackground));
        }
    }

    private bool ValidatePassword()
    {
        bool flag = !PasswordTextBox.IsEnabled || PasswordTextBox.Text.Length < 300;
        PasswordTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, flag ? "WhiteColorBrush" : "NoticeBackgroundBrush");
        return flag;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        OkButton.Focus();
        UpdateLayout();
        Password = ((!PasswordCheckBox.IsChecked.GetValueOrDefault()) ? null : PasswordTextBox.Text);
        BSuc = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Password_GotFocus(object sender, RoutedEventArgs e)
    {
        ValidatePassword();
        UpdateOkButtonState();
    }

    private void PasswordTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidatePassword();
        UpdateOkButtonState();
    }

    private void PasswordCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.IsEnabled = PasswordCheckBox.IsChecked.GetValueOrDefault();
        PasswordTextBox.Text = "";
        ValidatePassword();
        UpdateOkButtonState();
    }
}
