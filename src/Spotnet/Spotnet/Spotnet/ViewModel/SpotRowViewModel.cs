using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spotnet.Mvvm;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.DAL;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.TaskSchedulers;
using Spotnet.Utilities;
using Spotnet.Views;

namespace Spotnet.ViewModel;

public class SpotRowViewModel : ViewModelBase, ISpotRow, IDisposable
{
	private static readonly Logger Log;

	private static readonly SpotProvider SpotsProvider;

	private static readonly NntpSettings HeaderSettings;

	private bool _isVisible;

	private static TaskScheduler _taskSchedulerForLoadFromNet;

	private readonly object _lockGetSpot = new object();

	private readonly object _lockLoadSpot = new object();

	private BitmapSource _bitmapSource;

	private string _description;

	private FontWeight _fontWeight;

	private double _imageOpacity;

	private SpotLoadingStatusEnum _spotLoadingStatus;

	private string _spotMessageId;

	private static readonly ConcurrentDictionary<long, SpotRowViewModel> LoadingSpots;

	private static DateTime _lastTaskTime;

	private PosterIdentType _posterIdent;

	private SolidColorBrush _isNewSpotBorderColor;

	private int _isNewSpotBorderThickness;

	private bool _isInFavorites;

	private SpotRowChild Spot { get; set; }

	public SolidColorBrush GenreColorBrush => Spots.CategoryToColor(Cat);

