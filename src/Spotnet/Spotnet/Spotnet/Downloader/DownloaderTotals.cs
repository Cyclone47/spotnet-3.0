using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.Downloader;

public class DownloaderTotals : ObservableCollection<DownloaderItemViewModel>
{
	public delegate void ProgressChangedEventHandler(double lVal);

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly object _lockCollection = new object();

	private readonly DownloaderItemViewModel _row;

	private readonly DownloaderItems _sourceForTotals;

	private int _totalPercents;

	private DownloadStatus _totalStatus;

	private int _totalSecondsLeft;

	private string _totalSpeed;

	private double _totalSizeMegaBytes;

	public event ProgressChangedEventHandler ProgressChanged;

	public DownloaderTotals(DownloaderItems sourceForTotals)
	{
		_sourceForTotals = sourceForTotals;
		_row = Update(0, "", DownloadStatus.Totals, 0, 0.0, 0, null, null, "", 0, 0);
		if (!Sys.Downloader.IsStarted)
		{
			_row.RawStatus = DownloadStatus.Starting;
		}
		Sys.Downloader.DownloaderStatusChanged += delegate(object sender, DownloadStatus status)
		{
			_row.RawStatus = status;
		};
	}

	public DownloaderItemViewModel Update(int sId, string titel, DownloadStatus status, int perc, double sizeMegaBytes, int time, string incompleteDir, string completeDir, string zSpeed, int totalArticles, int successArticles)
	{
		DownloaderItemViewModel downloaderItemViewModel;
		lock (_lockCollection)
		{
			if (_row != null)
			{
				_row.Perc = perc;
				_row.RawStatus = status;
				if (!_row.IsDownloading)
				{
					zSpeed = "";
				}
				_row.SecondsLeft = time;
				_row.Speed = zSpeed;
				_row.SizeMegaBytes = sizeMegaBytes;
				_row.TotalArticles = totalArticles;
				_row.SuccessArticles = successArticles;
				downloaderItemViewModel = _row;
			}
			else
			{
				downloaderItemViewModel = DownloaderItemFactory.New(sId, titel, status, perc, sizeMegaBytes, time, 20000, incompleteDir, completeDir, zSpeed, null, 0, null, 0L, 0L);
				Add(downloaderItemViewModel);
			}
		}
		return downloaderItemViewModel;
	}

	private void ReportProgress(bool flag1, bool flag2, DownloaderItemViewModel downloaderItem)
	{
		ProgressChangedEventHandler progressChanged = this.ProgressChanged;
		if (progressChanged != null)
		{
			if (flag2)
			{
				progressChanged(-1.0);
			}
			else if (flag1 && downloaderItem.IsDownloading)
			{
				progressChanged((downloaderItem.Perc > 0.0) ? downloaderItem.Perc : 1.0);
			}
		}
	}

	public void UpdateTotal(bool onGlobalPause)
	{
		lock (_lockCollection)
		{
			bool flag = false;
			bool flag2 = false;
			bool isDownloading = _row.IsDownloading;
			UpdateTotals(onGlobalPause);
			if (Math.Abs(_row.Perc - (double)_totalPercents) > 0.01)
			{
				_row.Perc = _totalPercents;
				flag = true;
			}
			if (_row.RawStatus != DownloadStatus.Starting && _row.RawStatus != DownloadStatus.Stopping && _row.RawStatus != DownloadStatus.Failure && _row.RawStatus != DownloadStatus.FailureNoSuchArticle)
			{
				_row.RawStatus = _totalStatus;
			}
			_row.SecondsLeft = _totalSecondsLeft;
			_row.Speed = _totalSpeed;
			_row.SizeMegaBytes = _totalSizeMegaBytes;
			bool isDownloading2 = _row.IsDownloading;
			if (isDownloading && !isDownloading2)
			{
				flag2 = true;
			}
			if (isDownloading2 && !isDownloading)
			{
				flag = true;
			}
			if (flag || flag2)
			{
				ReportProgress(flag, flag2, _row);
			}
		}
	}

	private void UpdateTotals(bool onGlobalPause)
	{
		_totalSpeed = "";
		_totalSecondsLeft = 0;
		_totalSizeMegaBytes = 0.0;
		_totalPercents = -1;
		double num = 0.0;
		long num2 = 0L;
		_totalStatus = DownloadStatus.Success;
		foreach (KeyValuePair<int, DownloaderItemViewModel> item in _sourceForTotals.ItemsDict.ToList())
		{
			DownloaderItemViewModel value = item.Value;
			if (!value.IsNzbDownload && !value.IsPaused)
			{
				if (value.IsDownloading || value.IsQueued)
				{
					_totalStatus = DownloadStatus.Downloading;
				}
				if (!value.Speed.IsNullOrEmpty())
				{
					num2 += value.BytesPerSecond;
				}
				_totalSizeMegaBytes += value.SizeMegaBytes;
				num += ((value.IsHistory || value.IsPostProcess) ? value.SizeMegaBytes : (value.SizeMegaBytes / 100.0 * value.Perc));
			}
		}
		_totalSpeed = ((num2 >= 10) ? AverageSpeedCalculator.BpsToString(num2) : "");
		if (num2 < 10)
		{
			_totalSpeed = "";
			_totalSecondsLeft = 0;
		}
		else
		{
			_totalSpeed = AverageSpeedCalculator.BpsToString(num2);
			double num3 = (_totalSizeMegaBytes - num) * 1024.0 * 1024.0 / (double)num2;
			_totalSecondsLeft = (int)num3;
		}
		if (_totalSizeMegaBytes > 0.0)
		{
			_totalPercents = (int)(num / _totalSizeMegaBytes * 100.0);
		}
		if (onGlobalPause)
		{
			_totalStatus = DownloadStatus.Paused;
		}
	}
}
