using System.Windows;
using System.Windows.Controls;

namespace Spotnet.Controls;

internal class UnCloseableTabItem : TabItem
{
	public bool AutoSelect;

	public bool IsDownloadTab { get; set; }

	static UnCloseableTabItem()
	{
		FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(UnCloseableTabItem), new FrameworkPropertyMetadata(typeof(UnCloseableTabItem)));
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (AutoSelect)
		{
			base.IsSelected = true;
		}
		AutoSelect = false;
	}
}
