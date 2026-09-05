using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;

namespace Spotnet.Controls;
public partial class ChangeLanguageConfirmDialog : MetroWindow
{
    public bool RestartNow;
    public ChangeLanguageConfirmDialog()
    {
        InitializeComponent();
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        RestartNow = true;
        Close();
    }

    private void RestartLater_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}