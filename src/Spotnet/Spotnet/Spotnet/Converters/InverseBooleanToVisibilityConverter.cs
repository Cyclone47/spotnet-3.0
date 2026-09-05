using System.Windows;

namespace Spotnet.Converters;

public sealed class InverseBooleanToVisibilityConverter : BooleanConverter<Visibility>
{
	public InverseBooleanToVisibilityConverter()
		: base(Visibility.Collapsed, Visibility.Visible)
	{
	}
}
