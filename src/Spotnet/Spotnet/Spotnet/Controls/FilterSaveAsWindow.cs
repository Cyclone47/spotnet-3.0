using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Controls;
public partial class FilterSaveAsWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Brush _fieldValidBackground => (Brush)FindResource("WhiteColorBrush");
    private Brush _fieldInvalidBackground => (Brush)FindResource("NoticeBackgroundBrush");
    public string NewName = "";
    public FilterSaveAsWindow()
    {
        base.Closing += FilterSave_Closing;
        InitializeComponent();
    }

    private void FilterSave_Closing(object sender, CancelEventArgs e)
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

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        if (NameTextBox.Background.Equals(_fieldInvalidBackground))
        {
            NameTextBox.Focus();
            return;
        }

        NewName = NameTextBox.Text.Trim();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void NameTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        Verify();
    }

    private void NameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        Verify();
    }

    private void Verify()
    {
        string text = NameTextBox.Text.Trim();
        Regex regex = new Regex("^[a-zA-Z0-9\\ ]+$");
        if (!text.IsNullOrEmpty() && text.Length < 18 && regex.IsMatch(text))
        {
            NameTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WhiteColorBrush");
        }
        else
        {
            NameTextBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "NoticeBackgroundBrush");
        }
    }
}
