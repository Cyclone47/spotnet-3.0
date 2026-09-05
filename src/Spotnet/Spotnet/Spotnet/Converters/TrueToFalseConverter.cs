namespace Spotnet.Converters;

public sealed class TrueToFalseConverter : BooleanConverter<bool>
{
	public TrueToFalseConverter()
		: base(trueValue: false, falseValue: true)
	{
	}
}
