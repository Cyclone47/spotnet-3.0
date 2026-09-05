using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using Spotnet.Mvvm;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Controls;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.ViewModel;

public class MainWindowViewModel : ViewModelBase
{
	private static readonly Logger Log;

	private bool _showTrustedOnlyPressed;

	private int _tabItemsCount = 1;

	private readonly List<string> _alreadyAddedWarnings = new List<string>();

	private readonly object _lockRoot = new object();

	private static readonly ImageBrush FiltersBackgroundDefault;

	private Brush _filtersBackground = FiltersBackgroundDefault;
	private volatile string _filterBackgroundColor;

	private bool _isShowTrustedTemporaryDisabled;

	private readonly object _lockTemporaryShowTrusted = new object();

	private bool _isSearchResetVisible;

	public ObservableCollection<FrameworkElement> WarningsList { get; set; }

	internal Filters FiltersDb { get; private set; }

	public ObservableCollection<FilterViewModel> FiltersCollection => FiltersDb.FiltersRoot.Children;

	public Brush FiltersBackground
	{
		get
		{
			return _filtersBackground;
		}
		set
		{
			if (_filtersBackground == null || !_filtersBackground.Equals(value))
			{
				_filtersBackground = value;
				RaisePropertyChanged("FiltersBackground");
			}
		}
	}

	public bool ShowTrustedOnlyMode
	{
		get
		{
			if (!_isShowTrustedTemporaryDisabled)
			{
				return Settings.Default.ShowTrustedOnlyEnabled;
			}
			return false;
		}
		set
		{
			Settings.Default.ShowTrustedOnlyEnabled = value;
			Settings.Default.Save();
			if (value)
			{
				_isShowTrustedTemporaryDisabled = false;
			}
			Sys.MainWindow.RefreshSpotsList(force: true);
			RaisePropertyChanged("ShowTrustedOnlyMode");
			RaisePropertyChanged("ShowTrustedOnlyIcon");
			RaisePropertyChanged("ShowTrustedOnlyTooltip");
		}
	}

	public string FilterSelectedName
	{
		get
		{
			return Settings.Default.Filter;
		}
		set
		{
			if (!Settings.Default.Filter.Equals(value))
			{
				Settings.Default.Filter = value;
				Settings.Default.Save();
				RaisePropertyChanged("FilterSelectedName");
				Log.Debug("Filters list changed to " + value);
			}
		}
	}

	public string ShowTrustedOnlyIcon
	{
		get
		{
			if (!ShowTrustedOnlyMode)
			{
				return "\uf02b";
			}
			return "\uf02c";
		}
	}

	public bool ShowTrustedOnlyPressed
	{
		get
		{
			return _showTrustedOnlyPressed;
		}
		set
		{
			_showTrustedOnlyPressed = value;
			RaisePropertyChanged("ShowTrustedOnlyPressed");
		}
	}

	public string ShowTrustedOnlyTooltip
	{
		get
		{
			if (!ShowTrustedOnlyMode)
			{
				return Words.ShowTrustedOnlyEnableButtonTooltip;
			}
			return Words.ShowTrustedOnlyDisableButtonTooltip;
		}
	}

	public double TabItemTextWidth
	{
		get
		{
			int num = 1200 / TabItemsCount;
			if (num < 50)
			{
				num = 50;
			}
			else if (num > 400)
			{
				num = 400;
			}
			return num;
		}
	}

	public int TabItemsCount
	{
		get
		{
			return _tabItemsCount;
		}
		set
		{
			if (_tabItemsCount != value)
			{
				_tabItemsCount = value;
				RaisePropertyChanged("TabItemsCount");
				RaisePropertyChanged("TabItemTextWidth");
			}
		}
	}

	public bool IsSearchResetVisible
	{
		get
		{
			return _isSearchResetVisible;
		}
		set
		{
			if (_isSearchResetVisible != value)
			{
				_isSearchResetVisible = value;
				RaisePropertyChanged("IsSearchResetVisible");
			}
		}
	}

	static MainWindowViewModel()
	{
		Log = LogManager.GetCurrentClassLogger();
		StreamResourceInfo resourceStream = Application.GetResourceStream(new Uri("pack://application:,,,/Spotnet;component/Resources/ImagesInternal/spotsbg.png", UriKind.Absolute));
		if (resourceStream != null)
		{
			using var stream = resourceStream.Stream;
			FiltersBackgroundDefault = new ImageBrush
			{
				ImageSource = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
				TileMode = TileMode.Tile,
				ViewportUnits = BrushMappingMode.Absolute,
				Viewport = new Rect(new Point(0.0, 0.0), new Point(150.0, 150.0))
			};
			FiltersBackgroundDefault.Freeze();
		}
	}

	public MainWindowViewModel()
	{
		FiltersDb = new Filters();
		WarningsList = new ObservableCollection<FrameworkElement>();
		SetFiltersBackground(null);
		ThemeHelper.ThemeChanged += () =>
		{
			SetFiltersBackground(_filterBackgroundColor);
			// Classic draws the filter set's bitmaps, the Modern styles draw glyphs, so
			// every node has to re-read its icon when the style changes.
			FiltersDb?.FiltersRoot?.RefreshIcon();
		};
	}

