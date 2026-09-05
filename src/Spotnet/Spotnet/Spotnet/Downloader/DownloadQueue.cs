using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Downloader;

internal static class DownloadQueue
{
	public const string DownloaderBodyMark = "D";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object LockUpdateDownloadQueue = new object();

	private static CancellationTokenSource _cTokenSource;

	private static readonly SynchronizedList<Task> DownloadTasksList = new SynchronizedList<Task>();

	internal static bool IsQueueStarted;

	private static readonly object LockStartStop = new object();

	private static bool _isDownloadQueueReordered;

	private static readonly object LockDownloadQueueReordering = new object();

	private static ConcurrentStack<NNTPSegment> _segmentsToProcessStack;

	private static ConcurrentQueue<NNTPSegment> _segmentsUnderTimeoutQueue;

	private static DateTime _serverStartToProcessDateTime = DateTime.MinValue;

	private static readonly TimeSpan PausingTimeout = TimeSpan.FromSeconds(120.0);

	private static readonly List<string> ListOfGroupsNotFound = new List<string>();

	private static bool _updatePrioritiesOneMoreTime;

	private static readonly ManualResetEventSlim EmptyQueueEvent = new ManualResetEventSlim();

	private static SynchronizedList<BlockingCollection<NNTPSegment>> _segmentsToDownload;

	private static BlockingCollection<NNTPSegment> _segmentsToGetSlave;

	private static readonly ManualResetEventSlim OneDownloadTaskCompleted = new ManualResetEventSlim();

	private static int _numberOfDownloadThreads;

	private const int NumberOfPrecachedSegmentsPerThread = 16;

	private const int NumberOfPrecachedSegmentsPerThreadToIncrease = 8;

	private static int _numberOfPrecachedSegmentsToAddMax;

	private static int _nextQueueId;

	private static readonly List<NNTPInput> FirstPriorityFiles = new List<NNTPInput>();

	private static bool UseSlowDecoder;

	private static bool IsCachingEnabled
	{
		get
		{
			if (CachingSystem.IsEnabled)
			{
				if (!AppHelper.IsSnelNlProvider)
				{
					return AppHelper.Is5EuroProvider;
				}
				return true;
			}
			return false;
		}
	}

	private static bool IsServerUnderTimeout => _serverStartToProcessDateTime > DateTime.Now;

