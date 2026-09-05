using System.Windows;
using System.Windows.Controls;

namespace Spotnet.Helpers;

/// <summary>
/// Choose our named styles rather than the MenuItem style in MahApps' default
/// Menu template resources. Applies to generated items and derived MenuItems too.
/// Separators need their own style, not a MenuItem-targeted ItemContainerStyle.
/// </summary>
public sealed class MenuItemStyleSelector : StyleSelector
{
    public override Style SelectStyle(object item, DependencyObject container)
    {
        if (container is FrameworkElement element)
        {
            if (container is MenuItem) return element.TryFindResource("SpotnetMenuItemStyle") as Style;
            if (container is Separator) return element.TryFindResource("SpotnetMenuSeparator") as Style;
        }
        return base.SelectStyle(item, container);
    }
}
