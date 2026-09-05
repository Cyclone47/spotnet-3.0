using System.Windows.Media;

namespace Spotnet.Helpers;

/// <summary>Brushes used by background-loaded models must be immutable across dispatchers.</summary>
public static class ThemeBrushes
{
    public static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public static readonly SolidColorBrush DarkFilterBackground = Frozen("#111B27");
}
