using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spotnet.Mvvm;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Controls;
using Spotnet.DAL;
using Spotnet.DataVirtualization;
using Spotnet.Helpers;
using Spotnet.Properties;
using Spotnet.TaskSchedulers;

namespace Spotnet.ViewModel;

public class SpotsListViewModel : ViewModelBase
{
	private static readonly Logger Log;

	private static readonly Brush IconColorDefault;

	private static readonly Brush IconColorForActive;

	private static readonly Brush IconColorForActiveAndSelected;

	private static readonly Brush IconColorForSelected;

	private Brush _view1IconColor;

	private Brush _view2IconColor;

	private Brush _view3IconColor;

	private SpotsListTypeEnum _activeIcon;

	private SpotsListTypeEnum _selectedIcon;

	private bool _isSpotsDbUpToDate = true;

	private Visibility _spotsDbIsNotUpToDateWarningVisibility = Visibility.Collapsed;

	private Visibility _newSpotsBarVisibility = Visibility.Collapsed;

	private int _numberOfNewSpots;

	private bool _isCommentsDbUpToDate = true;

	private bool _isSpotsListLoading;

	private object _selectedItem;

	public ISpotsContainer SpotsContainer { get; private set; }

	public Brush View1IconColor
	{
		get
		{
			return _view1IconColor;
		}
		private set
		{
			if (!value.Equals(_view1IconColor))
			{
				_view1IconColor = value;
				RaisePropertyChanged("View1IconColor");
			}
		}
	}

	public Brush View2IconColor
	{
		get
		{
			return _view2IconColor;
		}
		private set
		{
			if (!value.Equals(_view2IconColor))
			{
				_view2IconColor = value;
				RaisePropertyChanged("View2IconColor");
			}
		}
	}

	public Brush View3IconColor
	{
		get
		{
			return _view3IconColor;
		}
		private set
		{
			if (!value.Equals(_view3IconColor))
			{
				_view3IconColor = value;
				RaisePropertyChanged("View3IconColor");
			}
		}
	}

	public bool SpotsListNoDetails
	{
		get
		{
			SpotsListTypeEnum spotsListType = (SpotsListTypeEnum)Settings.Default.SpotsListType;
			if (spotsListType != 0)
			{
				return spotsListType == SpotsListTypeEnum.NoDetails;
			}
			return true;
		}
	}

	public bool SpotsListWithDetails => Settings.Default.SpotsListType == 2;

	public bool SpotsListPicsOnly => Settings.Default.SpotsListType == 3;

	public DataGridRowDetailsVisibilityMode SpotDetailsVisibilityMode
	{
		get
		{
			if (!SpotsListWithDetails)
			{
				return DataGridRowDetailsVisibilityMode.Collapsed;
			}
			return DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
		}
	}

	public Thickness ThumbsContainerPadding
	{
		get
		{
			int num = 2;
			if (SpotsListPicsOnly)
			{
				double actualWidth = SpotsContainer.Spots.ActualWidth;
				num = Math.Max(1, (int)(actualWidth % 159.0 / 2.0)) + 1;
			}
			return new Thickness(num, 0.0, 1.0, 0.0);
		}
	}

	public bool IsSpotsDbUpToDate
	{
		get
		{
			return _isSpotsDbUpToDate;
		}
		set
		{
			SpotsDbIsNotUpToDateWarningVisibility = (value ? Visibility.Collapsed : Visibility.Visible);
			if (_isSpotsDbUpToDate != value)
			{
				_isSpotsDbUpToDate = value;
				RaisePropertyChanged("IsSpotsDbUpToDate");
			}
		}
	}

	public Visibility SpotsDbIsNotUpToDateWarningVisibility
	{
		get
		{
			return _spotsDbIsNotUpToDateWarningVisibility;
		}
		set
		{
			if (_spotsDbIsNotUpToDateWarningVisibility != value)
			{
				_spotsDbIsNotUpToDateWarningVisibility = value;
				RaisePropertyChanged("SpotsDbIsNotUpToDateWarningVisibility");
			}
		}
	}

