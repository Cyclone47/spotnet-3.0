using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Downloader.Controls;
public partial class RemoveFilesFromTheDiskDialog : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public RemoveFilesFromTheDiskDialogAnswerEnum Answer = RemoveFilesFromTheDiskDialogAnswerEnum.Cancel;
    public RemoveFilesFromTheDiskDialog()
    {
        base.Initialized += ProviderSelectie_Initialized;
        InitializeComponent();
    }

    private void ProviderSelectie_Initialized(object sender, EventArgs e)
    {
        NoButton.Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Answer = RemoveFilesFromTheDiskDialogAnswerEnum.Yes;
        if (DoNotShowCheckbox.IsChecked.GetValueOrDefault())
        {
            Settings.Default.RemoveFilesOnDownloadRemove = 1;
            Settings.Default.Save();
        }

        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Answer = RemoveFilesFromTheDiskDialogAnswerEnum.No;
        if (DoNotShowCheckbox.IsChecked.GetValueOrDefault())
        {
            Settings.Default.RemoveFilesOnDownloadRemove = 0;
            Settings.Default.Save();
        }

        Close();
    }
}