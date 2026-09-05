using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Spotnet.Controls;

internal static class VirtualToggleButton
{
	public static readonly DependencyProperty IsCheckedProperty;

	public static readonly DependencyProperty IsThreeStateProperty;

	public static readonly DependencyProperty IsVirtualToggleButtonProperty;

	static VirtualToggleButton()
	{
		IsCheckedProperty = DependencyProperty.RegisterAttached("IsChecked", typeof(bool?), typeof(VirtualToggleButton), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal, OnIsCheckedChanged));
		IsThreeStateProperty = DependencyProperty.RegisterAttached("IsThreeState", typeof(bool), typeof(VirtualToggleButton), new FrameworkPropertyMetadata(false));
		IsVirtualToggleButtonProperty = DependencyProperty.RegisterAttached("IsVirtualToggleButton", typeof(bool), typeof(VirtualToggleButton), new FrameworkPropertyMetadata(false, OnIsVirtualToggleButtonChanged));
	}

	private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (!(d is UIElement target))
		{
			return;
		}
		bool? flag = (bool?)e.NewValue;
		bool? flag2 = flag;
		if (flag2.GetValueOrDefault())
		{
			RaiseCheckedEvent(target);
			return;
		}
		bool? flag3;
		if (!flag.HasValue)
		{
			flag3 = null;
		}
		else
		{
			flag2 = !flag.GetValueOrDefault();
			flag3 = flag2;
		}
		if (flag3.GetValueOrDefault())
		{
			RaiseUncheckedEvent(target);
		}
		else
		{
			RaiseIndeterminateEvent(target);
		}
	}

	private static void OnIsVirtualToggleButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is IInputElement inputElement)
		{
			if (Convert.ToBoolean(e.NewValue))
			{
				inputElement.MouseLeftButtonDown += OnMouseLeftButtonDown;
				inputElement.KeyDown += OnKeyDown;
			}
			else
			{
				inputElement.MouseLeftButtonDown -= OnMouseLeftButtonDown;
				inputElement.KeyDown -= OnKeyDown;
			}
		}
	}

	private static void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.OriginalSource != sender)
		{
			return;
		}
		if (e.Key == Key.Space)
		{
			if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
			{
				UpdateIsChecked(sender as DependencyObject);
				e.Handled = true;
			}
		}
		else if (e.Key == Key.Return && Convert.ToBoolean((sender as DependencyObject).GetValue(KeyboardNavigation.AcceptsReturnProperty)))
		{
			UpdateIsChecked(sender as DependencyObject);
			e.Handled = true;
		}
	}

	private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		UpdateIsChecked(sender as DependencyObject);
	}

	private static void UpdateIsChecked(DependencyObject d)
	{
		bool? isChecked = GetIsChecked(d);
		bool? flag = isChecked;
		SetIsChecked(d, flag.GetValueOrDefault() ? GetIsThreeState(d) : isChecked.HasValue);
	}

	internal static RoutedEventArgs RaiseCheckedEvent(UIElement target)
	{
		if (target == null)
		{
			return null;
		}
		RoutedEventArgs routedEventArgs = new RoutedEventArgs
		{
			RoutedEvent = ToggleButton.CheckedEvent
		};
		target.RaiseEvent(routedEventArgs);
		return routedEventArgs;
	}

	internal static RoutedEventArgs RaiseIndeterminateEvent(UIElement target)
	{
		if (target == null)
		{
			return null;
		}
		RoutedEventArgs routedEventArgs = new RoutedEventArgs
		{
			RoutedEvent = ToggleButton.IndeterminateEvent
		};
		target.RaiseEvent(routedEventArgs);
		return routedEventArgs;
	}

	internal static RoutedEventArgs RaiseUncheckedEvent(UIElement target)
	{
		if (target == null)
		{
			return null;
		}
		RoutedEventArgs routedEventArgs = new RoutedEventArgs
		{
			RoutedEvent = ToggleButton.UncheckedEvent
		};
		target.RaiseEvent(routedEventArgs);
		return routedEventArgs;
	}

	public static bool? GetIsChecked(DependencyObject d)
	{
		return (bool?)d.GetValue(IsCheckedProperty);
	}

	public static bool GetIsThreeState(DependencyObject d)
	{
		return Convert.ToBoolean(d.GetValue(IsThreeStateProperty));
	}

	public static bool GetIsVirtualToggleButton(DependencyObject d)
	{
		return Convert.ToBoolean(d.GetValue(IsVirtualToggleButtonProperty));
	}

	public static void SetIsChecked(DependencyObject d, bool? value)
	{
		d.SetValue(IsCheckedProperty, value);
	}

	public static void SetIsThreeState(DependencyObject d, bool value)
	{
		d.SetValue(IsThreeStateProperty, value);
	}

	public static void SetIsVirtualToggleButton(DependencyObject d, bool value)
	{
		d.SetValue(IsVirtualToggleButtonProperty, value);
	}
}