	public Visibility NewSpotsBarVisibility
	{
		get
		{
			return _newSpotsBarVisibility;
		}
		set
		{
			if (_newSpotsBarVisibility != value)
			{
				_newSpotsBarVisibility = value;
				RaisePropertyChanged("NewSpotsBarVisibility");
			}
		}
	}

	public string NewSpotsBarText => string.Format(Words.NewSpotsAvailable, _numberOfNewSpots);

	public bool IsCommentsDbUpToDate
	{
		get
		{
			return _isCommentsDbUpToDate;
		}
		set
		{
			if (_isCommentsDbUpToDate != value)
			{
				_isCommentsDbUpToDate = value;
				RaisePropertyChanged("IsCommentsDbUpToDate");
			}
		}
	}

	public bool IsSpotsListLoading
	{
		get
		{
			return _isSpotsListLoading;
		}
		set
		{
			if (_isSpotsListLoading != value)
			{
				_isSpotsListLoading = value;
				RaisePropertyChanged("IsSpotsListLoading");
				RaisePropertyChanged("IsSpotsListNotLoading");
			}
		}
	}

	public bool IsSpotsListNotLoading => !_isSpotsListLoading;

	public string NoSpotsFoundText
	{
		get
		{
			if (Settings.Default.DatabaseMax <= 0)
			{
				if (!DbUpdater.IsDbUpdateInProgress)
				{
					return Words.NoSpotsInTheDbPleaseStartDbUpdate;
				}
				return Words.NoSpotsInTheDbWaitForUpdate;
			}
			return Words.NoSpotsFound;
		}
	}

	public object SelectedItem
	{
		get
		{
			return _selectedItem;
		}
		set
		{
			_selectedItem = value;
			RaisePropertyChanged("SelectedItem");
		}
	}

	public event Action SpotsListTypeChanged;

	static SpotsListViewModel()
	{
		Log = LogManager.GetCurrentClassLogger();
		IconColorDefault = Brushes.Transparent;
		IconColorForActive = (Brush)Application.Current.FindResource("HighlightBrush");
		IconColorForActiveAndSelected = (Brush)Application.Current.FindResource("HighlightBrush");
		IconColorForSelected = (Brush)Application.Current.FindResource("AccentColorBrush2");
	}

	public SpotsListViewModel()
	{
		DbUpdater.OnDbUpdateStart += delegate
		{
			RaisePropertyChanged("NoSpotsFoundText");
		};
		DbUpdater.OnDbUpdateEnd += delegate
		{
			RaisePropertyChanged("NoSpotsFoundText");
		};
		SpotSaver.OnDbSettingsUpdate += delegate
		{
			RaisePropertyChanged("NoSpotsFoundText");
		};
		SpotProvider.OnDbSettingsUpdate += delegate
		{
			RaisePropertyChanged("NoSpotsFoundText");
		};
		AppHelper.OnDbSettingsUpdate += delegate
		{
			RaisePropertyChanged("NoSpotsFoundText");
		};
	}

	private void UpdateIconsColor()
	{
		Brush brush = IconColorDefault;
		Brush brush2 = IconColorDefault;
		Brush brush3 = IconColorDefault;
		switch (_activeIcon)
		{
		case SpotsListTypeEnum.WithDetails:
			brush2 = IconColorForActive;
			break;
		case SpotsListTypeEnum.Thumbs:
			brush3 = IconColorForActive;
			break;
		default:
			brush = IconColorForActive;
			break;
		}
		if (_selectedIcon != 0)
		{
			switch (_selectedIcon)
			{
			case SpotsListTypeEnum.WithDetails:
				brush2 = (object.Equals(brush2, IconColorDefault) ? IconColorForSelected : IconColorForActiveAndSelected);
				break;
			case SpotsListTypeEnum.Thumbs:
				brush3 = (object.Equals(brush3, IconColorDefault) ? IconColorForSelected : IconColorForActiveAndSelected);
				break;
			default:
				brush = (object.Equals(brush, IconColorDefault) ? IconColorForSelected : IconColorForActiveAndSelected);
				break;
			}
		}
		View1IconColor = brush;
		View2IconColor = brush2;
		View3IconColor = brush3;
	}

