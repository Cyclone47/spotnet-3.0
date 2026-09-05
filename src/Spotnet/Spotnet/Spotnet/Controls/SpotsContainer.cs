using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Spotnet.Mvvm.Threading;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Controls;

public abstract class SpotsContainer : UserControl, ISpotsContainer
{
	protected static readonly Logger Log = LogManager.GetCurrentClassLogger();

	protected ItemContainerGenerator Ic;

	protected ContextMenu SpotMenu;

	protected SpotsListViewModel SpotsListViewModel;

	private readonly bool _delaySpotsLoadForTheFirstTime;

	private SolidColorBrush _brushForNewSpot;

	private SolidColorBrush _brushForOldSpot;

	private int _lockSelectionChanged;

	private int _lastIndexSelected = -1;

	protected static SpotsListViewModel SpotsListVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).SpotsList;

	public SpotRowViewModel SelectedSpot
	{
		get
		{
			try
			{
				if (Spots.SelectedItem == null)
				{
					return null;
				}
				return (SpotRowViewModel)((VirtualListItem<ISpotRow>)Spots.SelectedItem).Data;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			return null;
		}
	}

	public abstract Selector Spots { get; }

	public bool IcStopWait { get; set; }

	public bool IsSpotKeyboardFocused
	{
		get
		{
			if (Spots.SelectedItem != null)
			{
				return Spots.IsKeyboardFocusWithin;
			}
			return false;
		}
	}

	protected SpotsContainer(SpotsListViewModel vm, bool delaySpotsLoadForTheFirstTime)
	{
		SpotsListViewModel = vm;
		_delaySpotsLoadForTheFirstTime = delaySpotsLoadForTheFirstTime;
		DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
		{
			_brushForNewSpot = (SolidColorBrush)FindResource("GrayBrush2");
			_brushForOldSpot = (SolidColorBrush)FindResource("GrayBrush9");
		});
	}

	public abstract void UpdateContainer();

	public abstract void RestoreFocus();

	protected void OnSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
	{
		if (Interlocked.CompareExchange(ref _lockSelectionChanged, 1, 0) != 0)
		{
			return;
		}
		try
		{
			if (_lastIndexSelected > -1 && Keyboard.Modifiers == ModifierKeys.Control && SelectedSpot == null)
			{
				Spots.SelectedIndex = _lastIndexSelected;
			}
			else
			{
				_lastIndexSelected = Spots.SelectedIndex;
			}
		}
		finally
		{
			Interlocked.Exchange(ref _lockSelectionChanged, 0);
		}
	}

	public virtual void SaveCols()
	{
	}

	public void RefreshAllItemsStyle()
	{
		foreach (VirtualListItem<ISpotRow> item in Spots.ItemsSource.Cast<VirtualListItem<ISpotRow>>())
		{
			ISpotRow data = item.Data;
			if (data != null)
			{
				data.PosterIdent = PosterIdentType.Unspecified;
				UpdateItemStyle(data);
				RefreshMainToolbar(item);
			}
		}
	}

	public void LoadContentForTheFirstTime()
	{
		if (Sys.MainWindow.SpotProvider != null && Sys.MainWindow.SpotProvider.Connected)
		{
			DispatcherHelper.CheckBeginInvokeOnUI(delegate
			{
				Spots.ItemsSource = new VirtualList<ISpotRow>(Sys.MainWindow.SpotProvider, 250);
				IcStopWait = true;
				VirtualList<ISpotRow> virtualList = (VirtualList<ISpotRow>)Spots.ItemsSource;
				virtualList.Load(0);
				if (this is SpotsThumbnailsView @object)
				{
					virtualList.CollectionChanged += @object.VirtualListOnCollectionChanged;
				}
			});
		}
		else
		{
			Log.Error("List load failed because of database is not initialized yet");
			AppHelper.Error("List load failed because of database is not initialized yet");
		}
	}

	public void UpdateItemStyle(ISpotRow row)
	{
		if (row != null)
		{
			long rowNew = Sys.MainWindow?.SpotProvider?.RowNew ?? 0;
			if (rowNew > 1 && row.Id > rowNew)
			{
				row.FontWeight = FontWeights.Bold;
				row.IsNewSpotBorderThickness = 3;
				row.IsNewSpotBorderColor = _brushForNewSpot;
			}
			else
			{
				row.FontWeight = FontWeights.Normal;
				row.IsNewSpotBorderThickness = 1;
				row.IsNewSpotBorderColor = _brushForOldSpot;
			}
			// Row text colour comes from SpotRowTextStyle, whose triggers read the
			// theme's brushes, so a cached row repaints without reloading the list.
			bool isBlacklisted = !Settings.Default.HideBlacklistedSpots && row.PosterIdent == PosterIdentType.Black;
			row.ImageOpacity = (isBlacklisted ? 0.4 : 1.0);
		}
	}

	public abstract void SaveScrollPosition();

	public abstract void RestoreScrollPosition();

	protected override void OnInitialized(EventArgs e)
	{
		base.OnInitialized(e);
		Ic = Spots.ItemContainerGenerator;
		Ic.StatusChanged += IC_StatusChanged;
		if (!_delaySpotsLoadForTheFirstTime)
		{
			LoadContentForTheFirstTime();
		}
	}

	private void IC_StatusChanged(object sender, EventArgs e)
	{
		if (IcStopWait && Ic.Status == GeneratorStatus.ContainersGenerated)
		{
			IcStopWait = false;
			Sys.MainWindow.StopWait();
		}
	}

	protected abstract bool ObjectIsContainerItem(DependencyObject obj);

	protected void OpenContextMenu(RoutedEventArgs e, SpotRowViewModel row = null)
	{
		e.Handled = true;
		Spots.ContextMenu = null;
		SpotRowViewModel spotRowViewModel = row ?? SelectedSpot;
		bool flag = false;
		bool flag2 = false;
		if (ObjectIsContainerItem((DependencyObject)e.OriginalSource))
		{
			flag = true;
			flag2 = true;
		}
		if (e.OriginalSource is ScrollViewer)
		{
			flag = Sys.LeftPanel.IsSearching();
		}
		if (!flag)
		{
			return;
		}
		SpotMenu = new ContextMenu
		{
			FontFamily = base.FontFamily,
			FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
			FontStyle = base.FontStyle,
			Resources = AppHelper.GetMenuResourceDictionary,
			Tag = spotRowViewModel
		};
		SpotMenu.PreviewMouseUp += SpotMenu_PreviewMouseUp;
		if (spotRowViewModel != null && flag2)
		{
			MenuItem menuItem = new MenuItem();
			if (spotRowViewModel.IsMySpot && spotRowViewModel.IsDeleteSafePeriodIsNotReached)
			{
				menuItem.Tag = "delete";
				menuItem.Header = Words.SpotRemove;
				menuItem.Icon = AppHelper.GetIcon("delete");
			}
			else
			{
				menuItem.Tag = "report";
				menuItem.Header = Words.ComplainToSpot;
				menuItem.Icon = AppHelper.GetIcon("warning");
			}
			SpotMenu.Items.Add(menuItem);
			SpotMenu.Items.Add(new Separator());
			bool flag3 = BlackAndWhite.WhiteList().Contains(spotRowViewModel.Modulus);
			bool flag4 = BlackAndWhite.BlackList().Contains(spotRowViewModel.Modulus);
			MenuItem menuItem2 = new MenuItem
			{
				Tag = "fav",
				IsEnabled = (!flag4 || flag3),
				Header = (flag3 ? Words.WhiteListRemoveFrom : Words.WhiteListAddTo),
				Icon = AppHelper.GetIcon("favorite")
			};
			if (menuItem2.Icon != null)
			{
				((Image)menuItem2.Icon).Opacity = (menuItem2.IsEnabled ? 1.0 : 0.5);
			}
			SpotMenu.Items.Add(menuItem2);
			MenuItem menuItem3 = new MenuItem
			{
				Tag = "black",
				IsEnabled = (!spotRowViewModel.Modulus.EqualsIgnoreCase(UserKeyHelper.GetModulus()) && (!flag3 || flag4)),
				Header = (flag4 ? Words.BlackListRemoveFrom : Words.BlackListAddTo),
				Icon = AppHelper.GetIcon("trash")
			};
			if (menuItem3.Icon != null)
			{
				((Image)menuItem3.Icon).Opacity = (menuItem3.IsEnabled ? 1.0 : 0.5);
			}
			SpotMenu.Items.Add(menuItem3);
			SpotMenu.Items.Add(new Separator());
		}
		MenuItem menuItem4 = new MenuItem
		{
			Tag = "filter",
			Header = Words.SaveSearch,
			Icon = AppHelper.GetIcon("save"),
			IsEnabled = true
		};
		if (menuItem4.Icon != null)
		{
			((Image)menuItem4.Icon).Opacity = (menuItem4.IsEnabled ? 1.0 : 0.5);
		}
		SpotMenu.Items.Add(menuItem4);
		SpotMenu.Items.Add(new Separator());
		SpotMenu.Items.Add(new SpotsListTypeMenu());
		SpotMenu.UpdateLayout();
		Spots.ContextMenu = SpotMenu;
		Spots.ContextMenu.IsOpen = true;
	}

	protected void SpotMenu_PreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (e == null || !(e.Source is MenuItem { Tag: not null } menuItem))
			{
				return;
			}
			string text = menuItem.Tag.ToStringSafely();
			if (!(((FrameworkElement)menuItem.Parent).Tag is SpotRowViewModel spotRowViewModel))
			{
				Log.Error("No tag assigned to context menu, do nothing");
				return;
			}
			switch (text)
			{
			case "report":
				Sys.MainWindow.AddComplainReportToTheSpot(spotRowViewModel);
				break;
			case "delete":
				Sys.MainWindow.DeleteArticle(SpotHelper.MakeMsg(Sys.MainWindow.SpotProvider.GetMessageId(spotRowViewModel.Id)), spotRowViewModel.Titel);
				break;
			case "fav":
				if (!BlackAndWhite.BlackList().Contains(spotRowViewModel.Modulus) && !spotRowViewModel.Modulus.IsNullOrEmpty())
				{
					if (BlackAndWhite.WhiteList().Contains(spotRowViewModel.Modulus))
					{
						BlackAndWhite.RemoveWhite(spotRowViewModel.Modulus);
					}
					else
					{
						BlackAndWhite.AddWhite(AppHelper.StripNonAlphaNumericCharacters(spotRowViewModel.Afzender), spotRowViewModel.Modulus);
					}
					RefreshAllItemsStyle();
				}
				break;
			case "black":
				if (!BlackAndWhite.WhiteList().Contains(spotRowViewModel.Modulus) && !spotRowViewModel.Modulus.IsNullOrEmpty())
				{
					if (BlackAndWhite.BlackList().Contains(spotRowViewModel.Modulus))
					{
						BlackAndWhite.RemoveBlack(spotRowViewModel.Modulus);
						AppHelper.ShowPopupMessage(Words.BlackListYouWillReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(5.0));
					}
					else
					{
						BlackAndWhite.AddBlack(AppHelper.StripNonAlphaNumericCharacters(spotRowViewModel.Afzender), spotRowViewModel.Modulus);
						AppHelper.ShowPopupMessage(Words.BlackListYouWillNotReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(5.0));
					}
					RefreshAllItemsStyle();
				}
				break;
			case "filter":
			{
				string text2 = Interaction.InputBox("", Words.AddFilterNameToolTip, Sys.MainWindow.TabSearchText());
				if (!text2.Trim().IsNullOrEmpty())
				{
					Sys.LeftPanel.SaveFilter(Sys.MainWindow.SpotProvider.RowFilter, text2);
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	protected abstract void RefreshMainToolbar(VirtualListItem<ISpotRow> item);

	protected void Spots_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			Sys.MainWindow.OpenSpot(SelectedSpot);
		}
		else if (e.Key == Key.Delete)
		{
			e.Handled = true;
		}
		else
		{
			e.Handled = false;
		}
	}

	protected void Spots_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 1)
		{
			if (!Spots.IsFocused)
			{
				Spots.Focus();
			}
			if (e.LeftButton == MouseButtonState.Pressed && Settings.Default.SpotsListType == 2 && ((FrameworkElement)e.OriginalSource).GetParent<ContentControl>() is DataGridCell && ((FrameworkElement)e.OriginalSource).GetParent<DataGridRow>().IsSelected)
			{
				Spots.SelectedIndex = -1;
				e.Handled = true;
			}
		}
	}
}
