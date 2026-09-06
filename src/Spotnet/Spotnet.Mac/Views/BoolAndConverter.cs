using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Spotnet.Mac.Views;

/// <summary>
/// Logische EN van een bound vlag en een bound tweede vlag — voor de filterboom-stippen,
/// die alleen mogen tonen als de filter zelf een categorie heeft én Windows'
/// <c>ColoringFilters</c>-voorkeur aan staat. De tweede vlag komt binnen als
/// <see cref="ConverterParameter"/>; null of geen boolean telt als aan.
/// </summary>
public sealed class BoolAndConverter : IValueConverter
{
    public static readonly BoolAndConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool first = value is bool b && b;
        bool second = parameter is not bool flag || flag;
        return first && second;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
