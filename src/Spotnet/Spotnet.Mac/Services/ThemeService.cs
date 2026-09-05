using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using NLog;

namespace Spotnet.Mac.Services;

/// <summary>
/// Swaps the application palette between the three styles the Windows client offers.
/// Each style is a resource dictionary under Themes/ carrying the same brush keys, with
/// the colour values lifted from the Windows client's style/*.xaml so both clients look
/// the same.
/// </summary>
public sealed class ThemeService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static ThemeService? _instance;

    public static ThemeService Instance => _instance ??= new ThemeService();

    /// <summary>The palette currently merged into Application.Resources, if any.</summary>
    private ResourceInclude? _active;

    public AppThemeStyle CurrentStyle { get; private set; } = AppThemeStyle.ModernLight;

    public event Action<AppThemeStyle>? ThemeChanged;

    public void ApplyTheme(AppThemeStyle style)
    {
        CurrentStyle = style;

        var app = Application.Current;
        if (app != null)
        {
            // The Fluent controls still need to know whether they are drawing on a light
            // or a dark ground; the palette below then paints Spotnet's own chrome.
            app.RequestedThemeVariant = style == AppThemeStyle.ModernDark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

            string source = style switch
            {
                AppThemeStyle.ModernDark => "avares://Spotnet.Mac/Themes/ModernDark.axaml",
                AppThemeStyle.Classic    => "avares://Spotnet.Mac/Themes/Classic.axaml",
                _                        => "avares://Spotnet.Mac/Themes/ModernLight.axaml"
            };

            try
            {
                var baseUri = new Uri("avares://Spotnet.Mac/");
                var palette = new ResourceInclude(baseUri) { Source = new Uri(source) };

                if (_active != null)
                {
                    app.Resources.MergedDictionaries.Remove(_active);
                }
                app.Resources.MergedDictionaries.Add(palette);
                _active = palette;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load palette {0}: {1}", source, ex.Message);
            }
        }

        Log.Info("Applied theme: {0}", style);
        ThemeChanged?.Invoke(style);
    }
}
