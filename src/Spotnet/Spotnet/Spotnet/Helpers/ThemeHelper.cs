using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MahApps.Metro;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;

namespace Spotnet.Helpers;

public static class ThemeHelper
{
    /// <summary>The original Spotnet look, with the original bitmap filter icons.</summary>
    public const string Classic = "ClassicLight";

    /// <summary>Classic's palette, with the filter icons drawn from FontAwesome.</summary>
    public const string ModernLight = "ModernLight";

    /// <summary>The dark palette, with the same FontAwesome filter icons as Modern Light.</summary>
    public const string ModernDark = "ModernDark";

    /// <summary>The settings value Classic was stored under before it was renamed.</summary>
    public const string ClassicLight = Classic;

    private static readonly string[] KnownThemes = { Classic, ModernLight, ModernDark };

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static event Action ThemeChanged;

    private static string _appliedTheme;
    public static string CurrentTheme => _appliedTheme ?? Normalize(Settings.Default.AppTheme);

    public static bool IsModernDark => string.Equals(CurrentTheme, ModernDark, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for the two Modern styles, which draw filter icons as FontAwesome glyphs
    /// instead of the bitmaps Classic uses.
    /// </summary>
    public static bool UsesGlyphIcons => !string.Equals(CurrentTheme, Classic, StringComparison.OrdinalIgnoreCase);

    /// <summary>The palette dictionary each style loads out of Style/.</summary>
    private static string ThemeDictionaryFor(string themeName) => themeName switch
    {
        ModernDark => "moderndark.xaml",
        ModernLight => "modernlight.xaml",
        _ => "classiclight.xaml",
    };

    /// <summary>
    /// Recognises a palette dictionary so a theme switch can drop the previous one.
    /// blueedited.xaml is a retired palette that older settings may still have merged.
    /// </summary>
    private static bool IsThemeDictionary(string source) =>
        new[] { "classiclight.xaml", "modernlight.xaml", "moderndark.xaml", "blueedited.xaml" }
            .Any(name => source.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>Falls back to Classic for an empty or unrecognised setting.</summary>
    private static string Normalize(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return Classic;
        }

        return KnownThemes.FirstOrDefault(
            t => string.Equals(t, themeName.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Classic;
    }

    public static void Initialize()
    {
        try
        {
            ApplyTheme(Normalize(Settings.Default.AppTheme), persist: false);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    public static void ApplyTheme(string themeName, bool persist = true)
    {
        themeName = Normalize(themeName);
        bool isDark = string.Equals(themeName, ModernDark, StringComparison.OrdinalIgnoreCase);

        if (persist)
        {
            Settings.Default.AppTheme = themeName;
            Settings.Default.Save();
        }

        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var app = Application.Current;
                if (app == null) return;
                _appliedTheme = themeName;

                // 1. Swap MahApps Metro BaseDark / BaseLight theme
                try
                {
                    // MahApps 2 replaced the separate accent and base theme with one
                    // named theme, and moved the manager itself into ControlzEx.
                    ControlzEx.Theming.ThemeManager.Current.ChangeTheme(
                        app, isDark ? "Dark.Blue" : "Light.Blue");
                }
                catch (Exception ex)
                {
                    Log.Warn("MahApps ChangeAppStyle notice: {0}", ex.Message);
                }

                // 2. Swap our custom theme palette dictionary.
                //
                // The light theme loads classiclight.xaml, not blueedited.xaml. The latter
                // defines only 31 of the 51 keys the controls reference - it is missing
                // BackgroundSelected, BackgroundNotSelected, SpotBackgroundBrush and the
                // whole GrayBrush set. Those resolved to nothing after a theme switch, so
                // the selected tab lost its background while keeping a white foreground
                // and became unreadable. classiclight.xaml is the complete counterpart to
                // moderndark.xaml and defines every key.
                string targetDictName = ThemeDictionaryFor(themeName);
                var merged = app.Resources.MergedDictionaries;

                // Remove existing theme dictionaries
                var existing = merged.Where(d =>
                    d.Source != null && (
                        IsThemeDictionary(d.Source.OriginalString)
                    )).ToList();

                // Insert the new theme dictionary
                var newDict = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Spotnet;component/Style/{targetDictName}", UriKind.Absolute)
                };
                merged.Add(newDict);
                // Add before removing so live controls never see missing resources.
                foreach (var d in existing) merged.Remove(d);

                // 3. Update active windows
                foreach (Window win in app.Windows)
                {
                    try
                    {
                        var winMerged = win.Resources.MergedDictionaries;
                        var winExisting = winMerged.Where(d =>
                            d.Source != null && (
                                IsThemeDictionary(d.Source.OriginalString)
                            )).ToList();

                        winMerged.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/Spotnet;component/Style/{targetDictName}", UriKind.Absolute)
                        });
                        foreach (var d in winExisting) winMerged.Remove(d);

                        // The dictionary above only carries Spotnet's own brushes. The
                        // MahApps control templates - radio buttons, text boxes, buttons -
                        // read MahApps.Brushes.* keys, so a window that has picked up its
                        // own theme keeps painting its controls light while the panels
                        // around them turn dark. Theme each window explicitly as well.
                        try
                        {
                            ControlzEx.Theming.ThemeManager.Current.ChangeTheme(
                                win, isDark ? "Dark.Blue" : "Light.Blue");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Window theme notice: {0}", ex.Message);
                        }

                        if (win is MahApps.Metro.Controls.MetroWindow metroWin)
                        {
                            metroWin.SetResourceReference(MahApps.Metro.Controls.MetroWindow.WindowTitleBrushProperty, "WindowTitleColorBrush");
                            metroWin.SetResourceReference(MahApps.Metro.Controls.MetroWindow.NonActiveWindowTitleBrushProperty, "NonActiveWindowTitleBrush");
                            metroWin.SetResourceReference(MahApps.Metro.Controls.MetroWindow.TitleForegroundProperty, "WindowTitleForegroundBrush");
                        }

                        win.InvalidateVisual();
                    }
                    catch
                    {
                        // Ignore per-window refresh errors
                    }
                }

                ThemeChanged?.Invoke();

                // Reload open spot pages; WPF surfaces update through dynamic resources.
                try
                {
                    SpotParser.ResetThemeFiles();
                    // Row colors are dynamic resources. Do not clear/requery a list
                    // while its background provider is adding spots just to repaint it.
                    Sys.MainWindow?.ReloadAllSpotPages();
                }
                catch { }

                Log.Info("Applied theme: {0}", themeName);
            });
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }
}
