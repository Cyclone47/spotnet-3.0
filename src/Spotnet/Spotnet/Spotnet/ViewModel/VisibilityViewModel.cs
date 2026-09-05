using System;
using System.Collections.Generic;
using System.Windows;
using Spotnet.Mvvm;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Views;

namespace Spotnet.ViewModel;

public class VisibilityViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private Dictionary<HideableElement, bool> _visibilityFlags;

	internal Action FontSizeChanged;

	private byte _fontSize = Settings.Default.FontSize;

	private byte _spotFontSize = Settings.Default.SpotFontSize;

	public byte FontSize
	{
		get
		{
			return Settings.Default.FontSize;
		}
		set
		{
			if (_fontSize != value)
			{
				_fontSize = value;
				Settings.Default.FontSize = _fontSize;
				Settings.Default.Save();
				UpdateFontSize();
				RaisePropertyChanged("FontSize");
				FontSizeChanged?.Invoke();
			}
		}
	}

	public byte SpotFontSize
	{
		get
		{
			return Settings.Default.SpotFontSize;
		}
		set
		{
			if (_spotFontSize != value)
			{
				_spotFontSize = value;
				Settings.Default.SpotFontSize = _spotFontSize;
				Settings.Default.Save();
				Sys.MainWindow.ReloadAllSpotPages();
			}
		}
	}

	public bool IsVisibleStatusBar => _visibilityFlags[HideableElement.StatusBar];

	public bool IsVisibleSearch => _visibilityFlags[HideableElement.Search];

	public bool IsVisibleFilters => _visibilityFlags[HideableElement.Filters];

	public bool IsVisibleAddFilter => _visibilityFlags[HideableElement.AddFilter];

	public bool IsVisibleMainMenu => _visibilityFlags[HideableElement.MainMenu];

	public bool IsVisibleLeftPanel => _visibilityFlags[HideableElement.LeftPanel];

	public bool IsVisibleMainToolbar => _visibilityFlags[HideableElement.MainToolbar];

	public VisibilityViewModel()
	{
		SetVisibilityFlags();
		UpdateFontSize();
	}

	private void SetVisibilityFlags()
	{
		_visibilityFlags = new Dictionary<HideableElement, bool>();
		_visibilityFlags[HideableElement.StatusBar] = Settings.Default.VisibleStatusBar;
		_visibilityFlags[HideableElement.Search] = Settings.Default.VisibleSearch;
		_visibilityFlags[HideableElement.Filters] = Settings.Default.VisibleFilters;
		_visibilityFlags[HideableElement.AddFilter] = Settings.Default.VisibleAddFilter;
		_visibilityFlags[HideableElement.MainMenu] = Settings.Default.VisibleMainMenu;
		_visibilityFlags[HideableElement.LeftPanel] = Settings.Default.VisibleLeftPanel;
		_visibilityFlags[HideableElement.MainToolbar] = Settings.Default.VisibleMainToolbar;
	}

	private void UpdateFontSize()
	{
		double num = Convert.ToDouble(FontSize);
		Application.Current.Resources["HeaderFontSize"] = num + 26.0;
		Application.Current.Resources["SubHeaderFontSize"] = num + 14.0;
		Application.Current.Resources["WindowTitleFontSize"] = num + 2.0;
		Application.Current.Resources["NormalFontSize"] = num;
		Application.Current.Resources["ContentFontSize"] = num - 2.0;
		Application.Current.Resources["FlatButtonFontSize"] = num;
		Application.Current.Resources["TabItemFontSize"] = num + 12.0;
		Application.Current.Resources["UpperCaseContentFontSize"] = num - 4.0;
		Application.Current.Resources["MenuFontSize"] = num - 1.0;
		Application.Current.Resources["ContextMenuFontSize"] = num - 1.0;
	}

	internal void UpdateVisibility(HideableElement element, bool visible)
	{
		_visibilityFlags[element] = visible;
		MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
		switch (element)
		{
		case HideableElement.StatusBar:
			Settings.Default.VisibleStatusBar = visible;
			RaisePropertyChanged("IsVisibleStatusBar");
			break;
		case HideableElement.Search:
			Settings.Default.VisibleSearch = visible;
			RaisePropertyChanged("IsVisibleSearch");
			break;
		case HideableElement.Filters:
			Settings.Default.VisibleFilters = visible;
			RaisePropertyChanged("IsVisibleFilters");
			break;
		case HideableElement.AddFilter:
			Settings.Default.VisibleAddFilter = visible;
			RaisePropertyChanged("IsVisibleAddFilter");
			break;
		case HideableElement.MainMenu:
			Settings.Default.VisibleMainMenu = visible;
			RaisePropertyChanged("IsVisibleMainMenu");
			mainWindow.UpdateMainMenuVisibility();
			break;
		case HideableElement.LeftPanel:
			Settings.Default.VisibleLeftPanel = visible;
			RaisePropertyChanged("IsVisibleLeftPanel");
			mainWindow.UpdateMainMenuVisibility();
			break;
		case HideableElement.MainToolbar:
			Settings.Default.VisibleMainToolbar = visible;
			RaisePropertyChanged("IsVisibleMainToolbar");
			break;
		default:
			throw new Exception("Such an element is not supported.");
		}
		Settings.Default.Save();
	}
}
