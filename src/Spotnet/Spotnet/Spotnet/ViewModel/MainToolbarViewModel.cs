using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Spotnet.Mvvm;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.ViewModel;

public sealed class MainToolbarViewModel : ViewModelBase, IDisposable
{
	private const double OpacityForDisabled = 0.3;

	private const double OpacityForEnabled = 1.0;

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private double _opacityDownloadNzb = 1.0;

	private Image _image;

	public SpotRowViewModel Row { get; private set; }

	private static VisibilityViewModel VisibilityVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).Visibility;

	public Visibility VisibilityMainToolbar
	{
		get
		{
			if (!VisibilityVm.IsVisibleMainToolbar)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public string FavIcon
	{
		get
		{
			if (Row == null || !Row.IsInFavorites)
			{
				return "..\\Resources\\ImagesInternal\\fav_add.png";
			}
			return "..\\Resources\\ImagesInternal\\fav_del.png";
		}
	}

	public string FavIcon2
	{
		get
		{
			if (Row == null || !Row.IsInFavorites)
			{
				return "..\\Resources\\ImagesInternal\\fav_add2.png";
			}
			return "..\\Resources\\ImagesInternal\\fav_del2.png";
		}
	}

	public double OpacityFavorites => 1.0;

	public Visibility VisibilityPlay => Visibility.Collapsed;

	public double OpacityWhiteList
	{
		get
		{
			if (!IsWhitelistAbilityEnabled())
			{
				return 0.3;
			}
			return 1.0;
		}
	}

	public double OpacityBlackList
	{
		get
		{
			if (!IsBlacklistAbilityEnabled())
			{
				return 0.3;
			}
			return 1.0;
		}
	}

	public double OpacityDownloadNzb
	{
		get
		{
			return _opacityDownloadNzb;
		}
		private set
		{
			if (!(Math.Abs(_opacityDownloadNzb - value) < 0.01))
			{
				_opacityDownloadNzb = value;
				RaisePropertyChanged("OpacityDownloadNzb");
				RaisePropertyChanged("TooltipDownloadNzb");
				RaisePropertyChanged("CursorDownloadNzb");
			}
		}
	}

	public double OpacityCopyTitle => 1.0;

	public double OpacityCopyMessageId => 1.0;

	public double OpacityCopyImage
	{
		get
		{
			if (_image == null)
			{
				return 0.3;
			}
			return 1.0;
		}
	}

	public double IconSize
	{
		get
		{
			double num = (double)Application.Current.Resources["NormalFontSize"];
			if (!(num > 14.0))
			{
				return num + 2.0;
			}
			return 14.0;
		}
	}

	public double PanelHeight
	{
		get
		{
			double num = (double)Application.Current.Resources["NormalFontSize"];
			if (!(num < 12.0))
			{
				return num + 5.0;
			}
			return num + 3.0;
		}
	}

	public string TooltipFavorites
	{
		get
		{
			if (Row == null || !Row.IsInFavorites)
			{
				return Words.FavoritesAddTo;
			}
			return Words.FavoritesRemoveFrom;
		}
	}

	public string TooltipWhiteList
	{
		get
		{
			if (Row == null || Row.Modulus.IsNullOrEmpty())
			{
				return null;
			}
			bool flag = BlackAndWhite.WhiteList().Contains(Row.Modulus);
			if (!BlackAndWhite.BlackList().Contains(Row.Modulus) || flag)
			{
				if (!flag)
				{
					return Words.WhiteListAddTo;
				}
				return Words.WhiteListRemoveFrom;
			}
			return null;
		}
	}

	public string TooltipBlackList
	{
		get
		{
			if (Row == null || Row.Modulus.IsNullOrEmpty())
			{
				return null;
			}
			bool num = BlackAndWhite.WhiteList().Contains(Row.Modulus);
			bool flag = BlackAndWhite.BlackList().Contains(Row.Modulus);
			if (!num && !IsMyOwnRow)
			{
				if (!flag)
				{
					return Words.BlackListAddTo;
				}
				return Words.BlackListRemoveFrom;
			}
			return null;
		}
	}

	public bool IsMyOwnRow { get; private set; }

	public string TooltipDownloadNzb
	{
		get
		{
			if (OpacityDownloadNzb < 1.0)
			{
				if (Row != null && SpotHelper.IsStampOutOfEuroRetention(Row.Stamp))
				{
					return Words.SpotIsOutOfRetention;
				}
				return null;
			}
			return Words.Download;
		}
	}

	public string TooltipCopyTitle
	{
		get
		{
			if (!(OpacityCopyTitle < 1.0))
			{
				return Words.CopyTitle;
			}
			return null;
		}
	}

	public string TooltipCopyMessageId
	{
		get
		{
			if (!(OpacityCopyMessageId < 1.0))
			{
				return Words.CopySpotLink;
			}
			return null;
		}
	}

	public string TooltipCopyImage
	{
		get
		{
			if (!(OpacityCopyImage < 1.0))
			{
				return Words.CopyImage;
			}
			return null;
		}
	}

	public string TooltipPlay => Words.Play;

	public Cursor CursorFavorites => Cursors.Hand;

	public Cursor CursorWhiteList
	{
		get
		{
			if (!(OpacityWhiteList < 1.0))
			{
				return Cursors.Hand;
			}
			return null;
		}
	}

	public Cursor CursorBlackList
	{
		get
		{
			if (!(OpacityBlackList < 1.0))
			{
				return Cursors.Hand;
			}
			return null;
		}
	}

	public Cursor CursorPlay => Cursors.Hand;

	public Cursor CursorDownloadNzb
	{
		get
		{
			if (!(OpacityDownloadNzb < 1.0))
			{
				return Cursors.Hand;
			}
			return null;
		}
	}

	public Cursor CursorCopyTitle => Cursors.Hand;

	public Cursor CursorCopyMessageId => Cursors.Hand;

	public Cursor CursorCopyImage
	{
		get
		{
			if (!(OpacityCopyImage < 1.0))
			{
				return Cursors.Hand;
			}
			return null;
		}
	}

	public Image Image
	{
		get
		{
			return _image;
		}
		set
		{
			if (_image != value)
			{
				_image = value;
				RaisePropertyChanged("Image");
				RaisePropertyChanged("OpacityCopyImage");
				RaisePropertyChanged("TooltipCopyImage");
				RaisePropertyChanged("CursorCopyImage");
			}
		}
	}

	public MainToolbarViewModel()
	{
		VisibilityViewModel visibilityVm = VisibilityVm;
		visibilityVm.FontSizeChanged = (Action)Delegate.Combine(visibilityVm.FontSizeChanged, (Action)delegate
		{
			RaisePropertyChanged("IconSize");
			RaisePropertyChanged("PanelHeight");
		});
	}

	public void InitializeWithRow(SpotRowViewModel row)
	{
		if (Row != row)
		{
			Row = row;
			IsMyOwnRow = row != null && !row.Modulus.IsNullOrEmpty() && Row.Modulus.EqualsIgnoreCase(UserKeyHelper.GetModulus());
			RaisePropertiesChanged();
			if (Settings.Default.DownloadAction <= 1)
			{
				OpacityDownloadNzb = ((row != null && SpotHelper.IsStampOutOfEuroRetention(row.Stamp)) ? 0.3 : 1.0);
			}
			Row.IsInFavChanged += delegate
			{
				RaisePropertyChanged("FavIcon");
				RaisePropertyChanged("FavIcon2");
				RaisePropertyChanged("OpacityFavorites");
				RaisePropertyChanged("TooltipFavorites");
				RaisePropertyChanged("CursorFavorites");
			};
		}
	}

	public void RaisePropertiesChanged()
	{
		RaisePropertyChanged("OpacityWhiteList");
		RaisePropertyChanged("OpacityBlackList");
		RaisePropertyChanged("TooltipWhiteList");
		RaisePropertyChanged("TooltipBlackList");
		RaisePropertyChanged("CursorWhiteList");
		RaisePropertyChanged("CursorBlackList");
		RaisePropertyChanged("FavIcon");
		RaisePropertyChanged("FavIcon2");
		RaisePropertyChanged("OpacityFavorites");
		RaisePropertyChanged("TooltipFavorites");
		RaisePropertyChanged("CursorFavorites");
		RaisePropertyChanged("VisibilityPlay");
	}

	private void DisableDownloadNzb()
	{
		OpacityDownloadNzb = 0.3;
	}

	private void EnableDownloadNzb()
	{
		OpacityDownloadNzb = 1.0;
	}

	internal bool IsWhitelistAbilityEnabled()
	{
		if (Row == null || Row.Modulus.IsNullOrEmpty())
		{
			return false;
		}
		bool flag = BlackAndWhite.WhiteList().Contains(Row.Modulus);
		return !BlackAndWhite.BlackList().Contains(Row.Modulus) || flag;
	}

	internal bool IsBlacklistAbilityEnabled()
	{
		if (Row == null || Row.Modulus.IsNullOrEmpty())
		{
			return false;
		}
		bool num = BlackAndWhite.WhiteList().Contains(Row.Modulus);
		bool flag = BlackAndWhite.BlackList().Contains(Row.Modulus);
		if (!num || flag)
		{
			return !IsMyOwnRow;
		}
		return false;
	}

	public void ScheduleDownloadAsync(bool showTooltip = true)
	{
		if (OpacityDownloadNzb < 1.0)
		{
			return;
		}
		Task.Factory.StartNew(delegate
		{
			try
			{
				DisableDownloadNzb();
				if (showTooltip && Settings.Default.DownloadAction <= 1 && !Row.Titel.IsNullOrEmpty())
				{
					AppHelper.ShowPopupMessage(Words.DownloadScheduled + "\r\n" + Row.Titel, inTheCenter: false, TimeSpan.FromSeconds(5.0));
				}
				string errorMsg;
				SpotEx spotEx = SpotRowVmToSpotEx(out errorMsg);
				if (spotEx == null)
				{
					if (!Sys.IsShutdownRequested)
					{
						string text = "Error on getting spot: " + errorMsg;
						Log.Error(text + ". MessageId: " + Row.SpotMessageId + ". Group: " + Settings.Default.HeaderGroup);
						AppHelper.Error(text);
					}
				}
				else
				{
					SpotHelper.DownloadNzbAndStartDownloadItem(spotEx);
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			finally
			{
				EnableDownloadNzb();
			}
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	public void SchedulePlayAsync()
	{
		Task.Run(delegate
		{
			DownloaderItemViewModel item = null;
			DateTime now = DateTime.Now;
			while (DateTime.Now - now < TimeSpan.FromSeconds(20.0) && !Sys.Downloader.IsDownloadInQueueAlready(Row.SpotMessageId, out item))
			{
				Thread.Sleep(100);
			}
			item?.SchedulePlayOrPause();
		});
	}

	private SpotEx SpotRowVmToSpotEx(out string errorMsg)
	{
		SpotEx spotOut = null;
		errorMsg = null;
		try
		{
			SpotEx spotEx = FileCacheManager.Get(Row.SpotMessageId);
			if (spotEx != null && !spotEx.Body.IsNullOrEmpty())
			{
				spotOut = spotEx;
			}
			else
			{
				Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, Row.Id, Row.SpotMessageId, ref spotOut, AppHelper.HeaderSettings(bIncludePosition: false), ref errorMsg);
			}
		}
		catch (Exception ex)
		{
			errorMsg = ex.Message;
		}
		return spotOut;
	}

	public void Dispose()
	{
		_image.Dispose();
	}
}
