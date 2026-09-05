using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;
using Spotnet.Mvvm;
using Spotnet.Mvvm.Threading;
using NLog;
using System.IO;
using Spotnet.Downloader.Controls.Player;
using Spotnet.Model;

namespace Spotnet.Downloader.ViewModel;

public class PlayerViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly Timer _timerToUpdateDownloadProgress;

	private double _downloadProgress;

	private bool _isPlaying;

	private bool _isStopDetected;

	private TimeSpan _playerPositionSaved = TimeSpan.Zero;

	private static readonly IList<string> PlaylistDirectoriesToIgnore = new string[1] { "__unpack" };

	public VlcPlayer Player;

	private readonly Timer _timerToDetectTheStop;

	private TimeSpan _timeWhenStopDetected;

	public ObservableCollection<PlaylistItemViewModel> PlaylistItems { get; private set; }

	public PlaylistItemViewModel CurrentPlaylistItemPlayed { get; private set; }

	public DownloaderItemViewModel ParentDownloaderItem { get; private set; }

	public bool IsStopDetected
	{
		get
		{
			return _isStopDetected;
		}
		set
		{
			if (_isStopDetected != value)
			{
				_isStopDetected = value;
				RaisePropertyChanged("IsStopDetected");
				RaisePropertyChanged("IsPlayButtonInTheCenterOfVideoVisible");
			}
		}
	}

	public double DownloadProgress
	{
		get
		{
			return _downloadProgress;
		}
		private set
		{
			if (!(Math.Abs(_downloadProgress - value) < 0.0001))
			{
				_downloadProgress = value;
				RaisePropertyChanged("DownloadProgress");
			}
		}
	}

	public string PlayPauseIcon
	{
		get
		{
			if (!IsPlaying)
			{
				return "\uf04b";
			}
			return "\uf04c";
		}
	}

	public string VolumeIcon
	{
		get
		{
			if (Player == null || !Player.IsMute)
			{
				return "\uf028";
			}
			return "\uf026";
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
				this.PlayStatusChanged?.Invoke(_isPlaying);
				if (!_isPlaying)
				{
					IsStopDetected = false;
				}
				RaisePropertyChanged("IsPlaying");
				RaisePropertyChanged("IsPlayButtonInTheCenterOfVideoVisible");
				RaisePropertyChanged("PlayPauseIcon");
			}
		}
	}

	public bool IsPlayButtonInTheCenterOfVideoVisible
	{
		get
		{
			if (!IsPlaying)
			{
				return !IsStopDetected;
			}
			return false;
		}
	}

	public event Action<bool> PlayStatusChanged;

	public event Action FullStop;

	public event Action Disposed;

	public event Func<PlaylistItemViewModel, TimeSpan, bool, Task> StartPlaying = (PlaylistItemViewModel i, TimeSpan t, bool a) => (Task)null;

	public PlayerViewModel()
	{
		if (Sys.MainWindow != null)
		{
			Sys.MainWindow.TabSelectionChanged += OnTabChanged;
		}
		Sys.DownloadsPlayer = this;
		PlaylistItems = new ObservableCollection<PlaylistItemViewModel>();
		_timerToUpdateDownloadProgress = new Timer(1000.0)
		{
			AutoReset = true
		};
		_timerToUpdateDownloadProgress.Elapsed += TimerToUpdateDownloadProgressOnElapsed;
		_timerToDetectTheStop = new Timer(1500.0)
		{
			AutoReset = false
		};
		_timerToDetectTheStop.Elapsed += TimerToDetectTheStopOnElapsed;
	}

	private void TimerToUpdateDownloadProgressOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
	{
		if (ParentDownloaderItem != null && ParentDownloaderItem.IsHistory)
		{
			DownloadProgress = 1.0;
		}
		else if (CurrentPlaylistItemPlayed != null)
		{
			DownloadProgress = CurrentPlaylistItemPlayed.DownloadProgress;
		}
	}

	public void RaiseVolumeChanged()
	{
		RaisePropertyChanged("VolumeIcon");
	}

	public void PlayerInitialize()
	{
		Player.IsMuteChanged += PlayerOnIsMuteChanged;
		_timerToUpdateDownloadProgress.Start();
	}

	private void OnTabChanged()
	{
		if (Player != null)
		{
			IsPlaying = false;
			Player.VlcMediaPlayer.Pause();
		}
	}

	private void PlayerOnIsMuteChanged(object sender, EventArgs eventArgs)
	{
		RaisePropertyChanged("VolumeIcon");
	}

	private PlaylistItemViewModel AddNewItemToPlaylist(string filePath)
	{
		PlaylistItemViewModel playlistItemViewModel = new PlaylistItemViewModel(filePath, ParentDownloaderItem);
		PlaylistItems.Add(playlistItemViewModel);
		return playlistItemViewModel;
	}

	private void MarkPlaylistItemAsPlayed(PlaylistItemViewModel item)
	{
		foreach (PlaylistItemViewModel playlistItem in PlaylistItems)
		{
			playlistItem.IsPlaying = playlistItem == item;
		}
	}

	public void StartPlayFirstItem(DownloaderItemViewModel item)
	{
		UpdatePlaylist(item).Task.ContinueWith(delegate
		{
			DispatcherHelper.CheckBeginInvokeOnUI(delegate
			{
				PlaylistItemViewModel playlistItemViewModel = PlaylistItems.OrderBy((PlaylistItemViewModel vm) => vm.FileFullPath).FirstOrDefault();
				if (playlistItemViewModel != null)
				{
					Play(playlistItemViewModel, TimeSpan.Zero);
				}
			});
		});
	}

	public DispatcherOperation UpdatePlaylist(DownloaderItemViewModel downloaderItem)
	{
		return DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
		{
			ParentDownloaderItem = downloaderItem;
			TimerToUpdateDownloadProgressOnElapsed(null, null);
			List<string> list = downloaderItem.Player.GetListOfFilesToPlay().ToList();
			if (!list.Any())
			{
				PlaylistItems.Clear();
				return;
			}
			foreach (PlaylistItemViewModel item in PlaylistItems.ToList())
			{
				if (!list.Contains(item.FileFullPath))
				{
					PlaylistItems.Remove(item);
				}
			}
			foreach (string file in list)
			{
				if (!PlaylistDirectoriesToIgnore.Any((string dirToIgnore) => file.Contains(dirToIgnore)) && !PlaylistItems.Any((PlaylistItemViewModel i) => i.FileFullPath.Equals(file)))
				{
					AddNewItemToPlaylist(file);
				}
			}
		});
	}

	public DispatcherOperation Play(PlaylistItemViewModel itemToPlay, TimeSpan startPosition, bool applyAnimation = true)
	{
		return DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
		{
			CurrentPlaylistItemPlayed = itemToPlay;
			MarkPlaylistItemAsPlayed(itemToPlay);
			this.StartPlaying(itemToPlay, startPosition, applyAnimation);
		});
	}

	private PlaylistItemViewModel FindPlayingItemInAnotherDirectory(string path)
	{
		return PlaylistItems.ToList().FirstOrDefault((PlaylistItemViewModel p) => CurrentPlaylistItemPlayed.Title.Equals(p.Title) && IsSubDir(path, p.FileFullPath));
	}

	public bool SwitchToAnotherDirectory(string path, DownloaderItemViewModel item)
	{
		if (Player == null || item != ParentDownloaderItem)
		{
			return true;
		}
		bool isPlaying = IsPlaying;
		TimeSpan time = Player.Time;
		UpdatePlaylist(ParentDownloaderItem).Wait();
		Log.Debug("Switch video play to new path: " + path);
		PlaylistItemViewModel playlistItemViewModel = FindPlayingItemInAnotherDirectory(path);
		if (playlistItemViewModel == null)
		{
			return false;
		}
		Play(playlistItemViewModel, time, applyAnimation: false).Wait();
		if (!isPlaying)
		{
			Pause();
		}
		return true;
	}

	public bool IsSubDir(string parentPath, string childPath)
	{
		Uri uri = new Uri(parentPath);
		for (DirectoryInfo parent = new DirectoryInfo(childPath).Parent; parent != null; parent = parent.Parent)
		{
			if (new Uri(parent.FullName) == uri)
			{
				return true;
			}
		}
		return false;
	}

	public void PlayerFullStop()
	{
		this.FullStop?.Invoke();
	}

	public void Dispose()
	{
		IsPlaying = false;
		_timerToUpdateDownloadProgress.Stop();
		_timerToDetectTheStop.Stop();
		if (Player != null)
		{
			Player.IsMuteChanged -= PlayerOnIsMuteChanged;
		}
		this.Disposed?.Invoke();
	}

	public void TryToPlayNext()
	{
		if (CurrentPlaylistItemPlayed != null)
		{
			int num = PlaylistItems.IndexOf(CurrentPlaylistItemPlayed) + 1;
			if (num < PlaylistItems.Count)
			{
				Play(PlaylistItems[num], TimeSpan.Zero, applyAnimation: false);
			}
		}
	}

	public void Resume()
	{
		if (!IsPlaying)
		{
			Player.VlcMediaPlayer.Play();
			IsPlaying = true;
		}
	}

	public void Pause()
	{
		if (IsPlaying)
		{
			Player.VlcMediaPlayer.Pause();
			IsPlaying = false;
		}
	}

	public void PauseOrResume()
	{
		if (Player != null)
		{
			if (IsPlaying)
			{
				Pause();
			}
			else
			{
				Resume();
			}
		}
	}

	private void TimerToDetectTheStopOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
	{
		if (Player != null && IsPlaying)
		{
			_timeWhenStopDetected = Player.Time;
			IsStopDetected = true;
		}
	}

	public void RestartTimerToDetectTheStop()
	{
		IsStopDetected = false;
		_timerToDetectTheStop.Stop();
		_timerToDetectTheStop.Start();
	}
}
