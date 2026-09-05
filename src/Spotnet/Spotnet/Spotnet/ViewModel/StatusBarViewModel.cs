using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Spotnet.Mvvm;
using NLog;
using Spotnet.Controls;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.ViewModel;

public class StatusBarViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private bool _dbUpdateImageEnabled;

	private bool _dbUpdateImageStarted;

	private string _dbUpdateStatusMessage = string.Empty;

	private string _spotsListStatusMessage;

	private bool _statusBarProgressIndeterminate;

	private string _statusBarProgressTooltip;

	private int _statusBarProgressValue;

	private TaskbarItemProgressState _taskBarProgressState;

	private double _taskBarProgressValue;

	public TaskbarItemProgressState TaskBarProgressState
	{
		get
		{
			return _taskBarProgressState;
		}
		private set
		{
			if (!_taskBarProgressState.Equals(value))
			{
				_taskBarProgressState = value;
				RaisePropertyChanged("TaskBarProgressState");
			}
		}
	}

	public double TaskBarProgressValue
	{
		get
		{
			return _taskBarProgressValue;
		}
		private set
		{
			if (!_taskBarProgressValue.Equals(value))
			{
				_taskBarProgressValue = value;
				RaisePropertyChanged("TaskBarProgressValue");
			}
		}
	}

	public string StatusBarProgressTooltip
	{
		get
		{
			return _statusBarProgressTooltip;
		}
		private set
		{
			if (!value.Equals(_statusBarProgressTooltip))
			{
				_statusBarProgressTooltip = value;
				RaisePropertyChanged("StatusBarProgressTooltip");
			}
		}
	}

	public bool StatusBarProgressIndeterminate
	{
		get
		{
			return _statusBarProgressIndeterminate;
		}
		set
		{
			if (!value.Equals(_statusBarProgressIndeterminate))
			{
				_statusBarProgressIndeterminate = value;
				RaisePropertyChanged("StatusBarProgressIndeterminate");
			}
		}
	}

	public string DbUpdateStatusMessage
	{
		get
		{
			return _dbUpdateStatusMessage;
		}
		private set
		{
			if (!value.IsNullOrEmpty() && !value.Equals(_dbUpdateStatusMessage))
			{
				_dbUpdateStatusMessage = value;
				RaisePropertyChanged("DbUpdateStatusMessage");
			}
		}
	}

	public string SpotsListStatusMessage
	{
		get
		{
			return _spotsListStatusMessage;
		}
		set
		{
			if (!value.IsNullOrEmpty() && !value.Equals(_spotsListStatusMessage))
			{
				_spotsListStatusMessage = value;
				RaisePropertyChanged("SpotsListStatusMessage");
			}
		}
	}

	public string ProxyIcon
	{
		get
		{
			if (!Settings.Default.UseSocksProxy)
			{
				return "\uf09c";
			}
			return "\uf023";
		}
	}

	public Visibility ProxyIconVisibility
	{
		get
		{
			if (!SocksProxy.GlobalyEnabled)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public SolidColorBrush ProxyIconForeground => (SolidColorBrush)(Settings.Default.UseSocksProxy ? (new BrushConverter().ConvertFromString("#39A633") as SolidColorBrush) : Application.Current.FindResource("GrayBrush2"));

	public string ProxyIconToolTip
	{
		get
		{
			if (!Settings.Default.UseSocksProxy)
			{
				return "SOCKS5 proxy is Off";
			}
			return "SOCKS5 proxy is On";
		}
	}

	public string DbUpdateIcon
	{
		get
		{
			if (!DbUpdateImageStarted)
			{
				return "\uf021";
			}
			return "\uf04c";
		}
	}

	public SolidColorBrush DbUpdateIconForeground => (SolidColorBrush)(DbUpdateImageEnabled ? Application.Current.FindResource("BrightColorBrush2") : Application.Current.FindResource("GrayBrush4"));

	public string DbUpdateImageCursor
	{
		get
		{
			if (!DbUpdateImageEnabled)
			{
				return "";
			}
			return "Hand";
		}
	}

	public string DbUpdateImageToolTip
	{
		get
		{
			if (!DbUpdateImageEnabled)
			{
				return null;
			}
			if (!DbUpdateImageStarted)
			{
				return Words.DbUpdateStart;
			}
			return Words.DbUpdatePause;
		}
	}

	public string SystemStateText => SystemStateChecker.ProblemsDescription;

	public string SystemStateImageSource
	{
		get
		{
			if (!SystemStateChecker.IsGreen)
			{
				return "../Resources/ImagesInternal/warning.ico";
			}
			return "../Resources/ImagesInternal/success.ico";
		}
	}

	public ModernTooltipType SystemStateType
	{
		get
		{
			if (!SystemStateChecker.IsGreen)
			{
				return ModernTooltipType.Warning;
			}
			return ModernTooltipType.Info;
		}
	}

	public bool DbUpdateImageEnabled
	{
		get
		{
			return _dbUpdateImageEnabled;
		}
		set
		{
			if (!value.Equals(_dbUpdateImageEnabled))
			{
				_dbUpdateImageEnabled = value;
				RaisePropertyChanged("DbUpdateImageEnabled");
				RaisePropertyChanged("DbUpdateIcon");
				RaisePropertyChanged("DbUpdateIconForeground");
				RaisePropertyChanged("DbUpdateImageCursor");
				RaisePropertyChanged("DbUpdateImageToolTip");
			}
		}
	}

	public bool DbUpdateImageStarted
	{
		get
		{
			return _dbUpdateImageStarted;
		}
		set
		{
			if (!value.Equals(_dbUpdateImageStarted))
			{
				_dbUpdateImageStarted = value;
				RaisePropertyChanged("DbUpdateImageStarted");
				RaisePropertyChanged("DbUpdateIcon");
				RaisePropertyChanged("DbUpdateIconForeground");
				RaisePropertyChanged("DbUpdateImageToolTip");
			}
		}
	}

	public int StatusBarProgressValue
	{
		get
		{
			return _statusBarProgressValue;
		}
		private set
		{
			if (!_statusBarProgressValue.Equals(value))
			{
				_statusBarProgressValue = value;
				RaisePropertyChanged("StatusBarProgressValue");
			}
		}
	}

	public StatusBarViewModel()
	{
		if (Settings.Default.DbAutoUpdateIntervalMin > 0 && Settings.Default.DbAutoUpdateEnabled)
		{
			DbUpdateImageStarted = true;
			DbUpdateImageEnabled = false;
		}
		else
		{
			SetDbUpdateProgressStatus(Words.DbUpdatePaused, -1);
			DbUpdateImageStarted = false;
			DbUpdateImageEnabled = true;
		}
		SystemStateChecker.StateChanged += delegate
		{
			RaisePropertyChanged("SystemStateText");
			RaisePropertyChanged("SystemStateImageSource");
			RaisePropertyChanged("SystemStateType");
		};
		SocksProxy.StateChanged += delegate
		{
			RaisePropertyChanged("ProxyIcon");
			RaisePropertyChanged("ProxyIconVisibility");
			RaisePropertyChanged("ProxyIconForeground");
		};
	}

	public void SetStatusBarProgressTooltip(string message = null)
	{
		if (message == null)
		{
			if (StatusBarProgressIndeterminate)
			{
				StatusBarProgressTooltip = Words.InProgress + "...";
				return;
			}
			if (StatusBarProgressValue == 0)
			{
				StatusBarProgressTooltip = "OK";
				return;
			}
			StatusBarProgressTooltip = Words.InProgress + ", " + StatusBarProgressValue + "%";
		}
		else
		{
			StatusBarProgressTooltip = message;
		}
	}

	public void SetDbUpdateProgressStatus(string message, int valuePercentage = 0)
	{
		try
		{
			if (valuePercentage == 0)
			{
				StatusBarProgressIndeterminate = true;
			}
			else if (valuePercentage < 0)
			{
				StatusBarProgressValue = 0;
				StatusBarProgressIndeterminate = false;
			}
			else
			{
				StatusBarProgressValue = valuePercentage;
				StatusBarProgressIndeterminate = false;
			}
			SetStatusBarProgressTooltip();
			DbUpdateStatusMessage = message;
			SetDefaultSpotsListStatusMessage();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public void SetTaskBarProgressStatus(string message, int valuePercentage = 0)
	{
		try
		{
			if (valuePercentage == 0)
			{
				TaskBarProgressState = TaskbarItemProgressState.Indeterminate;
			}
			else if (valuePercentage < 0)
			{
				TaskBarProgressValue = 0.0;
				TaskBarProgressState = TaskbarItemProgressState.None;
			}
			else
			{
				TaskBarProgressValue = (double)valuePercentage / 100.0;
				TaskBarProgressState = TaskbarItemProgressState.Normal;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	internal void SetDefaultSpotsListStatusMessage()
	{
		try
		{
			SpotProvider spotProvider = Sys.MainWindow.SpotProvider;
			if (spotProvider != null)
			{
				long databaseCount = Settings.Default.DatabaseCount;
				string text = databaseCount.ToString("#,#").Replace(",", ".");
				string spotsListStatusMessage;
				if (databaseCount < 1)
				{
					spotsListStatusMessage = Words.NoSpotsInDatabase;
				}
				else if (spotProvider.QueryCount < 1 && databaseCount > 0 && spotProvider.IsCacheQueryCountPrecise)
				{
					spotsListStatusMessage = Words.NoSpotsFound;
				}
				else if (spotProvider.RowFilter.IsNullOrEmpty() || spotProvider.RowFilter.ToLower().Equals("cat < 9") || spotProvider.RowFilter.Replace(" ", "").EqualsIgnoreCase("cat!=0"))
				{
					spotsListStatusMessage = ((databaseCount != 1) ? (text + " " + Words.Spots + " " + Words.InDatabase) : ("1 " + Words.spot + " " + Words.InDatabase));
				}
				else if (!spotProvider.IsCacheQueryCountPrecise)
				{
					spotsListStatusMessage = Words.Calculating;
					spotsListStatusMessage = spotsListStatusMessage + "... (" + Words.OfThe + " " + text + ")";
				}
				else
				{
					long queryCount = spotProvider.QueryCount;
					spotsListStatusMessage = ((queryCount < 1) ? Words.NoSpotsFound : ((queryCount != 1) ? (spotProvider.QueryCount.ToString("#,#").Replace(",", ".") + " " + Words.Spots + " (" + Words.OfThe + " " + text + ")") : ("1 " + Words.spot + " (" + Words.OfThe + " " + text + ")")));
				}
				SpotsListStatusMessage = spotsListStatusMessage;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public async void IconBlink(UIElement ctrl, short time)
	{
		int i = 0;
		ctrl.Visibility = Visibility.Hidden;
		do
		{
			await Task.Delay(500);
			ctrl.Visibility = ((i++ % 2 != 0) ? Visibility.Hidden : Visibility.Visible);
		}
		while (i != 2 * time);
		ctrl.Visibility = Visibility.Visible;
	}
}
