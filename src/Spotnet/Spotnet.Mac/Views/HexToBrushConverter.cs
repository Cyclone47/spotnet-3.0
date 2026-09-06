using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Spotnet.Mac.Views;

/// <summary>
/// Maakt van een hexkleur (#RRGGBB) een penseel — voor de categoriestreep in de
/// spotlijst en de stippen in de filterboom. Een lege of ongeldige waarde geeft
/// een transparant penseel, zodat de binding niet naar bindingsfouten leidt.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length > 0)
        {
            try
            {
                var brush = new SolidColorBrush(Color.Parse(hex));
                return brush;
            }
            catch (FormatException)
            {
                // val door naar transparant
            }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
