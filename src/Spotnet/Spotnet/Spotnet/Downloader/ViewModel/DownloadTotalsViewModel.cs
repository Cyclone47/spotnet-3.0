using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Downloader.ViewModel;

public class DownloadTotalsViewModel : INotifyPropertyChanged
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private int _perc;

	private DownloadStatus _rawStatus;

	private int _secondsLeft;

	private double _sizeMegaBytes;

	private string _speed;

	public bool IsDownloading => _rawStatus == DownloadStatus.Downloading;

	public Visibility ProgressBarVisibility => Visibility.Visible;

	public double SizeMegaBytes
	{
		get
		{
			return _sizeMegaBytes;
		}
		set
		{
			if (_sizeMegaBytes != value)
			{
				_sizeMegaBytes = value;
				NotifyPropertyChanged("Size");
				NotifyPropertyChanged("SizeMegaBytes");
			}
		}
	}

	public double Opacity => 1.0;

	public string Size => FormatSizeMegaBytes(SizeMegaBytes);

	public int Perc
	{
		get
		{
			return _perc;
		}
		set
		{
			if (_perc != value)
			{
				_perc = value;
				NotifyPropertyChanged("Perc");
				NotifyPropertyChanged("Tooltip");
			}
		}
	}

	public string Tooltip => Words.ColumnProgress + " " + _perc + "%";

	public DownloadStatus RawStatus
	{
		get
		{
			return _rawStatus;
		}
		set
		{
			if (_rawStatus != value)
			{
				_rawStatus = value;
				NotifyPropertyChanged("Status");
			}
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
			if (!IsDownloading)
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

	public string Status => _rawStatus switch
	{
		DownloadStatus.Paused => Words.StatPaused, 
		DownloadStatus.NzbDownloading => Words.NZBDownloading, 
		DownloadStatus.Downloading => Words.StatDownloading, 
		DownloadStatus.Repairing => Words.StatRepairing, 
		DownloadStatus.Unpacking => Words.StatExtracting, 
		DownloadStatus.Moving => Words.StatMoving, 
		DownloadStatus.Queued => Words.StatQueued, 
		DownloadStatus.Verifying => Words.StatVerifying, 
		DownloadStatus.Checking => Words.StatQuickCheck, 
		DownloadStatus.Par2PieceDownloading => Words.StatPar2Downloading, 
		DownloadStatus.Unknown => Words.Unknown, 
		DownloadStatus.Success => Words.StatCompleted, 
		DownloadStatus.Failure => Words.StatFailed, 
		DownloadStatus.Warning => "Warning", 
		DownloadStatus.Deleted => "Deleted", 
		DownloadStatus.Empty => "", 
		_ => _rawStatus.ToString(), 
	};

	public string Time
	{
		get
		{
			if (_rawStatus == DownloadStatus.Queued || _secondsLeft == 0)
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

	public event PropertyChangedEventHandler PropertyChanged;

	public DownloadTotalsViewModel(DownloadStatus status, int perc, double sizeMegaBytes, int secondsLeft, string zSpeed)
	{
		_perc = perc;
		_sizeMegaBytes = sizeMegaBytes;
		_secondsLeft = secondsLeft;
		_speed = zSpeed;
		RawStatus = status;
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
		return $"{text}:{text2}:{text3}";
	}

	private string FormatSizeMegaBytes(double sizeMegaBytes)
	{
		if (sizeMegaBytes >= 104857600.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 0) + " TB";
		}
		if (sizeMegaBytes >= 10485760.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 1) + " TB";
		}
		if (sizeMegaBytes >= 1024000.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 2) + " TB";
		}
		if (sizeMegaBytes >= 102400.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 0) + " GB";
		}
		if (sizeMegaBytes >= 10240.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 1) + " GB";
		}
		if (sizeMegaBytes >= 1000.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 2) + " GB";
		}
		if (sizeMegaBytes >= 100.0)
		{
			return Math.Round(sizeMegaBytes, 0) + " MB";
		}
		if (sizeMegaBytes >= 10.0)
		{
			return Math.Round(sizeMegaBytes, 1) + " MB";
		}
		return Math.Round(sizeMegaBytes, 2) + " MB";
	}

	private void NotifyPropertyChanged(string info)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
	}
}