	public void SetFiltersBackground(string color)
	{
		// Record before dispatching: a theme change raised right behind this call has
		// to see the colour of the filter set that is loading, not the previous one.
		_filterBackgroundColor = color;
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			// Read the field rather than the captured value so the last request wins
			// even if two loads queue their updates out of order.
			string wanted = _filterBackgroundColor;
			// Filter packs can specify a light background. Dark mode must override
			// that too, and startup must use the same rule as a live theme change.
			FiltersBackground = ThemeHelper.IsModernDark ? ThemeBrushes.DarkFilterBackground
				: wanted.IsNullOrEmpty() ? FiltersBackgroundDefault : ThemeBrushes.Frozen(wanted);
		});
	}

	public void CheckShowTrustedOnlyModeShouldBeTemporaryDisabled()
	{
		lock (_lockTemporaryShowTrusted)
		{
			if (Settings.Default.ShowTrustedOnlyEnabled)
			{
				BlackAndWhite.WhiteList();
				if (BlackAndWhite.NumberOfUsersTrusted() < 500)
				{
					_isShowTrustedTemporaryDisabled = true;
					BlackAndWhite.OnTrustedListUploaded += CheckShowTrustedOnlyModeShouldBeRestored;
					RaisePropertyChanged("ShowTrustedOnlyMode");
					RaisePropertyChanged("ShowTrustedOnlyIcon");
					RaisePropertyChanged("ShowTrustedOnlyTooltip");
				}
			}
		}
	}

	public void CheckShowTrustedOnlyModeShouldBeRestored()
	{
		lock (_lockTemporaryShowTrusted)
		{
			if (_isShowTrustedTemporaryDisabled && BlackAndWhite.NumberOfUsersTrusted() >= 500)
			{
				_isShowTrustedTemporaryDisabled = false;
				RaisePropertyChanged("ShowTrustedOnlyMode");
				RaisePropertyChanged("ShowTrustedOnlyIcon");
				RaisePropertyChanged("ShowTrustedOnlyTooltip");
			}
			if (!_isShowTrustedTemporaryDisabled)
			{
				BlackAndWhite.OnTrustedListUploaded -= CheckShowTrustedOnlyModeShouldBeRestored;
			}
		}
	}

	internal List<FilterViewModel> GetCompleteFiltersList(FilterViewModel root = null)
	{
		root = root ?? FiltersDb.FiltersRoot;
		if (!root.Children.Any())
		{
			return new List<FilterViewModel>();
		}
		List<FilterViewModel> list = root.Children.ToList();
		foreach (FilterViewModel child in root.Children)
		{
			list.AddRange(GetCompleteFiltersList(child));
		}
		return list;
	}

	internal void AddNewWarningOnce(string message)
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			lock (_lockRoot)
			{
				if (!_alreadyAddedWarnings.Contains(message))
				{
					if (message.Contains("<link>"))
					{
						WarningsList.Add(new WarningTipWithLink
						{
							Text = message
						});
					}
					else
					{
						WarningsList.Add(new WarningTip
						{
							Text = message
						});
					}
					RaisePropertyChanged("WarningsList");
					_alreadyAddedWarnings.Add(message);
				}
			}
		});
	}

	internal StackPanel GenerateTabItemHeader(string sTitle, PageTypeEnum pageType)
	{
		UIElement uIElement;
		if (pageType == PageTypeEnum.Loading)
		{
			uIElement = new ProgressRing
			{
				IsActive = true,
				Width = 15.0,
				Height = 15.0,
				IsLarge = false
			};
		}
		else
		{
			uIElement = new TextBlock();
			((TextBlock)uIElement).Style = (Style)Application.Current.FindResource("FontAwesomeTabs");
			((TextBlock)uIElement).Text = pageType switch
			{
				PageTypeEnum.SpotsNoFilter => "\uf0c2", 
				PageTypeEnum.SpotsFilter => "\uf0b0", 
				PageTypeEnum.SpotsSearch => "\uf002", 
				PageTypeEnum.Downloads => "\uf0ed", 
				PageTypeEnum.SpotLoaded => "\uf005", 
				PageTypeEnum.SpotNotLoaded => "\uf123", 
				PageTypeEnum.WebPage => "\uf0ac", 
				PageTypeEnum.ReleaseNotes => "\uf0e0", 
				PageTypeEnum.ResponseSite => "\uf188", 
				PageTypeEnum.About => "\uf128", 
				PageTypeEnum.AddNewSpot => "\uf0ee", 
				_ => "\uf188", 
			};
		}
		TextBlock textBlock = new TextBlock
		{
			Text = sTitle + " ",
			ToolTip = sTitle,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		Binding binding = new Binding();
		binding.Source = this;
		binding.Path = new PropertyPath("TabItemTextWidth");
		binding.Mode = BindingMode.OneWay;
		binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
		Binding binding2 = binding;
		BindingOperations.SetBinding(textBlock, FrameworkElement.MaxWidthProperty, binding2);
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Children = 
			{
				uIElement,
				(UIElement)textBlock
			}
		};
	}
}
