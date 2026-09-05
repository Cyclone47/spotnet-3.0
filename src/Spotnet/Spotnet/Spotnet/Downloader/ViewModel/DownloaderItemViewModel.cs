using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Downloader.ViewModel;

public abstract class DownloaderItemViewModel : INotifyPropertyChanged, IComparable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly object _lockRawStatusChange = new object();

	private readonly object _lockRemove = new object();

	protected string _completeDir;

	protected string _incompleteDir;

	protected int _index;

	private bool _isBlinking;

	private bool _isPlayActive;

	private bool _isPlaying;

	private bool _isPlayScheduled;

	private bool _isProgressIndeterminate;

	private bool _isToBeRemoved;

	protected string _messageId;

	protected string _pathToNzb;

	protected double _perc;

	private Player _player;

	private DownloadStatus _rawStatus;

	protected int _secondsLeft;

	protected double _sizeMegaBytes;

	protected string _speed;

	private double _sizeOfPar2MegaBytes;

	private Timer _timerUpdatePlayVisibility;

	protected string _title;

	public int ID;

	public string UnpackPassword;

	public int Category { get; protected set; }

	public string MessageId
	{
		get
		{
			return _messageId;
		}
		set
		{
			_messageId = value;
			NotifyPropertyChanged("MessageId");
		}
	}

	public int Index
	{
		get
		{
			return _index;
		}
		set
		{
			_index = value;
			NotifyPropertyChanged("Index");
		}
	}

	public string Priority
	{
		get
		{
			if (!IsHistory && !IsNzbDownload)
			{
				return Sys.Downloader.GetPriority(this).ToString();
			}
			return "";
		}
	}

	public bool IsDownloading => _rawStatus == DownloadStatus.Downloading;

	public bool IsQueued => _rawStatus == DownloadStatus.Queued;

	public bool IsNzbDownload => RawStatus == DownloadStatus.NzbDownloading;

	public bool IsTotals => RawStatus == DownloadStatus.Totals;

	public bool IsHistory
	{
		get
		{
			if (_rawStatus != DownloadStatus.Success && _rawStatus != DownloadStatus.Failure && _rawStatus != DownloadStatus.FailureNoSuchArticle && _rawStatus != DownloadStatus.Warning)
			{
				return _rawStatus == DownloadStatus.Deleted;
			}
			return true;
		}
	}

	public bool IsPostProcess
	{
		get
		{
			if (_rawStatus != DownloadStatus.Par2PieceDownloading && _rawStatus != DownloadStatus.Checking && _rawStatus != DownloadStatus.Repairing && _rawStatus != DownloadStatus.Verifying && _rawStatus != DownloadStatus.Moving && _rawStatus != DownloadStatus.Unpacking)
			{
				return _rawStatus == DownloadStatus.WrongPassword;
			}
			return true;
		}
	}

	public bool IsPaused => _rawStatus == DownloadStatus.Paused;

	public Visibility ProgressBarVisibility
	{
		get
		{
			if (IsHistory || _rawStatus == DownloadStatus.NzbDownloading)
			{
				return Visibility.Hidden;
			}
			return Visibility.Visible;
		}
	}

	public string IncompleteDir
	{
		get
		{
			return _incompleteDir;
		}
		set
		{
			if (!(_incompleteDir == AppHelper.FixDirectoryName(value)))
			{
				_incompleteDir = AppHelper.FixDirectoryName(value);
				NotifyPropertyChanged("IncompleteDir");
			}
		}
	}

	public string CompleteDir
	{
		get
		{
			return _completeDir;
		}
		set
		{
			if (_completeDir == null || !_completeDir.Equals(AppHelper.FixDirectoryName(value)))
			{
				_completeDir = AppHelper.FixDirectoryName(value);
				NotifyPropertyChanged("CompleteDir");
				NotifyPropertyChanged("PlayVisibility");
				NotifyPropertyChanged("PlayProgressRingVisibility");
			}
		}
	}

	public double SizeMegaBytes
	{
		get
		{
			return _sizeMegaBytes;
		}
		set
		{
			if (!(Math.Abs(_sizeMegaBytes - value) < 0.001))
			{
				_sizeMegaBytes = value;
				NotifyPropertyChanged("Size");
				NotifyPropertyChanged("SizeMegaBytes");
			}
		}
	}

	public double SizeOfPar2MegaBytes
	{
		get
		{
			return _sizeOfPar2MegaBytes;
		}
		set
		{
			if (!(Math.Abs(_sizeOfPar2MegaBytes - value) < 0.001))
			{
				_sizeOfPar2MegaBytes = value;
				NotifyPropertyChanged("SizeOfPar2MegaBytes");
			}
		}
	}

	public Player Player => _player ?? (_player = new Player(this));

	public Visibility PlayVisibility
	{
		get
		{
			if (!IsPlayScheduled)
			{
				_ = IsNzbDownload;
			}
			return Visibility.Collapsed;
		}
	}

	public string PlayPauseIcon
	{
		get
		{
			if (!IsPlayActive || !_isPlaying)
			{
				return "\uf04b";
			}
			return "\uf04c";
		}
	}

	public bool IsVideoAudioPrimaryGroup => new int[4] { 1, 2, 6, 9 }.Contains(Category);

	public long AddedUnixTime { get; private set; }

	public long FinishedUnixTime { get; private set; }

	public string Added
	{
		get
		{
			return AddedUnixTime.FromUnixTime().ToAge();
		}
		set
		{
			if (AddedUnixTime != Convert.ToInt64(value))
			{
				AddedUnixTime = Convert.ToInt64(value);
				NotifyPropertyChanged("Added");
			}
		}
	}

	public string Finished
	{
		get
		{
			return FinishedUnixTime.FromUnixTime().ToAge();
		}
		set
		{
			if (FinishedUnixTime != Convert.ToInt64(value))
			{
				FinishedUnixTime = Convert.ToInt64(value);
				NotifyPropertyChanged("Finished");
			}
		}
	}

	public Visibility PlayProgressRingVisibility
	{
		get
		{
			if (!IsPlayScheduled)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public bool IsPlayScheduled
	{
		get
		{
			return _isPlayScheduled;
		}
		set
		{
			if (_isPlayScheduled != value)
			{
				_isPlayScheduled = value;
				NotifyPropertyChanged("PlayVisibility");
				NotifyPropertyChanged("PlayProgressRingVisibility");
			}
		}
	}

	public string Size => AppHelper.FormatSizeMegaBytes(SizeMegaBytes);

	public int TotalArticles { get; set; }

	public int SuccessArticles { get; set; }

	public double Opacity => 1.0;

	public double Perc
	{
		get
		{
			return _perc;
		}
		set
		{
			if (!(Math.Abs(_perc - value) < 0.001))
			{
				_perc = value;
				IsProgressIndeterminate = value < 0.0;
				NotifyPropertyChanged("PercInt");
				NotifyPropertyChanged("Tooltip");
			}
		}
	}

	public bool IsProgressIndeterminate
	{
		get
		{
			return _isProgressIndeterminate;
		}
		set
		{
			if (_isProgressIndeterminate != value)
			{
				_isProgressIndeterminate = value;
				NotifyPropertyChanged("IsProgressIndeterminate");
			}
		}
	}

	public int PercInt => (int)_perc;

	public virtual string Tooltip
	{
		get
		{
			if (PercInt >= 0)
			{
				return $"{Words.ColumnProgress}: {PercInt}%.";
			}
			return Words.InProgress;
		}
	}

	public bool IsMessageIdSpecified => !MessageId.IsNullOrEmpty();

	public bool IsForDownloadQueue
	{
		get
		{
			if (!IsDownloading)
			{
				return IsQueued;
			}
			return true;
		}
	}

	public DownloadStatus RawStatus
	{
		get
		{
			return _rawStatus;
		}
		set
		{
			bool isHistory = IsHistory;
			bool isDownloading = IsDownloading;
			bool isForDownloadQueue = IsForDownloadQueue;
			lock (_lockRawStatusChange)
			{
				if (_rawStatus == value)
				{
					return;
				}
				_rawStatus = value;
			}
			if (!IsHistory && !IsVideoAudioPrimaryGroup)
			{
				_timerUpdatePlayVisibility?.Start();
			}
			this.OnStatusChanged?.Invoke(isDownloading, isHistory);
			if (IsHistory && !isHistory)
			{
				if (IsPlayActive)
				{
					Sys.DownloadsPlayer.UpdatePlaylist(this);
				}
				this.IsHistoryChanged?.Invoke(this, arg2: true);
			}
			if (isForDownloadQueue != IsForDownloadQueue || isHistory != IsHistory)
			{
				Sys.Downloader.UpdateItemsOrder();
			}
			NotifyPropertyChanged("IsHistory");
			NotifyPropertyChanged("Status");
			NotifyPropertyChanged("ProgressBarVisibility");
			NotifyPropertyChanged("Visibility");
			NotifyPropertyChanged("StatusVisibility");
			NotifyPropertyChanged("PlayVisibility");
			NotifyPropertyChanged("StatusWarningText");
			NotifyPropertyChanged("IsStatusWarningIconVisible");
			NotifyPropertyChanged("VisibilityOfStatusWarningIcon");
			NotifyPropertyChanged("StatusWithLinkVisibility");
		}
	}

	public int SecondsLeft
	{
		get
		{
			return _secondsLeft;
		}
		set
		{
			if (_secondsLeft != value)
			{
				_secondsLeft = value;
				NotifyPropertyChanged("Time");
			}
		}
	}

	public string Speed
	{
		get
		{
			if (!IsDownloading && !IsPausing && RawStatus != DownloadStatus.Par2PieceDownloading)
			{
				return "";
			}
			return _speed;
		}
		set
		{
			if (_speed == null || !_speed.Equals(value))
			{
				_speed = value;
				NotifyPropertyChanged("Speed");
			}
		}
	}

	public string Status
	{
		get
		{
			switch (_rawStatus)
			{
			case DownloadStatus.Pausing:
				return Words.Pausing;
			case DownloadStatus.Paused:
				return Words.StatPaused;
			case DownloadStatus.NzbDownloading:
				return Words.NZBDownloading;
			case DownloadStatus.Downloading:
				return Words.StatDownloading;
			case DownloadStatus.Repairing:
				return Words.StatRepairing;
			case DownloadStatus.Unpacking:
				return Words.StatExtracting;
			case DownloadStatus.Moving:
				return Words.StatMoving;
			case DownloadStatus.Queued:
				return Words.StatQueued;
			case DownloadStatus.Verifying:
				return Words.StatVerifying;
			case DownloadStatus.Checking:
				return Words.StatQuickCheck;
			case DownloadStatus.Par2PieceDownloading:
				return Words.StatPar2Downloading;
			case DownloadStatus.WrongPassword:
				return Words.StatWrongUnpackPassword;
			case DownloadStatus.Unknown:
				return Words.Unknown;
			case DownloadStatus.Success:
				return Words.StatCompleted;
			case DownloadStatus.Failure:
			case DownloadStatus.FailureNoSuchArticle:
				return Words.StatFailed;
			case DownloadStatus.Warning:
				return Words.Warning;
			case DownloadStatus.Deleted:
				return "Deleted";
			case DownloadStatus.Empty:
				return "";
			default:
				return _rawStatus.ToString();
			}
		}
	}

	public Visibility StatusWithLinkVisibility
	{
		get
		{
			if (_rawStatus == DownloadStatus.WrongPassword || (_rawStatus == DownloadStatus.FailureNoSuchArticle && !AppHelper.IsSnelNlProvider && !AppHelper.Is5EuroProvider))
			{
				return Visibility.Visible;
			}
			return Visibility.Collapsed;
		}
	}

	public virtual Visibility VisibilityOfStatusWarningIcon => Visibility.Collapsed;

	public virtual bool IsStatusWarningIconVisible => false;

	public virtual string StatusWarningText { get; set; }

	public Visibility StatusVisibility
	{
		get
		{
			if (StatusWithLinkVisibility != Visibility.Collapsed)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public string PromoLink => Configuration.DownloadsTabPromoLink;

	public string Time
	{
		get
		{
			if (IsHistory || _rawStatus == DownloadStatus.Queued || _secondsLeft == 0 || IsPaused || (IsPostProcess && RawStatus != DownloadStatus.Par2PieceDownloading))
			{
				return "";
			}
			if (_secondsLeft <= 360000)
			{
				return FormatTimeLeft(_secondsLeft);
			}
			return "";
		}
	}

	public string Titel
	{
		get
		{
			return _title;
		}
		set
		{
			_title = value;
			NotifyPropertyChanged("Titel");
		}
	}

	public bool IsBlinking
	{
		get
		{
			return _isBlinking;
		}
		set
		{
			if (_isBlinking != value)
			{
				_isBlinking = value;
				NotifyPropertyChanged("IsBlinking");
			}
		}
	}

	public bool IsPlayActive
	{
		get
		{
			return _isPlayActive;
		}
		set
		{
			if (_isPlayActive != value)
			{
				_isPlayActive = value;
				if (_isPlayActive)
				{
					Player.StartUpdatePlaylist();
				}
				else
				{
					Player.StopUpdatePlaylist();
				}
				NotifyPropertyChanged("IsPlayActive");
				NotifyPropertyChanged("PlayPauseIcon");
			}
		}
	}

	public string PathToNzb
	{
		get
		{
			return _pathToNzb;
		}
		set
		{
			if (_pathToNzb == null || !_pathToNzb.Equals(value))
			{
				_pathToNzb = value;
				this.OnPathToNzbChanged?.Invoke(value);
			}
		}
	}

	public bool CanOpen
	{
		get
		{
			if (IsHistory && !FinalDir.IsNullOrEmpty())
			{
				return Directory.Exists(FinalDir);
			}
			return false;
		}
	}

	private string FinalDir
	{
		get
		{
			if (_rawStatus != DownloadStatus.Success && _rawStatus != DownloadStatus.Warning)
			{
				return IncompleteDir;
			}
			return CompleteDir;
		}
	}

	public bool IsPausing => RawStatus == DownloadStatus.Pausing;

	public long BytesPerSecond { get; set; }

	public event PropertyChangedEventHandler PropertyChanged;

	public event Action<bool, bool> OnStatusChanged;

	public event Action<string> OnPathToNzbChanged;

	public event Action OnItemRemove;

	public event Action OnSchedulePlayOrPause;

	public event Action<DownloaderItemViewModel, bool> IsHistoryChanged;

	public int CompareTo(object obj)
	{
		if (!(obj is DownloaderItemViewModel downloaderItemViewModel))
		{
			return -1;
		}
		int num = Index.CompareTo(downloaderItemViewModel.Index);
		if (num == 0 && IsNzbDownload != downloaderItemViewModel.IsNzbDownload)
		{
			if (!IsNzbDownload)
			{
				return 1;
			}
			return -1;
		}
		return num;
	}

	protected void Initialize(int id, string title, DownloadStatus status, int perc, double sizeMegaBytes, int secondsLeft, int index, string incompleteDir, string completeDir, string speed, string messageId, int category, string pathToNzb, long added, long finished)
	{
		if (index < 0)
		{
			index = Sys.Downloader.Items.GetNewIndex(status);
		}
		ID = id;
		_perc = perc;
		_title = title;
		_sizeMegaBytes = sizeMegaBytes;
		_secondsLeft = secondsLeft;
		_index = index;
		AddedUnixTime = added;
		FinishedUnixTime = finished;
		_completeDir = AppHelper.FixDirectoryName(completeDir);
		if (_completeDir.IsNullOrEmpty())
		{
			string path = AppHelper.MakeFilename(Titel).Trim();
			string path2 = Path.Combine(DownloaderProps.DestDir, path);
			_completeDir = AppHelper.FixDirectoryName(FixDirectoryIfExist(path2));
		}
		_incompleteDir = AppHelper.FixDirectoryName(incompleteDir);
		if (_incompleteDir.IsNullOrEmpty())
		{
			string path3 = AppHelper.MakeFilename(Titel).Trim();
			string path4 = Path.Combine(DownloaderProps.InterDir, path3);
			_incompleteDir = AppHelper.FixDirectoryName(FixDirectoryIfExist(path4));
		}
		_speed = speed;
		_messageId = messageId;
		Category = category;
		RawStatus = status;
		PathToNzb = pathToNzb;
		if (!IsTotals && RawStatus != DownloadStatus.Deleted && Settings.Default.DownloadAction <= 1)
		{
			Sys.Downloader.Items.AddToTheList(this);
		}
	}

	public void Remove()
	{
		lock (_lockRemove)
		{
			if (!_isToBeRemoved)
			{
				_isToBeRemoved = true;
				RawStatus = DownloadStatus.Deleted;
				if (_timerUpdatePlayVisibility != null)
				{
					_timerUpdatePlayVisibility.Dispose();
					_timerUpdatePlayVisibility = null;
				}
				Sys.DownloadsPlayer.PlayStatusChanged -= DownloadsPlayerOnPlayStatusChanged;
				this.OnItemRemove?.Invoke();
				if (!IncompleteDir.Equals(CompleteDir))
				{
					Remover.ScheduleDirectoryRemove(IncompleteDir);
				}
				if (!IsHistory)
				{
					Remover.ScheduleDirectoryRemove(CompleteDir);
				}
			}
		}
	}

	private void DownloadsPlayerOnPlayStatusChanged(bool isPlaying)
	{
		_isPlaying = isPlaying;
		NotifyPropertyChanged("PlayPauseIcon");
	}

	private static string FormatTimeLeft(double sec)
	{
		double num = Math.Floor(sec / 3600.0);
		double num2 = Math.Floor(sec / 60.0 % 60.0);
		double num3 = Math.Floor(sec % 60.0);
		string text = Convert.ToString(num, CultureInfo.InvariantCulture);
		string text2 = Convert.ToString(num2, CultureInfo.InvariantCulture);
		string text3 = Convert.ToString(num3, CultureInfo.InvariantCulture);
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		return text + ":" + text2 + ":" + text3;
	}

	public void NotifyPropertyChanged(string info)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
	}

	internal async void Blink()
	{
		if (!IsBlinking)
		{
			IsBlinking = true;
			await Task.Delay(4000);
			IsBlinking = false;
		}
	}

	public async void SchedulePlayOrPause()
	{
		if (IsPlayActive)
		{
			Sys.DownloadsPlayer.PauseOrResume();
			return;
		}
		this.OnSchedulePlayOrPause?.Invoke();
		if (IsPlayScheduled && await Player.SchedulePlay(delegate
		{
			IsPlayScheduled = false;
		}, Sys.Downloader.SetPlayInactiveToAllItems, TimeSpan.FromSeconds(60.0)))
		{
			IsPlayActive = true;
		}
	}

	protected string FixDirectoryIfExist(string path)
	{
		string text = path;
		int num = 1;
		while (Directory.Exists(text) && num < 100000)
		{
			text = $"{path}.#{num++}";
		}
		return text;
	}

	public bool OpenCompleteDir()
	{
		if (!CanOpen)
		{
			return false;
		}
		Process.Start("explorer.exe", FinalDir);
		return true;
	}

	public virtual void DownloadResume()
	{
		if (!IsHistory)
		{
			RawStatus = DownloadStatus.Queued;
		}
	}

	public virtual void DownloadPause()
	{
		if (!IsHistory)
		{
			RawStatus = DownloadStatus.Pausing;
		}
	}
}
