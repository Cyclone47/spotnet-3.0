using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;

internal class CloseableTabItem : TabItem
{
	private static readonly Logger Log;

	public bool AutoSelect;

	private TabItem _parentTab;

	private bool _resetParentOnNextReset = true;

	static CloseableTabItem()
	{
		Log = LogManager.GetCurrentClassLogger();
		FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(CloseableTabItem), new FrameworkPropertyMetadata(typeof(CloseableTabItem)));
		Sys.MainWindow.TabSelectionChanged += ResetAllParentTabs;
	}

	public CloseableTabItem()
	{
		base.MouseDown += CloseableTabItem_MouseDown;
		AutoSelect = true;
	}

	private void CloseableTabItem_MouseDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		if (e.MiddleButton == MouseButtonState.Pressed)
		{
			CloseMe();
		}
		else if (e.RightButton == MouseButtonState.Pressed)
		{
			CloseMe();
		}
		else if (e.OriginalSource is ScrollViewer)
		{
			CloseMe();
		}
	}

	private void CloseMe(object sender, RoutedEventArgs e)
	{
		CloseMe();
	}

	public void CloseMe()
	{
		try
		{
			TabControl tabControl = (TabControl)base.Parent;
			if (tabControl != null)
			{
				if (_parentTab != null && tabControl.Items.IndexOf(_parentTab) > -1)
				{
					tabControl.SelectedIndex = tabControl.Items.IndexOf(_parentTab);
				}
				else if (Settings.Default.DownloadAction <= 1 && tabControl.SelectedIndex == 2 && tabControl.Items.Count == 3)
				{
					tabControl.SelectedIndex = 0;
				}
				tabControl.Items.Remove(this);
				Sys.MainWindow.SaveTabs();
				if (base.Content is ICloseableView closeableView)
				{
					closeableView.Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	internal void SetParentTab(TabItem parrentTab)
	{
		_parentTab = parrentTab;
		_resetParentOnNextReset = false;
	}

	private static void ResetAllParentTabs()
	{
		TabControl tabControl = Sys.MainWindow.TabControl1;
		if (tabControl == null)
		{
			return;
		}
		foreach (object item in (IEnumerable)tabControl.Items)
		{
			if (item is CloseableTabItem closeableTabItem)
			{
				if (closeableTabItem._resetParentOnNextReset)
				{
					closeableTabItem.SetParentTab(null);
				}
				else
				{
					closeableTabItem._resetParentOnNextReset = true;
				}
			}
		}
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (GetTemplateChild("PART_Close") is UIElement uIElement)
		{
			uIElement.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(CloseMe));
		}
		if (AutoSelect)
		{
			base.IsSelected = true;
		}
		AutoSelect = false;
	}
}
