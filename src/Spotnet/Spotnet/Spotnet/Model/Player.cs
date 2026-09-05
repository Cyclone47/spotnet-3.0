using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;

namespace Spotnet.Model;

public class Player
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static CancellationTokenSource _cancelSourceForPlay;

	private static readonly object LockRoot = new object();

	private readonly string[] _extentionsSupported = new string[54]
	{
		".ASX", ".DTS", ".GXF", ".M2V", ".M3U", ".M4V", ".MPEG1", ".MPEG2", ".MTS", ".MXF",
		".OGM", ".PLS", ".A52", ".AAC", ".B4S", ".CUE", ".DIVX", ".DV", ".FLV", ".M1V",
		".M2TS", ".MKV", ".MOV", ".MPEG4", ".OMA", ".SPX", ".TS", ".VLC", ".VOB", ".XSPF",
		".DAT", ".BIN", ".IFO", ".3G2", ".AVI", ".MPEG", ".MPG", ".FLAC", ".M4A", ".MP1",
		".OGG", ".WAV", ".XM", ".3GP", ".WMV", ".AC3", ".ASF", ".MOD", ".MP2", ".MP3",
		".MP4", ".WMA", ".MKA", ".M4P"
	};

	private readonly DownloaderItemViewModel _item;

	private readonly System.Timers.Timer _playlistUpdateTimer = new System.Timers.Timer();

	internal Player(DownloaderItemViewModel item)
	{
		Player player = this;
		_item = item;
		_playlistUpdateTimer.AutoReset = false;
		_playlistUpdateTimer.Interval = 10000.0;
		_playlistUpdateTimer.Elapsed += delegate
		{
			if (item.IsPlayActive && !item.IsHistory)
			{
				Sys.DownloadsPlayer.UpdatePlaylist(item);
				player._playlistUpdateTimer.Start();
			}
		};
	}

	internal async Task<bool> SchedulePlay(Action onStopWaiting = null, Action beforeStartPlaying = null, TimeSpan timeout = default(TimeSpan))
	{
		if (timeout == default(TimeSpan))
		{
			timeout = TimeSpan.Zero;
		}
		CancellationTokenSource cancelLocal;
		lock (LockRoot)
		{
			_cancelSourceForPlay?.Cancel();
			_cancelSourceForPlay = new CancellationTokenSource();
			cancelLocal = _cancelSourceForPlay;
		}
		if (GetListOfFilesToPlay().Any((string f) => new System.IO.FileInfo(f).Length > 0))
		{
			onStopWaiting?.Invoke();
			beforeStartPlaying?.Invoke();
			Play();
		}
		else
		{
			if (_item.IsHistory)
			{
				return false;
			}
			bool flag;
			try
			{
				flag = await WaitForFilesToPlay(timeout, cancelLocal.Token);
			}
			finally
			{
				onStopWaiting?.Invoke();
			}
			if (cancelLocal.IsCancellationRequested || !flag)
			{
				return false;
			}
			beforeStartPlaying?.Invoke();
			Play();
		}
		return true;
	}

	private void Play()
	{
		Sys.DownloadsPlayer.StartPlayFirstItem(_item);
	}

	public Task<bool> WaitForFilesToPlay(TimeSpan timeout, CancellationToken cancel)
	{
		return Task.Run(delegate
		{
			DateTime now = DateTime.Now;
			bool flag = GetListOfFilesToPlay().Any((string f) => new System.IO.FileInfo(f).Length > 0);
			while (!flag && DateTime.Now - now < timeout && !cancel.IsCancellationRequested)
			{
				Thread.Sleep(1000);
				flag = GetListOfFilesToPlay().Any();
			}
			return flag;
		}, cancel);
	}

	private IEnumerable<string> GetListOfFilesToPlay(string location)
	{
		if (location.IsNullOrEmpty() || !System.IO.Directory.Exists(location))
		{
			yield break;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < 3; i++)
		{
			try
			{
				list = System.IO.Directory.GetFiles(location, "*.*", SearchOption.AllDirectories).ToList();
			}
			catch (Exception ex)
			{
				Log.Debug("Failed to get the list of files to play. Try " + i + ". Error: " + ex.Message);
				Thread.Sleep(100);
				continue;
			}
			break;
		}
		foreach (string item in list)
		{
			string extension = System.IO.Path.GetExtension(System.IO.Path.GetFileName(item));
			if (extension != null && _extentionsSupported.Contains(extension.ToUpper()) && !extension.ToUpper().Equals(".IFO"))
			{
				yield return item;
			}
		}
	}

	public IEnumerable<string> GetListOfFilesToPlay()
	{
		if (!_item.CompleteDir.Equals(_item.IncompleteDir))
		{
			return GetListOfFilesToPlay(_item.CompleteDir).Concat(GetListOfFilesToPlay(_item.IncompleteDir)).Distinct();
		}
		return GetListOfFilesToPlay(_item.CompleteDir);
	}

	public void StopUpdatePlaylist()
	{
		_playlistUpdateTimer.Stop();
	}

	public void StartUpdatePlaylist()
	{
		_playlistUpdateTimer.Start();
	}
}