	public Visibility GenreColorVisibility
	{
		get
		{
			if (!Settings.Default.ColoringSpots)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public int Cat => Spot.Cat;

	public string TitelUpper => Titel.ToUpper();

	public string Afzender
	{
		get
		{
			if (!Spot.Poster.IsNullOrEmpty())
			{
				return AppHelper.StripNonAlphaNumericCharacters(Spot.Poster);
			}
			return "";
		}
	}

	public string AfzenderId
	{
		get
		{
			if (Afzender.IsNullOrEmpty())
			{
				return "";
			}
			return AppHelper.MakeUnique(Spot.Modulus);
		}
	}

	public string Datum
	{
		get
		{
			if (Spot.Stamp != 0L)
			{
				return Spot.Stamp.FromUnixTime().ToString("dd-MM-yy (HH:mm)");
			}
			return "";
		}
	}

	public string Formaat
	{
		get
		{
			if (Spot.Title.IsNullOrEmpty())
			{
				return "";
			}
			byte hCat = Conversions.ToByte(Spot.SubCat.ToStringSafely().Substring(0, 1));
			try
			{
				return AppHelper.TranslateCatShort(hCat, Conversions.ToByte(Spot.SubCat.ToStringSafely().Substring(1)));
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				return "";
			}
		}
	}

	public bool IsMySpot => UserKeyHelper.GetModulus().Equals(Modulus);

	public bool IsDeleteSafePeriodIsNotReached
	{
		get
		{
			if (Spot.Stamp == 0L)
			{
				return false;
			}
			return DateAndTime.Now.ToUnixTime() - Spot.Stamp <= 432000;
		}
	}

	public string Genre
	{
		get
		{
			object obj;
			if (!Spot.Title.IsNullOrEmpty())
			{
				obj = AppHelper.ExtCatToString(Spot.ExtCat);
				if (obj == null)
				{
					return "";
				}
			}
			else
			{
				obj = "";
			}
			return (string)obj;
		}
	}

	public long Id => Spot.ID;

	public string Leeftijd => Spot.Stamp.FromUnixTime().ToAge();

	public string Modulus => Spot.Modulus;

	public string Omvang
	{
		get
		{
			if (!Spot.Title.IsNullOrEmpty() && Spot.Filesize > 0)
			{
				return AppHelper.ConvertSize(Spot.Filesize);
			}
			return "";
		}
	}

	public string Tag
	{
		get
		{
			if (!string.IsNullOrEmpty(Spot.Title))
			{
				return AppHelper.StripNonAlphaNumericCharacters(Spot.Tag);
			}
			return "";
		}
	}

	public string Titel
	{
		get
		{
			if (Spot.Title.IsNullOrEmpty())
			{
				return "";
			}
			if (!Spot.Title.Contains("&"))
			{
				return Spot.Title;
			}
			return WebUtility.HtmlDecode(Spot.Title);
		}
	}

	public int NumberOfSpamReports => Spot.NumberOfSpamReports;

	public Visibility VisibilityOfSpamCell
	{
		get
		{
			if (NumberOfSpamReports != 0)
			{
				return Visibility.Visible;
			}
			return Visibility.Collapsed;
		}
	}

	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		set
		{
			if (_isVisible != value)
			{
				_isVisible = value;
				RaisePropertyChanged("IsVisible");
			}
		}
	}

	public bool IsInFavorites
	{
		get
		{
			return _isInFavorites;
		}
		set
		{
			_isInFavorites = value;
			this.IsInFavChanged?.Invoke();
		}
	}

	public string SpotMessageId => _spotMessageId ?? (_spotMessageId = SpotsProvider.GetMessageId(Id));

	public SpotLoadingStatusEnum SpotLoadingStatus
	{
		get
		{
			return _spotLoadingStatus;
		}
		set
		{
			if (_spotLoadingStatus != value)
			{
				_spotLoadingStatus = value;
				RaisePropertyChanged("SpotLoadingStatus");
			}
		}
	}

	public string ErrorOnGettingSpot { get; set; }

	public bool IsAnimatedAlready { get; set; }

	public int IsNewSpotBorderThickness
	{
		get
		{
			return _isNewSpotBorderThickness;
		}
		set
		{
			if (_isNewSpotBorderThickness != value)
			{
				_isNewSpotBorderThickness = value;
				RaisePropertyChanged("IsNewSpotBorderThickness");
			}
		}
	}

	public long Stamp => Spot.Stamp;

	public SolidColorBrush IsNewSpotBorderColor
	{
		get
		{
			return _isNewSpotBorderColor;
		}
		set
		{
			if (_isNewSpotBorderColor == null || !_isNewSpotBorderColor.Equals(value))
			{
				_isNewSpotBorderColor = value;
				RaisePropertyChanged("IsNewSpotBorderColor");
			}
		}
	}

	public static int ThumbGridWidth => 144;

	public static int ThumbGridHeight => 211;

	public static int ThumbMaxWidth => 143;

	public static int ThumbMaxHeight => 210;

	public FontWeight FontWeight
	{
		get
		{
			return _fontWeight;
		}
		set
		{
			if (!(_fontWeight == value))
			{
				_fontWeight = value;
				RaisePropertyChanged("FontWeight");
			}
		}
	}

	public double ImageOpacity
	{
		get
		{
			return _imageOpacity;
		}
		set
		{
			if (!(Math.Abs(_imageOpacity - value) < 0.01))
			{
				_imageOpacity = value;
				RaisePropertyChanged("ImageOpacity");
			}
		}
	}

	public BitmapSource BitmapSource
	{
		get
		{
			return _bitmapSource;
		}
		set
		{
			_bitmapSource = value;
			RaisePropertyChanged("BitmapSource");
		}
	}

	public string Description
	{
		get
		{
			return _description;
		}
		set
		{
			if (!(_description == value))
			{
				_description = value;
				RaisePropertyChanged("Description");
			}
		}
	}

	public PosterIdentType PosterIdent
	{
		get
		{
			if (_posterIdent == PosterIdentType.Unspecified)
			{
				if (!Modulus.IsNullOrEmpty() && !Afzender.IsNullOrEmpty() && !Modulus.Equals("none"))
				{
					if (BlackAndWhite.BlackList().Contains(Modulus))
					{
						PosterIdent = PosterIdentType.Black;
					}
					else if (BlackAndWhite.SpotBlackList().Contains(SpotMessageId))
					{
						PosterIdent = PosterIdentType.SpotBlack;
					}
					else if (BlackAndWhite.WhiteList().Contains(Modulus))
					{
						PosterIdent = PosterIdentType.White;
					}
					else if (Spot.Stamp > 0 && AppHelper.Epoch.AddSeconds(Spot.Stamp) < DateTime.Parse("2013-01-01 00:00:00Z"))
					{
						PosterIdent = PosterIdentType.Verified;
					}
					else if (BlackAndWhite.IsModulusInServerWhitelist(Modulus))
					{
						PosterIdent = PosterIdentType.Verified;
					}
					else if (BlackAndWhite.SpotWhiteList().Contains(SpotMessageId))
					{
						PosterIdent = PosterIdentType.SpotWhite;
					}
					else if (BlackAndWhite.IsUsernameInServerWhitelist(Afzender))
					{
						PosterIdent = PosterIdentType.Fake;
					}
					else
					{
						PosterIdent = PosterIdentType.None;
					}
				}
				else if (Spot.Stamp > 0 && AppHelper.Epoch.AddSeconds(Spot.Stamp) < DateTime.Parse("2013-01-01 00:00:00Z"))
				{
					PosterIdent = PosterIdentType.Verified;
				}
				else
				{
					PosterIdent = PosterIdentType.None;
				}
			}
			return _posterIdent;
		}
		set
		{
			if (_posterIdent != value)
			{
				_posterIdent = value;
				if (value != 0)
				{
					RaisePropertyChanged("PosterIdent");
					RaisePropertyChanged("PosterIdentBorderBrush");
					RaisePropertyChanged("PosterIdentBorderBackground");
					RaisePropertyChanged("PosterIdentForeground");
					RaisePropertyChanged("PosterIdentVisibility");
					RaisePropertyChanged("PosterIdentLetter");
					RaisePropertyChanged("PosterIdentTooltip");
				}
			}
		}
	}

	public Brush PosterIdentBorderBrush => _posterIdent switch
	{
		PosterIdentType.Black => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 220, 220, 220)), 
		PosterIdentType.Fake => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 180, 180, 180)), 
		PosterIdentType.White => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 140, 190, 140)), 
		PosterIdentType.Verified => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 140, 190, 140)), 
		PosterIdentType.SpotBlack => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 220, 220, 220)), 
		PosterIdentType.SpotWhite => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 140, 190, 140)), 
		_ => null, 
	};

	public Brush PosterIdentBorderBackground => _posterIdent switch
	{
		PosterIdentType.Black => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 240, 240, 240)), 
		PosterIdentType.Fake => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 210, 210, 210)), 
		PosterIdentType.White => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 170, 220, 170)), 
		PosterIdentType.Verified => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 170, 220, 170)), 
		PosterIdentType.SpotBlack => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 240, 240, 240)), 
		PosterIdentType.SpotWhite => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 170, 220, 170)), 
		_ => null, 
	};

	public Brush PosterIdentForeground => _posterIdent switch
	{
		PosterIdentType.Black => new SolidColorBrush(Colors.DarkGray)
		{
			Opacity = 0.4
		}, 
		PosterIdentType.Fake => new SolidColorBrush(Color.FromArgb(byte.MaxValue, 170, 70, 70)), 
		PosterIdentType.White => new SolidColorBrush(Colors.White)
		{
			Opacity = 1.0
		}, 
		PosterIdentType.Verified => new SolidColorBrush(Colors.White)
		{
			Opacity = 1.0
		}, 
		PosterIdentType.SpotBlack => new SolidColorBrush(Colors.DarkGray)
		{
			Opacity = 0.4
		}, 
		PosterIdentType.SpotWhite => new SolidColorBrush(Colors.White)
		{
			Opacity = 1.0
		}, 
		_ => null, 
	};

	public Visibility PosterIdentVisibility
	{
		get
		{
			if (_posterIdent > PosterIdentType.None)
			{
				return Visibility.Visible;
			}
			return Visibility.Collapsed;
		}
	}

	public string PosterIdentLetter => _posterIdent switch
	{
		PosterIdentType.Black => Words.PosterIdentBlackLetter, 
		PosterIdentType.White => Words.PosterIdentWhiteLetter, 
		PosterIdentType.Verified => Words.PosterIdentTrustedLetter, 
		PosterIdentType.Fake => Words.PosterIdentUntrustedLetter, 
		PosterIdentType.SpotBlack => Words.PosterIdentBlackLetter, 
		PosterIdentType.SpotWhite => Words.PosterIdentTrustedLetter, 
		_ => null, 
	};

	public string PosterIdentTooltip => _posterIdent switch
	{
		PosterIdentType.Black => Words.PosterIdentBlack, 
		PosterIdentType.White => Words.PosterIdentWhite, 
		PosterIdentType.Verified => Words.PosterIdentTrusted, 
		PosterIdentType.Fake => Words.PosterIdentUntrusted, 
		PosterIdentType.SpotBlack => Words.PosterIdentSpotBlack, 
		PosterIdentType.SpotWhite => Words.PosterIdentSpotWhite, 
		_ => null, 
	};

	public event Action IsInFavChanged;

	static SpotRowViewModel()
	{
		Log = LogManager.GetCurrentClassLogger();
		LoadingSpots = new ConcurrentDictionary<long, SpotRowViewModel>();
		_lastTaskTime = DateTime.Now;
		SpotsProvider = Sys.MainWindow.SpotProvider;
		HeaderSettings = AppHelper.HeaderSettings(bIncludePosition: false);
	}

	public SpotRowViewModel()
	{
		MainWindow.ColoringForSpotsChanged += MainWindowOnColoringForSpotsChanged;
	}

	private SpotRowViewModel(SpotRowChild spot)
		: this()
	{
		Spot = spot;
		_spotMessageId = spot.MessageId;
		_description = Words.PleaseWaitWhilePageIsLoading;
		_imageOpacity = 1.0;
		IsInFavorites = (spot.Cats.IsNullOrEmpty() ? Favorites.ContainsMessageId(SpotMessageId) : Favorites.ContainsInCats(spot.Cats));
	}

	private SpotRowViewModel(Spot spot)
		: this()
	{
		Spot = new SpotRowChild
		{
			ID = spot.Article,
			Title = spot.Title,
			Modulus = spot.Modulus
		};
		_spotMessageId = spot.MessageId;
		IsInFavorites = Favorites.ContainsMessageId(SpotMessageId);
	}

	public void Dispose()
	{
		MainWindow.ColoringForSpotsChanged -= MainWindowOnColoringForSpotsChanged;
		BitmapSource = null;
	}

	private void MainWindowOnColoringForSpotsChanged()
	{
		RaisePropertyChanged("GenreColorVisibility");
	}

	public static SpotRowViewModel InitializeNewSpotRow(SpotRowChild spot)
	{
		if (LoadingSpots.TryGetValue(spot.ID, out var value))
		{
			return value;
		}
		return new SpotRowViewModel(spot);
	}

	public static SpotRowViewModel InitializeNewSpotRow(Spot spot)
	{
		return new SpotRowViewModel(spot);
	}

	internal SpotEx LoadSpotInfo()
	{
		string errorMsg = "";
		SpotEx spotOut = null;
		if (!Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, Id, SpotMessageId, ref spotOut, HeaderSettings, ref errorMsg))
		{
			ErrorOnGettingSpot = errorMsg;
		}
		return spotOut;
	}

	public void LoadSpotAsync(SpotsListTypeEnum listType)
	{
		if (SpotMessageId.IsNullOrEmpty() || (SpotLoadingStatus != 0 && SpotLoadingStatus != SpotLoadingStatusEnum.ThumbnailLoadFailed) || Monitor.IsEntered(_lockLoadSpot) || !Monitor.TryEnter(_lockGetSpot))
		{
			return;
		}
		try
		{
			SpotLoadingStatus = SpotLoadingStatusEnum.None;
			ErrorOnGettingSpot = null;
			SpotEx spotEx = FileCacheManager.Get(SpotMessageId);
			if (spotEx != null && !spotEx.ImageSource.IsNullOrEmpty() && (listType == SpotsListTypeEnum.Thumbs || !spotEx.Body.IsNullOrEmpty()))
			{
				LoadInfoFromSpot(spotEx);
			}
			Task.Factory.StartNew(delegate
			{
				LoadSpotFromTheNet(listType);
			}, CancellationToken.None, TaskCreationOptions.None, GetTaskSchedulerForLoadFromNet());
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailLoadFailed;
		}
		finally
		{
			Monitor.Exit(_lockGetSpot);
		}
	}

	public static void DisposeTaskScheduler()
	{
		if (_taskSchedulerForLoadFromNet is QueuedTaskScheduler queuedTaskScheduler)
		{
			queuedTaskScheduler.Dispose();
		}
	}

	public static TaskScheduler GetTaskSchedulerForLoadFromNet()
	{
		return LazyInitializer.EnsureInitialized(ref _taskSchedulerForLoadFromNet, () => new QueuedTaskScheduler(AppHelper.ServersDb.OHeader.Connections, "", useForegroundThreads: false, ThreadPriority.Lowest));
	}

	private void LoadSpotFromTheNet(SpotsListTypeEnum listType)
	{
		if (!Monitor.TryEnter(_lockLoadSpot))
		{
			return;
		}
		try
		{
			if (SpotLoadingStatus != 0)
			{
				return;
			}
			SpotLoadingStatus = SpotLoadingStatusEnum.Loading;
			LoadingSpots.TryAdd(Id, this);
			SpotEx spotEx = FileCacheManager.Get(SpotMessageId);
			if (listType != SpotsListTypeEnum.Thumbs)
			{
				LoadSpotDescription(ref spotEx);
				if (spotEx == null || spotEx.Body.IsNullOrEmpty())
				{
					if (ErrorOnGettingSpot.Equals("Removed"))
					{
						Log.Debug("Failed to get spot info: " + Id + " message: " + ErrorOnGettingSpot);
					}
					else
					{
						Log.Warn("Failed to get spot info: " + Id + " error: " + ErrorOnGettingSpot);
					}
					SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailLoadFailed;
					return;
				}
			}
			LoadSpotThumb(ref spotEx);
			if (spotEx == null || spotEx.ImageSource.IsNullOrEmpty())
			{
				Log.Warn("Failed to get image. ArticleId: " + Id);
				SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailImageLoadFailed;
			}
			else
			{
				BitmapSource = ImageHelper.BytesToBitmapImage(spotEx.ImageSource);
				SpotLoadingStatus = SpotLoadingStatusEnum.Loaded;
				spotEx.SaveToCache();
			}
		}
		catch (Exception ex)
		{
			if (ex.Message.EndsWith("Removed"))
			{
				Log.Debug(ex.Message);
			}
			else
			{
				Log.Exception(ex);
			}
			ErrorOnGettingSpot = ex.Message;
			if (SpotLoadingStatus == SpotLoadingStatusEnum.Loading)
			{
				SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailLoadFailed;
			}
			if (SpotLoadingStatus == SpotLoadingStatusEnum.DescriptionLoaded)
			{
				SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailImageLoadFailed;
			}
		}
		finally
		{
			LoadingSpots.TryRemove(Id, out var _);
			Monitor.Exit(_lockLoadSpot);
		}
	}

	private void LoadSpotDescription(ref SpotEx spotEx)
	{
		if (spotEx == null || spotEx.Body.IsNullOrEmpty())
		{
			byte[] imageSource = ((spotEx != null && !spotEx.ImageSource.IsNullOrEmpty()) ? spotEx.ImageSource : null);
			spotEx = LoadSpotInfo();
			if (spotEx == null || spotEx.Body.IsNullOrEmpty())
			{
				return;
			}
			spotEx.ImageSource = imageSource;
		}
		UpdateDescription(spotEx);
		SpotLoadingStatus = SpotLoadingStatusEnum.DescriptionLoaded;
	}

	private void LoadSpotThumb(ref SpotEx spotEx)
	{
		if (spotEx == null)
		{
			spotEx = new SpotEx
			{
				MessageId = SpotMessageId
			};
		}
		if (!spotEx.ImageSource.IsNullOrEmpty())
		{
			return;
		}
		spotEx.ImageSource = ImageHelper.LoadSpotThumb(spotEx);
		if (!spotEx.ImageSource.IsNullOrEmpty())
		{
			return;
		}
		if (spotEx.Body.IsNullOrEmpty())
		{
			LoadSpotDescription(ref spotEx);
			if (spotEx == null || spotEx.Body.IsNullOrEmpty())
			{
				Log.Debug("Failed to get spot info: " + Id + " message: " + ErrorOnGettingSpot);
				return;
			}
		}
		byte[] array = ImageHelper.LoadSpotFullImage(spotEx);
		try
		{
			spotEx.ImageSource = ImageHelper.ImageResize(array, 143, 210);
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			spotEx.ImageSource = array;
		}
	}

	private void UpdateDescription(SpotEx spotEx)
	{
		try
		{
			if (!spotEx.Body.IsNullOrEmpty())
			{
				Description = SpotParser.GetSpotDescriptionAsText(spotEx);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			Description = "Failed to parse spot description: " + ex.Message;
		}
	}

	private void LoadInfoFromSpot(SpotEx spotEx)
	{
		if (!Monitor.TryEnter(_lockGetSpot))
		{
			return;
		}
		try
		{
			if (SpotLoadingStatus == SpotLoadingStatusEnum.None && spotEx != null)
			{
				UpdateDescription(spotEx);
				BitmapSource = ImageHelper.BytesToBitmapImage(spotEx.ImageSource);
				SpotLoadingStatus = SpotLoadingStatusEnum.Loaded;
				double totalMilliseconds = (DateTime.Now - _lastTaskTime).TotalMilliseconds;
				if (50.0 - totalMilliseconds > 10.0)
				{
					Thread.Sleep(TimeSpan.FromMilliseconds(50.0 - totalMilliseconds));
				}
				_lastTaskTime = DateTime.Now;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			ErrorOnGettingSpot = ex.Message;
			SpotLoadingStatus = SpotLoadingStatusEnum.ThumbnailLoadFailed;
		}
		finally
		{
			Monitor.Exit(_lockGetSpot);
		}
	}
}
