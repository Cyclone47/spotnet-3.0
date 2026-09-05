using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Controls;
public partial class ShutdownComputerDialog : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private int _secondsLeft = 60;
    private Timer _timer;
    public ShutdownComputerDialog(string text)
    {
        base.Initialized += ProviderSelectie_Initialized;
        base.Closed += OnClosed;
        InitializeComponent();
        MainLabel.Content = text;
    }

    private void OnClosed(object sender, EventArgs eventArgs)
    {
        if (_timer != null)
        {
            _timer.Stop();
        }
    }

    private void ProviderSelectie_Initialized(object sender, EventArgs e)
    {
        Log.Debug("Shutdown PC dialog");
        UpdateSecondsLeft(_secondsLeft);
        CancelButton.Focus();
        _timer = new Timer(1000.0)
        {
            AutoReset = true
        };
        _timer.Elapsed += TimerOnElapsed;
        _timer.Start();
    }

    private void TimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
    {
        if (--_secondsLeft == 0)
        {
            _timer.Stop();
            DoShutdown();
        }

        UpdateSecondsLeft(_secondsLeft);
    }

    private void UpdateSecondsLeft(int secondsLeft)
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            SecondsLeftLabel.Content = $"({secondsLeft})";
        });
    }

    private void DoShutdown()
    {
        OperatingSystemHelper.ShutdownComputerNow();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShutdownNowButton_Click(object sender, RoutedEventArgs e)
    {
        DoShutdown();
    }
}