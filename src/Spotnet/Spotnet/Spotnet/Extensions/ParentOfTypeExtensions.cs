using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Spotnet.Extensions;

public static class ParentOfTypeExtensions
{
	public static T ParentOfType<T>(this DependencyObject element) where T : DependencyObject
	{
		if (element == null)
		{
			return null;
		}
		return element.GetParents().OfType<T>().FirstOrDefault();
	}

	public static bool IsAncestorOf(this DependencyObject element, DependencyObject descendant)
	{
		if (element == null)
		{
			throw new ArgumentNullException("element");
		}
		if (descendant == null)
		{
			throw new ArgumentNullException("descendant");
		}
		if (descendant != element)
		{
			return descendant.GetParents().Contains(element);
		}
		return true;
	}

	public static T GetVisualParent<T>(this DependencyObject element) where T : DependencyObject
	{
		return element.ParentOfType<T>();
	}

	internal static IEnumerable<T> GetAncestors<T>(this DependencyObject element) where T : class
	{
		return element.GetParents().OfType<T>();
	}

	internal static T GetParent<T>(this DependencyObject element) where T : FrameworkElement
	{
		return element.ParentOfType<T>();
	}

	public static IEnumerable<DependencyObject> GetParents(this DependencyObject element)
	{
		if (element == null)
		{
			throw new ArgumentNullException("element");
		}
		while (true)
		{
			DependencyObject parent;
			element = (parent = element.GetParent());
			if (parent != null)
			{
				yield return element;
				continue;
			}
			break;
		}
	}

	private static DependencyObject GetParent(this DependencyObject element)
	{
		DependencyObject dependencyObject = null;
		try
		{
			dependencyObject = VisualTreeHelper.GetParent(element);
		}
		catch (InvalidOperationException)
		{
			dependencyObject = null;
		}
		if (dependencyObject == null && element is FrameworkElement frameworkElement)
		{
			dependencyObject = frameworkElement.Parent;
		}
		return dependencyObject;
	}
}
