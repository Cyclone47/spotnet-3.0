using System.Windows;

namespace Spotnet.Converters;

public sealed class BooleanToVisibilityConverter : BooleanConverter<Visibility>
{
	public BooleanToVisibilityConverter()
		: base(Visibility.Visible, Visibility.Collapsed)
	{
	}
}