	public void ChangeActiveIcon(SpotsListTypeEnum currentType)
	{
		_activeIcon = currentType;
		UpdateIconsColor();
	}

	public void ChangeSelectedIcon(SpotsListTypeEnum selectedType)
	{
		_selectedIcon = selectedType;
		UpdateIconsColor();
	}

	private void UpdateSpotsContainer(bool delaySpotsLoad = false)
	{
		if (SpotsContainer != null)
		{
			SpotsContainer.Spots.SizeChanged -= SpotsOnSizeChanged;
		}
		if (SpotsListPicsOnly)
		{
			if (!(SpotsContainer is SpotsThumbnailsView))
			{
				SpotsContainer = new SpotsThumbnailsView(this, delaySpotsLoad);
				SpotsContainer.Spots.SizeChanged += SpotsOnSizeChanged;
			}
		}
		else if (!(SpotsContainer is SpotsListWithDetailsGrid))
		{
			((ITaskSchedulerExtentions)SpotRowViewModel.GetTaskSchedulerForLoadFromNet()).CancelAllTasks();
			SpotsContainer = new SpotsListWithDetailsGrid(this, delaySpotsLoad);
		}
		RaisePropertyChanged("SpotsContainer");
	}

	private void SpotsOnSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
	{
		RaisePropertyChanged("ThumbsContainerPadding");
	}

	public void SetNewSpotsFound(int numberOfNewSpots)
	{
		if (numberOfNewSpots > 0)
		{
			_numberOfNewSpots += numberOfNewSpots;
			RaisePropertyChanged("NewSpotsBarText");
			NewSpotsBarVisibility = Visibility.Visible;
		}
	}

	public void ResetNewSpotsBar()
	{
		NewSpotsBarVisibility = Visibility.Collapsed;
		_numberOfNewSpots = 0;
	}

	internal async Task RefreshSpotsListWithNewItemsAsync(long minRowId)
	{
		if (minRowId < 0)
		{
			return;
		}
		bool isThumbsView = Settings.Default.SpotsListType == 3;
		bool haveToRestore = false;
		bool doNotProcess = false;
		await DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
		{
			if (SpotsContainer.IsSpotKeyboardFocused)
			{
				if (isThumbsView)
				{
					doNotProcess = true;
				}
				SpotsContainer.SaveScrollPosition();
				haveToRestore = true;
			}
		});
		if (doNotProcess)
		{
			return;
		}
		await ((VirtualList<ISpotRow>)SpotsContainer.Spots.ItemsSource).LoadRangeAsync(0, minRowId);
		await DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
		{
			if (haveToRestore)
			{
				SpotsContainer.RestoreScrollPosition();
			}
			if (isThumbsView)
			{
				SpotsContainer.UpdateContainer();
			}
		});
	}

	public void UpdateSpotsListType(SpotsListTypeEnum type, bool force = false, bool delaySpotsLoad = false)
	{
		if (Settings.Default.SpotsListType != (byte)type || force)
		{
			Settings.Default.SpotsListType = (byte)type;
			Settings.Default.Save();
			UpdateSpotsContainer(delaySpotsLoad);
			RaisePropertyChanged("SpotsListNoDetails");
			RaisePropertyChanged("SpotsListWithDetails");
			RaisePropertyChanged("SpotsListPicsOnly");
			RaisePropertyChanged("SpotDetailsVisibilityMode");
			SpotsContainer.RestoreFocus();
			ChangeActiveIcon(type);
			this.SpotsListTypeChanged?.Invoke();
		}
	}
}
