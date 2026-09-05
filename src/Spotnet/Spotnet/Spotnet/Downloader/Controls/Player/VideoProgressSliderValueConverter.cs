using System;
using System.Globalization;
using System.Windows.Data;

namespace Spotnet.Downloader.Controls.Player;

public class VideoProgressSliderValueConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		double num = ((values[0] != null) ? ((double)values[0]) : 0.0);
		double num2 = (double)values[1];
		double num3 = (double)values[2];
		return (double)values[3] * (num - num2) / (num3 - num2);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
