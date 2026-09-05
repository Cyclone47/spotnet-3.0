using System;
using System.Collections.Generic;
using System.Windows;
using Spotnet.Mvvm;
using NLog;
using System.IO;
using Spotnet.Extensions;

namespace Spotnet.Downloader.ViewModel;

public class PlaylistItemViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private bool _isPlaying;

	private TimeSpan _length;

	private readonly DownloaderItemViewModel _parentDownloaderItem;

	public string DurationString
	{
		get
		{
			if (!(Length > TimeSpan.Zero))
			{
				return "";
			}
			return Length.ToShortTimeString();
		}
	}

	public FontWeight FontWeight
	{
		get
		{
			if (!IsPlaying)
			{
				return FontWeights.Normal;
			}
			return FontWeights.Bold;
		}
	}

	public long FileFullSize { get; private set; }

	public string FileFullPath { get; }

	public string Title => Path.GetFileName(FileFullPath);

	private long FileSize
	{
		get
		{
			if (!File.Exists(FileFullPath))
			{
				return 0L;
			}
			return new FileInfo(FileFullPath).Length;
		}
	}

	public double DownloadProgress
	{
		get
		{
			if (_parentDownloaderItem.IsHistory)
			{
				return 1.0;
			}
			if (FileFullSize == 0L)
			{
				TryToDetermineFileFullSize();
				if (FileFullSize == 0L)
				{
					return 0.0;
				}
			}
			return 1.0 * (double)FileSize / (double)FileFullSize;
		}
	}

	public TimeSpan Length
	{
		get
		{
			return _length;
		}
		set
		{
			if (!(_length == value))
			{
				_length = value;
				RaisePropertyChanged("Length");
				RaisePropertyChanged("DurationString");
			}
		}
	}

	public bool IsPlaying
	{
		get
		{
			return _isPlaying;
		}
		set
		{
			if (_isPlaying != value)
			{
				_isPlaying = value;
				RaisePropertyChanged("IsPlaying");
				RaisePropertyChanged("FontWeight");
			}
		}
	}

	public PlaylistItemViewModel(string filePath, DownloaderItemViewModel parentDownloaderItem)
	{
		FileFullPath = filePath;
		_parentDownloaderItem = parentDownloaderItem;
	}

	private void TryToDetermineFileFullSize()
	{
		if (_parentDownloaderItem.IsHistory)
		{
			return;
		}
		try
		{
			Dictionary<string, long> dictionary = new NzbGetRarScanner(_parentDownloaderItem.IncompleteDir).ParseFiles();
			string key = FileFullPath.Replace(_parentDownloaderItem.CompleteDir + "\\", "");
			if (dictionary.ContainsKey(key))
			{
				FileFullSize = dictionary[key];
			}
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
		}
	}
}
