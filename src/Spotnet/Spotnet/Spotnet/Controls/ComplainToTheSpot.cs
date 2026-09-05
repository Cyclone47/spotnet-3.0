using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class ComplainToTheSpot : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public string Result;
    public bool AddToBlacklist;
    public ComplainToTheSpot(string title, string poster, string posterId, bool isItToRemove)
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
        ComplainTextBox.TextChanged += ComplainTextBoxOnTextChanged;
        base.Owner = Sys.MainWindow;
        if (isItToRemove)
        {
            base.Title = Words.SpotRemove;
            AddToBlacklistCheckBox.Visibility = Visibility.Collapsed;
            BodyText.Content = Words.SpotRemoveAskDialogText;
            MainGrid.RowDefinitions[1].MaxHeight = 0.0;
        }

        LabelForTitle.Content = title;
        LabelForPoster.Content = $"{poster} ({posterId})";
    }

    private void ComplainTextBoxOnTextChanged(object sender, TextChangedEventArgs args)
    {
        OkButton.IsEnabled = ComplainTextBox.Text.Length > 3;
    }

    private void OnInitialized(object sender, EventArgs eventArgs)
    {
        ComplainTextBox.Focus();
    }

    private void DoButton()
    {
        Result = ComplainTextBox.Text;
        AddToBlacklist = AddToBlacklistCheckBox.IsChecked.GetValueOrDefault();
        Close();
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DoButton();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}