	internal static void StartDownloadQueue()
	{
		lock (LockStartStop)
		{
			if (IsQueueStarted)
			{
				return;
			}
			IsQueueStarted = true;
			_numberOfPrecachedSegmentsToAddMax = 8;
			bool isCachingEnabled = IsCachingEnabled;
			_numberOfDownloadThreads = Math.Max(AppHelper.ServersDb.ODown.Connections, 2);
			_cTokenSource = new CancellationTokenSource();
			_segmentsUnderTimeoutQueue = new ConcurrentQueue<NNTPSegment>();
			_segmentsToProcessStack = new ConcurrentStack<NNTPSegment>();
			_segmentsToDownload = new SynchronizedList<BlockingCollection<NNTPSegment>>();
			SpotDownloader.UpdatePriorities();
			StartDownloadSegmentsAsync();
			bool isOnPause = false;
			if (isCachingEnabled)
			{
				CachingSystem.DoesSlaveUseSslCalculated = false;
				_segmentsToGetSlave = new BlockingCollection<NNTPSegment>(200);
				StartGettingCacheSlavesAsync();
			}
			Log.Debug("Number of download threads: " + _numberOfDownloadThreads);
			Task.Run(delegate
			{
				int num = 1;
				try
				{
					while (!_cTokenSource.IsCancellationRequested)
					{
						while (!IsDownloaderActiveTime())
						{
							if (!isOnPause)
							{
								Log.Debug("Download queue is going to pause state due to downloader schedule");
								isOnPause = true;
							}
							Task.Delay(TimeSpan.FromMinutes(1.0), _cTokenSource.Token).Wait();
						}
						if (isOnPause)
						{
							Log.Debug("Download queue started after schedule pause");
							isOnPause = false;
						}
						num = 2;
						NNTPSegment result = null;
						bool flag = false;
						lock (LockDownloadQueueReordering)
						{
							if (_isDownloadQueueReordered)
							{
								ClearQueue();
								_isDownloadQueueReordered = false;
							}
							num = 3;
							if (_segmentsUnderTimeoutQueue.TryPeek(out var result2) && !result2.IsUnderTimeout)
							{
								_segmentsUnderTimeoutQueue.TryDequeue(out result);
							}
							num = 4;
							if (result == null && !_segmentsToProcessStack.TryPop(out result))
							{
								EmptyQueueEvent.Reset();
								if (!_segmentsToProcessStack.Any())
								{
									flag = true;
								}
							}
						}
						if (result == null)
						{
							if (flag)
							{
								if (_segmentsUnderTimeoutQueue.Any())
								{
									Thread.Sleep(200);
								}
								else
								{
									EmptyQueueEvent.Wait(_cTokenSource.Token);
								}
							}
						}
						else
						{
							num = 5;
							if (!result.IsDownloadScheduled)
							{
								bool flag2 = false;
								try
								{
									result.IsDownloadScheduled = true;
									num = 6;
									if (isCachingEnabled)
									{
										_segmentsToGetSlave.Add(result, _cTokenSource.Token);
									}
									else
									{
										AddToDownloadQueue(new List<NNTPSegment> { result });
									}
									flag2 = true;
								}
								finally
								{
									if (!flag2)
									{
										result.IsDownloadScheduled = false;
									}
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					if (!Sys.IsShutdownRequested && !_cTokenSource.IsCancellationRequested)
					{
						Log.Error("Download queue crashed with exception, step: " + num);
						Log.Exception(ex);
						Log.Debug("Restart download queue in 5 sec");
						Thread.Sleep(5000);
						Sys.Downloader.RestartProcessAsync();
					}
				}
				finally
				{
					if (!Sys.IsShutdownRequested)
					{
						Log.Debug("Download queue stopped");
					}
				}
			});
		}
	}

	private static void ClearQueue()
	{
		ClearGetSlavesQueue();
		ClearDownloadQueue();
	}

	private static void ClearGetSlavesQueue()
	{
		if (!IsCachingEnabled)
		{
			return;
		}
		while (_segmentsToGetSlave.Any())
		{
			if (_segmentsToGetSlave.TryTake(out var item))
			{
				item.IsDownloadScheduled = false;
			}
		}
	}

	private static void ClearDownloadQueue()
	{
		foreach (BlockingCollection<NNTPSegment> item2 in _segmentsToDownload)
		{
			while (item2.Any())
			{
				if (item2.TryTake(out var item))
				{
					item.IsDownloadScheduled = false;
				}
			}
		}
	}

	private static void AddToDownloadQueue(List<NNTPSegment> list)
	{
		if (_cTokenSource.IsCancellationRequested || !list.Any())
		{
			return;
		}
		string value = "";
		List<NNTPSegment> list2 = new List<NNTPSegment>();
		foreach (NNTPSegment item in list)
		{
			if (_cTokenSource.IsCancellationRequested)
			{
				return;
			}
			if (!item.SlaveHostname.Equals(value) || list2.Count >= _numberOfPrecachedSegmentsToAddMax)
			{
				AddToDownloadQueueShort(list2);
				list2.Clear();
			}
			list2.Add(item);
			value = item.SlaveHostname;
		}
		AddToDownloadQueueShort(list2);
	}

	private static void AddToDownloadQueueShort(List<NNTPSegment> list)
	{
		if (!list.Any())
		{
			return;
		}
		BlockingCollection<NNTPSegment> suitableCollection = GetSuitableCollection();
		foreach (NNTPSegment item in list)
		{
			suitableCollection.Add(item, _cTokenSource.Token);
		}
	}

	private static BlockingCollection<NNTPSegment> GetSuitableCollection()
	{
		while (!_cTokenSource.IsCancellationRequested)
		{
			for (int i = 0; i < _numberOfDownloadThreads; i++)
			{
				if (_nextQueueId >= _numberOfDownloadThreads)
				{
					_nextQueueId = 0;
				}
				BlockingCollection<NNTPSegment> blockingCollection = _segmentsToDownload[_nextQueueId++];
				if (blockingCollection.Count <= 8)
				{
					return blockingCollection;
				}
				if (_cTokenSource.IsCancellationRequested)
				{
					return null;
				}
			}
			Thread.Sleep(10);
		}
		_cTokenSource.Token.ThrowIfCancellationRequested();
		return null;
	}

	private static void StartGettingCacheSlavesAsync()
	{
		Task.Factory.StartNew(delegate
		{
			List<NNTPSegment> list = new List<NNTPSegment>(100);
			try
			{
				Stopwatch stopwatch = new Stopwatch();
				int num = 500;
				while (!_cTokenSource.IsCancellationRequested)
				{
					stopwatch.Restart();
					if (_segmentsToGetSlave.TryTake(out var item, num, _cTokenSource.Token))
					{
						list.Add(item);
					}
					if (_cTokenSource.IsCancellationRequested)
					{
						break;
					}
					if (list.Count > 0)
					{
						if (_isDownloadQueueReordered)
						{
							foreach (NNTPSegment item2 in list)
							{
								item2.IsDownloadScheduled = false;
							}
							list.Clear();
						}
						else if (stopwatch.ElapsedMilliseconds >= num || list.Count >= 100)
						{
							CachingSystem.GetSlaves(list);
							if (_cTokenSource.IsCancellationRequested)
							{
								break;
							}
							if (_isDownloadQueueReordered)
							{
								foreach (NNTPSegment item3 in list)
								{
									item3.IsDownloadScheduled = false;
								}
								list.Clear();
							}
							else
							{
								if (!CachingSystem.IsBridgedModeOn)
								{
									list.Sort((NNTPSegment x, NNTPSegment y) => string.Compare(x.SlaveHostname, y.SlaveHostname, StringComparison.Ordinal));
								}
								AddToDownloadQueue(list);
								list.Clear();
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!_cTokenSource.IsCancellationRequested)
				{
					Log.Exception(ex);
				}
			}
			finally
			{
				if (!Sys.IsShutdownRequested)
				{
					foreach (NNTPSegment item4 in list)
					{
						item4.IsDownloadScheduled = false;
					}
					Log.Debug("Exit getting slaves");
				}
			}
		}, TaskCreationOptions.LongRunning);
	}

	private static bool IsDownloaderActiveTime()
	{
		if (!Settings.Default.DownloaderSchedule)
		{
			return true;
		}
		TimeSpan timeOfDay = DateTime.Now.TimeOfDay;
		TimeSpan timeOfDay2 = Settings.Default.DownloaderStartTime.TimeOfDay;
		TimeSpan timeOfDay3 = Settings.Default.DownloaderEndTime.TimeOfDay;
		if (timeOfDay2 == timeOfDay3)
		{
			return true;
		}
		if (timeOfDay2 < timeOfDay3)
		{
			if (timeOfDay2 < timeOfDay)
			{
				return timeOfDay < timeOfDay3;
			}
			return false;
		}
		if (!(timeOfDay3 < timeOfDay))
		{
			return timeOfDay < timeOfDay2;
		}
		return true;
	}

	private static void DownloadSegmentOneThreadBody(BlockingCollection<NNTPSegment> collection)
	{
		try
		{
			while (!_cTokenSource.IsCancellationRequested)
			{
				WaitForTheServerTimeout();
				NNTPSegment nNTPSegment = collection.Take(_cTokenSource.Token);
				if (_isDownloadQueueReordered)
				{
					nNTPSegment.IsDownloadScheduled = false;
					continue;
				}
				if (nNTPSegment.IsDataAvailable || nNTPSegment.IsSaved || nNTPSegment.IsFailed || (nNTPSegment.File.DownloaderItem.RawStatus != DownloadStatus.Queued && nNTPSegment.File.DownloaderItem.RawStatus != DownloadStatus.Downloading && nNTPSegment.File.DownloaderItem.RawStatus != DownloadStatus.Par2PieceDownloading))
				{
					nNTPSegment.IsDownloadScheduled = false;
					continue;
				}
				nNTPSegment.RetriesLeft--;
				if (nNTPSegment.RetriesLeft < 0)
				{
					nNTPSegment.MarkAsFailed();
					nNTPSegment.IsDownloadScheduled = false;
					continue;
				}
				try
				{
					DownloadSegment(nNTPSegment);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					nNTPSegment.LastError = "Failed to download segment: " + ex.Message;
				}
				finally
				{
					nNTPSegment.IsDownloadScheduled = false;
					if (_cTokenSource.IsCancellationRequested)
					{
						nNTPSegment.RetriesLeft++;
					}
					if (!nNTPSegment.IsDownloaded)
					{
						PutSegmentBackToDownload(nNTPSegment);
					}
				}
			}
		}
		catch (Exception ex2)
		{
			if (!_cTokenSource.IsCancellationRequested)
			{
				Log.Exception(ex2);
			}
		}
		finally
		{
			while (collection.Any())
			{
				if (collection.TryTake(out var item))
				{
					item.IsDownloadScheduled = false;
				}
			}
		}
	}

	internal static void PutSegmentBackToDownload(NNTPSegment segment)
	{
		DownloadStatus rawStatus = segment.File.DownloaderItem.RawStatus;
		if (!segment.IsFailed && !Sys.IsShutdownRequested && !segment.File.IsDisposed && rawStatus != DownloadStatus.Deleted && rawStatus != DownloadStatus.Pausing && rawStatus != DownloadStatus.Paused && rawStatus != DownloadStatus.Failure && rawStatus != DownloadStatus.FailureNoSuchArticle)
		{
			if (segment.IsUnderTimeout)
			{
				_segmentsUnderTimeoutQueue.Enqueue(segment);
			}
			else
			{
				_segmentsToProcessStack.Push(segment);
			}
			EmptyQueueEvent.Set();
		}
	}

	private static void StartDownloadSegmentsAsync(int numberOfThreads = -1)
	{
		if (numberOfThreads < 0)
		{
			numberOfThreads = _numberOfDownloadThreads;
		}
		_nextQueueId = 0;
		for (int i = 0; i < numberOfThreads; i++)
		{
			BlockingCollection<NNTPSegment> collection = new BlockingCollection<NNTPSegment>(16);
			_segmentsToDownload.Add(collection);
			Task task = Task.Factory.StartNew(delegate
			{
				DownloadSegmentOneThreadBody(collection);
			}, TaskCreationOptions.LongRunning);
			DownloadTasksList.Add(task);
			task.ContinueWith(delegate
			{
				_segmentsToDownload.Remove(collection);
				DownloadTasksList.Remove(task);
				OneDownloadTaskCompleted.Set();
			});
		}
	}

	public static void ChangeDownloadThreadsNumber(int newNumber)
	{
		if (_numberOfDownloadThreads != newNumber && newNumber > _numberOfDownloadThreads)
		{
			StartDownloadSegmentsAsync(newNumber - _numberOfDownloadThreads);
			_numberOfDownloadThreads = newNumber;
		}
	}

	public static bool StopDownloadQueue()
	{
		if (Sys.IsShutdownRequested)
		{
			_cTokenSource.Cancel();
			return true;
		}
		lock (LockStartStop)
		{
			if (!IsQueueStarted)
			{
				return true;
			}
			IsQueueStarted = false;
			_cTokenSource.Cancel();
			DateTime now = DateTime.Now;
			while (DateTime.Now - now < PausingTimeout)
			{
				OneDownloadTaskCompleted.Reset();
				try
				{
					if (!DownloadTasksList.Any())
					{
						break;
					}
				}
				catch (InvalidOperationException)
				{
				}
				OneDownloadTaskCompleted.Wait(now + PausingTimeout - DateTime.Now);
			}
			ClearQueue();
			if (DownloadTasksList.Any() && DateTime.Now - now >= PausingTimeout)
			{
				Log.Error("Pausing timeout reached");
				return false;
			}
			return true;
		}
	}

	public static void UpdateDownloadQueue(List<DownloaderItemViewModel> downloadItems, List<NNTPInput> firstPriorityFiles = null)
	{
		_updatePrioritiesOneMoreTime = true;
		if (!Monitor.TryEnter(LockUpdateDownloadQueue))
		{
			return;
		}
		try
		{
			while (_updatePrioritiesOneMoreTime)
			{
				_updatePrioritiesOneMoreTime = false;
				ConcurrentStack<NNTPSegment> value = new ConcurrentStack<NNTPSegment>(GenerateSegmentsList((from d in downloadItems
					where d.IsForDownloadQueue
					select d into item
					orderby item.Index
					select item).Take(2).Cast<SpotnetDownloaderItemViewModel>(), firstPriorityFiles));
				lock (LockDownloadQueueReordering)
				{
					Interlocked.Exchange(ref _segmentsToProcessStack, value);
					_isDownloadQueueReordered = true;
					if (_segmentsToProcessStack.Any())
					{
						EmptyQueueEvent.Set();
					}
				}
			}
		}
		finally
		{
			Monitor.Exit(LockUpdateDownloadQueue);
		}
	}

	private static IEnumerable<NNTPSegment> GenerateSegmentsList(IEnumerable<SpotnetDownloaderItemViewModel> downloaderItems, List<NNTPInput> newFirstPriorityFiles = null)
	{
		foreach (SpotnetDownloaderItemViewModel item in downloaderItems.AsEnumerable().Reverse())
		{
			foreach (NNTPInput item2 in item.FilesToDownloadNoParPieces.AsEnumerable().Reverse())
			{
				if (item2.IsAllSegmentsDataReceived)
				{
					continue;
				}
				foreach (NNTPSegment item3 in item2.Segments.AsEnumerable().Reverse())
				{
					if (!item3.IsDownloaded && !item3.IsFailed && !item3.IsSaved && !item3.IsDataAvailable)
					{
						yield return item3;
					}
				}
			}
		}
		if (newFirstPriorityFiles != null)
		{
			FirstPriorityFiles.AddRange(newFirstPriorityFiles.AsEnumerable().Reverse());
		}
		FirstPriorityFiles.RemoveAll((NNTPInput f) => f.IsAllSegmentsDataReceived);
		foreach (NNTPInput firstPriorityFile in FirstPriorityFiles)
		{
			foreach (NNTPSegment item4 in firstPriorityFile.Segments.AsEnumerable().Reverse())
			{
				if (!item4.IsDownloaded && !item4.IsFailed && !item4.IsSaved && !item4.IsDataAvailable)
				{
					yield return item4;
				}
			}
		}
	}

	private static void SetServerTimeout(TimeSpan timeout)
	{
		Log.Debug("Pause {0} server for {1} seconds", AppHelper.ServersDb.ODown.Server, timeout.TotalSeconds);
		_serverStartToProcessDateTime = DateTime.Now + timeout;
		AppHelper.ClearDownloadPhuse();
	}

	private static void WaitForTheServerTimeout()
	{
		TimeSpan timeSpan = _serverStartToProcessDateTime - DateTime.Now;
		if (timeSpan > TimeSpan.Zero)
		{
			try
			{
				Task.Delay(timeSpan).Wait(_cTokenSource.Token);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private static void DownloadSegment(NNTPSegment segment)
	{
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(AppHelper.DownloadPhuse);
		foreach (string group in segment.File.Groups)
		{
			if (_cTokenSource.IsCancellationRequested)
			{
				return;
			}
			if (ListOfGroupsNotFound.Contains(group))
			{
				continue;
			}
			Stream resp;
			int resCode;
			string errorMsg;
			bool flag = nNTP.GetBodyFromCacheFirst(group, segment, out resp, out resCode, out errorMsg);
			if (_cTokenSource.IsCancellationRequested || Sys.IsShutdownRequested || segment.File.DownloaderItem.RawStatus == DownloadStatus.Deleted)
			{
				return;
			}
			if (flag)
			{
				if (resp.Length < 10)
				{
					flag = false;
					resCode = 1001;
				}
				else if (Module.GetString(resp, resp.Length - 3, 3L) != ".\r\n")
				{
					flag = false;
					resCode = 1002;
				}
			}
			if (flag)
			{
				segment.ActualDataReceivedLength = resp.Length;
				segment.IsDownloaded = true;
				if (UseSlowDecoder)
				{
					DownloaderDataDecoder.DecodeAsync(segment, resp);
					break;
				}
				try
				{
					DownloaderDataDecoderCpuOptimized.DecodeAsync(segment, resp);
				}
				catch (Exception e)
				{
					Log.Error("Failed to load fast SpotnetDecoder: " + e.TheMostInnerException().Message + ". So use slow decoder as a workaround.");
					UseSlowDecoder = true;
					DownloaderDataDecoder.DecodeAsync(segment, resp);
				}
				break;
			}
			if (!ProcessError(segment, resCode, errorMsg, group))
			{
				break;
			}
		}
		if (segment.IsUnderTimeout && segment.RetriesLeft == 0 && !segment.IsDownloaded)
		{
			segment.MarkAsFailed();
		}
	}

	public static bool ProcessError(NNTPSegment segment, int code, string errorMsg, string group)
	{
		if (IsServerUnderTimeout)
		{
			segment.RetriesLeft++;
			return false;
		}
		switch (code)
		{
		case 381:
		case 400:
		case 450:
		case 452:
		case 480:
		case 481:
		case 482:
		case 502:
			if (errorMsg.Contains("connection"))
			{
				Log.Debug(errorMsg);
			}
			else
			{
				segment.LastError = Words.UsernamePasswordWrong + ". From the server: " + errorMsg;
				SetServerTimeout(TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec * 5));
			}
			segment.RetriesLeft++;
			return false;
		case 411:
			segment.LastError = Words.GroupNotFound + ". From the server: " + errorMsg + ". Group: " + group;
			ListOfGroupsNotFound.Add(group);
			return true;
		case 941:
			segment.LastError = Words.HostIsUnknown + ". From the server: " + errorMsg;
			SetServerTimeout(TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec * 5));
			segment.RetriesLeft++;
			return false;
		case 423:
		case 430:
			segment.LastError = errorMsg;
			segment.RetriesLeft = 0;
			return false;
		case 931:
		case 950:
		case 952:
		case 995:
			segment.LastError = Words.TimeoutOccured + ". From the server: " + errorMsg;
			SetServerTimeout(TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec));
			segment.RetriesLeft++;
			return false;
		case 1001:
			segment.LastError = "No binary bytes received. Group: " + group;
			segment.SetTimeout();
			return true;
		case 1002:
			segment.LastError = "Binary bytes received have no dot at the end. Group: " + group;
			segment.SetTimeout();
			return true;
		default:
			segment.LastError = errorMsg;
			segment.SetTimeout();
			return false;
		}
	}
}
