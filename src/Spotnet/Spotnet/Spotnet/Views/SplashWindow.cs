using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Views;

/// <summary>
/// Custom splash window with animated progress bar and localized startup status messages.
/// Replaces the built-in WPF SplashScreen so we can show live progress during startup.
/// </summary>
public partial class SplashWindow : Window
{
    private static SplashWindow _instance;
    private static readonly object _lock = new();
    internal static SplashWindow Current => _instance;

    // Total progress bar track width (matches XAML StackPanel Width)
    private const double TrackWidth = 420.0;

    // Localized startup step messages — NL / EN
    private static readonly string[] StepsNL =
    {
        "Opstarten...",           // 0%
        "Instellingen laden...",  // 14%
        "Servers laden...",       // 28%
        "Filters laden...",       // 45%
        "Database verbinden...",  // 60%
        "Database controleren...",// 75%
        "Interface gereed...",    // 88%
        "Klaar!"                  // 100%
    };

    private static readonly string[] StepsEN =
    {
        "Starting up...",
        "Loading settings...",
        "Loading servers...",
        "Loading filters...",
        "Connecting to database...",
        "Verifying database...",
        "Preparing interface...",
        "Ready!"
    };

    private static string[] Steps => UserLanguageHelper.Language == UserLanguageHelper.Dutch
        ? StepsNL
        : StepsEN;

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + GetVersion();
        SetStep(0);
    }

    private static string GetVersion()
    {
        try { return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0"; }
        catch { return "2.0"; }
    }

    /// <summary>Show the splash window on the UI thread (call once from App.OnStartup).</summary>
    public static void ShowSplash()
    {
        lock (_lock)
        {
            if (_instance != null) return;
            _instance = new SplashWindow();
            _instance.Show();
        }
    }

    /// <summary>Advance the progress bar to a named startup step (0-7). Thread-safe.</summary>
    public static void SetProgress(int step)
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    _instance?.SetStep(step);
                }
            });
        }
        catch { /* splash already closed — ignore */ }
    }

    /// <summary>
    /// Replaces the step text without moving the progress bar.
    /// </summary>
    /// <remarks>
    /// For work that happens inside one step and takes long enough that a user would
    /// otherwise assume the application has hung - the one-time search index rebuild is
    /// the reason this exists. The bar stays where it is because there is no progress to
    /// report: the rebuild is a single SQLite statement.
    /// </remarks>
    public static void SetMessage(string dutch, string english)
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    if (_instance != null)
                    {
                        _instance.StatusText.Text =
                            UserLanguageHelper.Language == UserLanguageHelper.Dutch ? dutch : english;
                    }
                }
            });
        }
        catch { /* splash already closed - ignore */ }
    }

    /// <summary>Close the splash window smoothly. Thread-safe.</summary>
    public static void CloseSplash()
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    if (_instance == null) return;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (_, _) =>
                    {
                        lock (_lock)
                        {
                            _instance?.Close();
                            _instance = null;
                        }
                    };
                    _instance.BeginAnimation(OpacityProperty, fadeOut);
                }
            });
        }
        catch
        {
            try { Application.Current?.Dispatcher.Invoke(() => { _instance?.Close(); _instance = null; }); }
            catch { _instance = null; }
        }
    }

    // ----- Private helpers -----

    private void SetStep(int step)
    {
        string[] steps = Steps;
        step = Math.Max(0, Math.Min(step, steps.Length - 1));
        StatusText.Text = steps[step];

        double pct = steps.Length <= 1 ? 1.0 : (double)step / (steps.Length - 1);

        // Measure actual track width from the parent border
        double trackWidth = TrackWidth;
        var track = ProgressFill.Parent as FrameworkElement;
        if (track != null && track.ActualWidth > 0)
            trackWidth = track.ActualWidth;

        double targetWidth = Math.Max(0, pct * trackWidth);

        var anim = new DoubleAnimation(ProgressFill.Width, targetWidth,
            TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }
}
