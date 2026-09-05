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

namespace Spotnet.Controls;
public partial class SimpleTextDialog : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public string Result;
    public SimpleTextDialog()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
        base.Owner = Sys.MainWindow;
    }

    private void OnInitialized(object sender, EventArgs eventArgs)
    {
        MainTextBox.Focus();
    }

    private void DoButton()
    {
        Result = MainTextBox.Text;
        Close();
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DoButton();
    }
}