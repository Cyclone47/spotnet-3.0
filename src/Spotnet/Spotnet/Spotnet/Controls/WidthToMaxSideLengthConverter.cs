using System;
using System.Globalization;
using System.Windows.Data;

namespace Spotnet.Controls;

internal class WidthToMaxSideLengthConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is double num)
		{
			return (num < 16.0) ? 16.0 : num;
		}
		return null;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
