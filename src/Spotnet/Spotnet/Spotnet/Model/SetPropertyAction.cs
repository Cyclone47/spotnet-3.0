using System.Reflection;
using System.Windows;
using Microsoft.Xaml.Behaviors;
using Spotnet.DataVirtualization;

namespace Spotnet.Model;

public class SetPropertyAction : TriggerAction<FrameworkElement>
{
	public static readonly DependencyProperty PropertyNameProperty = DependencyProperty.Register("PropertyName", typeof(string), typeof(SetPropertyAction));

	public static readonly DependencyProperty PropertyValueProperty = DependencyProperty.Register("PropertyValue", typeof(object), typeof(SetPropertyAction));

	public static readonly DependencyProperty TargetObjectProperty = DependencyProperty.Register("TargetObject", typeof(object), typeof(SetPropertyAction));

	public string PropertyName
	{
		get
		{
			return (string)GetValue(PropertyNameProperty);
		}
		set
		{
			SetValue(PropertyNameProperty, value);
		}
	}

	public object PropertyValue
	{
		get
		{
			return GetValue(PropertyValueProperty);
		}
		set
		{
			SetValue(PropertyValueProperty, value);
		}
	}

	public object TargetObject
	{
		get
		{
			return GetValue(TargetObjectProperty);
		}
		set
		{
			SetValue(TargetObjectProperty, value);
		}
	}

	protected override void Invoke(object parameter)
	{
		object obj = TargetObject ?? base.AssociatedObject;
		if (PropertyName.Equals("IsAnimatedAlready"))
		{
			if (obj is VirtualListItem<ISpotRow> virtualListItem)
			{
				virtualListItem.Data.IsAnimatedAlready = bool.Parse((string)PropertyValue);
			}
		}
		else
		{
			obj.GetType().GetProperty(PropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.InvokeMethod).SetValue(obj, PropertyValue);
		}
	}
}
