using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Xml;
using NLog;
using Spotnet.Downloader.PostProcessing;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Downloader.ViewModel;

public class SpotnetDownloaderItemViewModel : DownloaderItemViewModel
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object LockSaveTheState = new object();

	private readonly SpotDownloader _spotDownloader;

	private double _bpsMultiplier;

	private readonly PreUnpack _preUnpackInstance = new PreUnpack();

	private HealthChecker _healthChecker;

	private long _lastBps;

	private double _lastBpsMultiplier;

	private System.Timers.Timer _timerUpdateDownloadSpeedAndTime;

	private System.Timers.Timer _timerToSaveTheState;

	private List<NNTPInput> _filesToDownload;

	private readonly object _lockPathToNzb = new object();

	private readonly object _lockPostProcess = new object();

	private readonly CancellationTokenSource _cTokenForPostProcess = new CancellationTokenSource();

	public bool IsDataRestoreInProgress;

	private int _downloadScheduledCount;

	private readonly AverageSpeedCalculator _speedCalculator = new AverageSpeedCalculator();

	public override string Tooltip
	{
		get
		{
			if (base.PercInt >= 0)
			{
				if (base.RawStatus != DownloadStatus.Repairing)
				{
					return $"{Words.ColumnProgress}: {base.PercInt}%. {Words.Health}: {HealthLevelPerc}% (min {HealthThresholdPerc}%)";
				}
				return $"{Words.ColumnProgress}: {base.PercInt}%";
			}
			return Words.InProgress;
		}
	}

	public override Visibility VisibilityOfStatusWarningIcon
	{
		get
		{
			if (!IsStatusWarningIconVisible)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public override bool IsStatusWarningIconVisible => !StatusWarningText.IsNullOrEmpty();

	public override string StatusWarningText
	{
		get
		{
			if (base.RawStatus == DownloadStatus.FailureNoSuchArticle)
			{
				if (!AppHelper.Is5EuroProvider && !AppHelper.IsSnelNlProvider)
				{
					return Words.StatFailedNoSuchArticle;
				}
				return LogQueue.LastFatal;
			}
			if (base.RawStatus == DownloadStatus.Failure)
			{
				return LogQueue.LastFatal;
			}
			if (base.RawStatus == DownloadStatus.Warning)
			{
				return LogQueue.LastWarning;
			}
			return null;
		}
	}

	public string LogPath => LogQueue.LogPath;

	public LogQueue LogQueue { get; }

	public List<NNTPInput> FilesToDownload
	{
		get
		{
			if (base.PathToNzb == null || !System.IO.File.Exists(base.PathToNzb))
			{
				return new List<NNTPInput>();
			}
			if (_filesToDownload == null)
			{
				lock (_lockPathToNzb)
				{
					if (_filesToDownload == null)
					{
						List<NNTPInput> list;
						using (FileStream xXml = System.IO.File.OpenRead(base.PathToNzb))
						{
							NNTPInputPriorityComparer comparer = new NNTPInputPriorityComparer();
							list = ParseNzb(xXml).OrderBy((NNTPInput f) => f, comparer).ToList();
							for (int i = 0; i < list.Count; i++)
							{
								list[i].Index = i + 1;
							}
						}
						if (!list.Any())
						{
							LogQueue.Fatal("Failed to parse NZB file: " + base.PathToNzb);
							base.RawStatus = DownloadStatus.Deleted;
							Sys.Downloader.RemoveItemsAsync(new SpotnetDownloaderItemViewModel[1] { this });
							return new List<NNTPInput>();
						}
						FixDuplicateFilenames(list);
						_healthChecker = new HealthChecker(list);
						NotifyPropertyChanged("HealthLevelPerc");
						NotifyPropertyChanged("HealthThresholdPerc");
						Thread.MemoryBarrier();
						_filesToDownload = list;
					}
				}
			}
			return _filesToDownload;
		}
	}

	public List<NNTPInput> FilesToDownloadNoParPieces
	{
		get
		{
			if (!Settings.Default.RemovePar2FilesAfterDownload)
			{
				return FilesToDownload;
			}
			return FilesToDownload.Where((NNTPInput f) => !f.IsParPiece).ToList();
		}
	}

	public List<NNTPInput> FilesToDownloadParPieces
	{
		get
		{
			if (!Settings.Default.RemovePar2FilesAfterDownload)
			{
				return new List<NNTPInput>();
			}
			return FilesToDownload.Where((NNTPInput f) => f.IsParPiece).ToList();
		}
	}

	public int HealthThresholdPerc
	{
		get
		{
			if (_healthChecker == null)
			{
				return 0;
			}
			return (int)(_healthChecker.HealthThreshold * 100.0);
		}
	}

	public int HealthLevelPerc
	{
		get
		{
			if (_healthChecker == null)
			{
				return 0;
			}
			return (int)(_healthChecker.HealthLevel * 100.0);
		}
	}

	public bool IsHealthThresholdReached => _healthChecker?.IsHealthThresholdReached() ?? false;

	public bool IsFilesToDownloadInitialized => _filesToDownload != null;

	private bool IsDownloadScheduled => _downloadScheduledCount > 0;

	public SpotnetDownloaderItemViewModel(int id, string title, DownloadStatus status, int perc, double sizeMegaBytes, int secondsLeft, int index, string incompleteDir, string completeDir, string speed, string messageId, int category, string pathToNzb, long added, long finished)
	{
		base.PropertyChanged += delegate(object sender, PropertyChangedEventArgs info)
		{
			switch (info.PropertyName)
			{
			case "Titel":
			case "Index":
			case "Status":
			case "CompleteDir":
			case "IncompleteDir":
			case "MessageId":
				ScheduleTimerToSaveTheState();
				break;
			}
		};
		base.OnPathToNzbChanged += delegate(string s)
		{
			_filesToDownload = null;
			if (!s.IsNullOrEmpty())
			{
				StatsUpdateSizeTotal();
				RestoreStateOfFiles();
				InitializeDownloadProgress();
			}
		};
		base.OnSchedulePlayOrPause += delegate
		{
			if (!base.IsHistory)
			{
				if (StartPreUnpack())
				{
					base.IsPlayScheduled = true;
					if (!base.IsPaused)
					{
						Sys.Downloader.MoveTop(this);
					}
				}
				else
				{
					LogQueue.Warn("Failed to start pre-unpack");
				}
			}
			else
			{
				base.IsPlayScheduled = true;
			}
		};
		base.OnStatusChanged += delegate(bool isDownloadingBefore, bool isHistoryBefore)
		{
			if (base.IsHistory && !isHistoryBefore)
			{
				base.Index = Sys.Downloader.GetNewHistoryIndex(base.Index);
			}
			if (isDownloadingBefore)
			{
				DownloaderDataStorer.WaitForAllCurrentItemsSave();
				FilesToDownload.ForEach(DownloaderDataStorer.CloseFileStream);
			}
			if (!base.IsDownloading && !base.IsPausing)
			{
				base.Speed = "";
				base.SecondsLeft = 0;
				_speedCalculator.Reset();
			}
		};
		base.OnItemRemove += OnRemove;
		if (id < 0)
		{
			id = Sys.Downloader.Items.GetNewId(id);
			while (!CleanOldQueueFiles(id))
			{
				id++;
			}
		}
		LogQueue = new LogQueue(id);
		Initialize(id, title, status, perc, sizeMegaBytes, secondsLeft, index, incompleteDir, completeDir, speed, messageId, category, pathToNzb, added, finished);
		if (base.IsTotals || base.RawStatus == DownloadStatus.Deleted || Settings.Default.DownloadAction > 1)
		{
			return;
		}
		if (!base.IsHistory)
		{
			_preUnpackInstance.Initialize(this);
			_spotDownloader = new SpotDownloader(this);
			_timerToSaveTheState = new System.Timers.Timer(500.0)
			{
				AutoReset = false
			};
			_timerToSaveTheState.Elapsed += delegate
			{
				SaveTheState();
			};
			_timerUpdateDownloadSpeedAndTime = new System.Timers.Timer(1000.0)
			{
				AutoReset = true
			};
			_timerUpdateDownloadSpeedAndTime.Elapsed += delegate
			{
				if (base.IsHistory)
				{
					_timerUpdateDownloadSpeedAndTime.Stop();
				}
				base.BytesPerSecond = _speedCalculator.GetBps();
				base.Speed = AverageSpeedCalculator.BpsToString(base.BytesPerSecond);
				if (base.BytesPerSecond < 10)
				{
					base.SecondsLeft = 0;
				}
				else
				{
					double num = ((base.RawStatus == DownloadStatus.Par2PieceDownloading) ? base.SizeOfPar2MegaBytes : base.SizeMegaBytes);
					double num2 = 1.0 * base.Perc * num / 100.0;
					double num3 = num - num2;
					if (Math.Abs(_lastBps - base.BytesPerSecond) > base.BytesPerSecond / 10)
					{
						_lastBps = base.BytesPerSecond;
						_lastBpsMultiplier = _bpsMultiplier;
					}
					double num4 = num3 * 1024.0 * 1024.0 / ((double)_lastBps / _lastBpsMultiplier);
					base.SecondsLeft = (int)num4;
				}
				_timerUpdateDownloadSpeedAndTime.Start();
			};
			_timerUpdateDownloadSpeedAndTime.Start();
		}
		Sys.Downloader.ItemsOrderChanged += DownloaderOnItemsOrderChanged;
	}

	private bool CleanOldQueueFiles(int id)
	{
		IEnumerable<System.IO.FileInfo> enumerable = new System.IO.DirectoryInfo(DownloaderProps.QueueDir).EnumerateFiles();
		Regex regex = new Regex(string.Format("^{0}.snet|^{0}_((\\d)+).snet|^{0}.nzb|^{0}.log", id));
		foreach (System.IO.FileInfo item in enumerable)
		{
			if (regex.Match(item.Name).Success)
			{
				string text = "";
				try
				{
					Log.Debug("Removing old queue file: " + item.Name);
					item.Delete();
				}
				catch (Exception ex)
				{
					text = ex.Message;
				}
				if (item.Exists)
				{
					Log.Debug("Failed to remove " + item.Name + ". " + text);
					return false;
				}
			}
		}
		return true;
	}

	public override void DownloadResume()
	{
		if (!base.IsHistory)
		{
			base.RawStatus = DownloadStatus.Queued;
			CheckForPostProcessAsync();
		}
	}

	public override void DownloadPause()
	{
		if (!base.IsHistory)
		{
			base.RawStatus = DownloadStatus.Pausing;
			CheckForPostProcessAsync();
		}
	}

	private void SegmentOnFailedChanged(NNTPSegment segment, bool isFailed)
	{
		if (base.RawStatus == DownloadStatus.Deleted)
		{
			return;
		}
		if (isFailed)
		{
			StatsUpdateProgressOnBytesDownloaded(segment);
			if (_healthChecker != null && _healthChecker.IsHealthThresholdReached(segment))
			{
				LogQueue.Fatal("Health level reached critical threshold: " + HealthThresholdPerc + ". Too many failed segments.");
				base.RawStatus = ((segment.LastError != null && segment.LastError.StartsWith("430 ")) ? DownloadStatus.FailureNoSuchArticle : DownloadStatus.Failure);
				NotifyPropertyChanged("IsHealthThresholdReached");
			}
			NotifyPropertyChanged("HealthLevelPerc");
		}
		else
		{
			_healthChecker.RemoveFromFailed(segment);
		}
	}

	public bool StartPreUnpack()
	{
		if (!base.IsHistory)
		{
			_preUnpackInstance.RunAsync();
		}
		return true;
	}

	private void OnRemove()
	{
		_preUnpackInstance.Stop();
		if (_timerToSaveTheState != null)
		{
			_timerToSaveTheState.Dispose();
			_timerToSaveTheState = null;
		}
		if (_timerUpdateDownloadSpeedAndTime != null)
		{
			_timerUpdateDownloadSpeedAndTime.Dispose();
			_timerUpdateDownloadSpeedAndTime = null;
		}
		Sys.Downloader.ItemsOrderChanged -= DownloaderOnItemsOrderChanged;
		_spotDownloader?.Dispose();
		_cTokenForPostProcess.Cancel();
		_preUnpackInstance.Wait();
		DownloaderDataStorer.WaitForAllCurrentItemsSave();
		FilesToDownload.ForEach(delegate(NNTPInput f)
		{
			f.Dispose();
			DownloaderDataStorer.CloseFileStream(f);
		});
		RemoveSavedState();
	}

	private void DownloaderOnItemsOrderChanged()
	{
		NotifyPropertyChanged("Priority");
	}

	public void PreUnpackWaitForFinish()
	{
		while (_preUnpackInstance.IsPreUnpackRunning && _preUnpackInstance.SegmentsInQueueCount != 0)
		{
			Thread.Sleep(100);
		}
	}

	public void PreUnpackStopAndWait()
	{
		_preUnpackInstance.Stop();
		_preUnpackInstance.Wait();
	}

	public void SaveTheState()
	{
		lock (LockSaveTheState)
		{
			string filenameOfNzbFile = GetFilenameOfNzbFile(ID);
			if (base.PathToNzb != null && System.IO.File.Exists(base.PathToNzb) && !System.IO.File.Exists(filenameOfNzbFile))
			{
				try
				{
					System.IO.File.Copy(base.PathToNzb, filenameOfNzbFile);
					_pathToNzb = filenameOfNzbFile;
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}
			Dictionary<string, string> dict = new Dictionary<string, string>
			{
				{ "Title", base.Titel },
				{
					"Index",
					base.Index.ToString()
				},
				{
					"Status",
					base.RawStatus.ToString()
				},
				{ "Complete", base.CompleteDir },
				{ "Incomplete", base.IncompleteDir },
				{ "LastWarning", LogQueue.LastWarning },
				{ "LastFatal", LogQueue.LastFatal },
				{ "Nzb", base.PathToNzb },
				{ "MessageId", base.MessageId },
				{
					"Category",
					base.Category.ToString()
				},
				{
					"Added",
					base.AddedUnixTime.ToString()
				},
				{
					"Finished",
					base.FinishedUnixTime.ToString()
				}
			};
			string filenameOfStateFile = GetFilenameOfStateFile(ID);
			AppHelper.SerializeDict(dict, filenameOfStateFile);
		}
	}

	private void InitializeDownloadProgress()
	{
		int num = 0;
		foreach (NNTPInput item in _filesToDownload.Where((NNTPInput f) => f.IsParPiece))
		{
			foreach (NNTPSegment segment in item.Segments)
			{
				num += segment.ExpectedSizeFromNzbFile;
			}
		}
		StatsUpdateProgress(base.Perc + 1.0 * (double)num / base.SizeMegaBytes * 100.0 / 1024.0 / 1024.0);
	}

	internal List<NNTPInput> ParseNzb(Stream xXml)
	{
		List<NNTPInput> list = new List<NNTPInput>();
		try
		{
			XmlReader xmlReader = XmlReader.Create(xXml, Module.ReaderSettings);
			int num = 0;
			while (xmlReader.ReadToFollowing("file"))
			{
				num++;
				NNTPInput nNTPInput = ParseSegments(xmlReader.ReadSubtree(), xmlReader.GetAttribute("subject"), num);
				if (nNTPInput == null)
				{
					return list;
				}
				list.Add(nNTPInput);
			}
		}
		catch (Exception ex)
		{
			string text = Words.ErrorWhileParsing + " " + ex.Message;
			LogQueue.Fatal(text);
			AppHelper.Error(text);
		}
		return list;
	}

	private NNTPInput ParseSegments(XmlReader reader, string subject, int fileIndex)
	{
		try
		{
			NNTPInput nNTPInput = new NNTPInput(this, subject, fileIndex);
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(reader);
			foreach (XmlElement item in xmlDocument.GetElementsByTagName("group"))
			{
				if (!item.InnerText.IsNullOrEmpty())
				{
					nNTPInput.Groups.Add(item.InnerText);
				}
			}
			if (nNTPInput.Groups == null || !nNTPInput.Groups.Any())
			{
				LogQueue.Fatal("Parse NZB: Groups section is empty");
				return null;
			}
			foreach (XmlNode item2 in xmlDocument.GetElementsByTagName("segment"))
			{
				if (item2.Attributes == null)
				{
					LogQueue.Fatal("Parse NZB: Invalid xml. No attributes.");
					return null;
				}
				int result = 0;
				int result2 = 0;
				foreach (XmlAttribute attribute in item2.Attributes)
				{
					if (attribute.Name.EqualsIgnoreCase("bytes"))
					{
						int.TryParse(attribute.InnerText, out result);
					}
					else if (attribute.Name.EqualsIgnoreCase("number"))
					{
						int.TryParse(attribute.InnerText, out result2);
					}
				}
				string innerText = item2.InnerText;
				if (innerText.Length < 1)
				{
					LogQueue.Fatal("Parse NZB: messageId is wrong");
					return null;
				}
				if (result2 > 0 && result > 0)
				{
					NNTPSegment nNTPSegment = new NNTPSegment(result2, result, innerText, nNTPInput);
					nNTPSegment.IsDownloadScheduledChanged += SegmentOnDownloadScheduledChanged;
					nNTPSegment.SavedChanged += SegmentOnSavedChanged;
					nNTPSegment.DownloadedChanged += SegmentOnDownloadedChanged;
					nNTPSegment.FailedChanged += SegmentOnFailedChanged;
					nNTPSegment.DataAvailableChanged += SegmentOnDataAvailableChanged;
					nNTPInput.AddNewSegment(nNTPSegment);
					continue;
				}
				if (result2 <= 0)
				{
					LogQueue.Warn("Failed to parse 'number' field for " + innerText + " / " + subject + ". So it will not be downloaded.");
				}
				if (result <= 0)
				{
					LogQueue.Warn("Failed to parse 'bytes' field for " + innerText + " / " + subject + ". So it will not be downloaded.");
				}
			}
			if (!nNTPInput.Segments.Any())
			{
				LogQueue.Fatal("Parse NZB. No segments found for the file: " + nNTPInput.Filename);
			}
			nNTPInput.Segments.Sort(Comparer<NNTPSegment>.Create((NNTPSegment a, NNTPSegment b) => a.Index.CompareTo(b.Index)));
			return nNTPInput;
		}
		catch (Exception ex)
		{
			LogQueue.Fatal("Failed to parse nzb file: " + ex.Message);
		}
		return null;
	}

	private void FixDuplicateFilenames(List<NNTPInput> list)
	{
		foreach (NNTPInput file in list)
		{
			while (list.Count((NNTPInput f) => f.Filename.Equals(file.Filename)) > 1)
			{
				file.Filename = NNTPInput.UpdateFilenameDuplicatePart(file.Filename);
			}
		}
	}

	private Task CheckForPostProcessAsync()
	{
		return Task.Run(delegate
		{
			lock (_lockPostProcess)
			{
				if (base.RawStatus != DownloadStatus.Deleted && !base.IsHistory && !base.IsPaused && FilesToDownloadNoParPieces.Any())
				{
					if (LogQueue.HasFatals)
					{
						if (base.RawStatus != DownloadStatus.FailureNoSuchArticle)
						{
							base.RawStatus = DownloadStatus.Failure;
						}
					}
					else if (IsDownloadScheduled)
					{
						if (!base.IsPausing)
						{
							base.RawStatus = DownloadStatus.Downloading;
						}
					}
					else
					{
						if (!base.IsPausing)
						{
							List<NNTPSegment> source = FilesToDownloadNoParPieces.SelectMany((NNTPInput f) => f.Segments.Where((NNTPSegment s) => !s.IsDataAvailable && !s.IsFailed)).ToList();
							if (source.Any())
							{
								List<NNTPSegment> source2 = source.Where((NNTPSegment s) => !s.IsDownloaded).ToList();
								if (source2.Any())
								{
									if (!source2.Any((NNTPSegment s) => s.IsUnderTimeout))
									{
										base.RawStatus = DownloadStatus.Queued;
									}
									return;
								}
							}
							DownloaderDataDecoder.WaitForDecodeTasksToComplete(FilesToDownloadNoParPieces);
							DownloaderDataStorer.WaitForAllCurrentItemsSave();
							FilesToDownloadNoParPieces.ForEach(DownloaderDataStorer.CloseFileStream);
							bool flag = false;
							try
							{
								flag = new PostProcessCoordinator(this, _cTokenForPostProcess.Token).Run();
								return;
							}
							finally
							{
								if (!_cTokenForPostProcess.IsCancellationRequested && base.RawStatus != DownloadStatus.WrongPassword)
								{
									if (LogQueue.HasFatals)
									{
										if (base.RawStatus != DownloadStatus.FailureNoSuchArticle)
										{
											base.RawStatus = DownloadStatus.Failure;
										}
									}
									else if (base.IsPausing)
									{
										base.RawStatus = DownloadStatus.Paused;
									}
									else
									{
										base.RawStatus = (flag ? DownloadStatus.Success : DownloadStatus.Warning);
									}
									if (base.IsHistory)
									{
										base.Finished = DateTime.Now.ToUnixTime().ToString();
									}
								}
							}
						}
						LogQueue.Debug("Download paused: " + base.Titel);
						base.RawStatus = DownloadStatus.Paused;
					}
				}
			}
		});
	}

	public void ScheduleTimerToSaveTheState()
	{
		if (base.RawStatus != DownloadStatus.Deleted)
		{
			_timerToSaveTheState?.Start();
		}
	}

	public static DownloaderItemViewModel RestoreState(int id)
	{
		lock (LockSaveTheState)
		{
			try
			{
				Dictionary<string, string> dictionary = AppHelper.RestoreDict(GetFilenameOfStateFile(id));
				if (dictionary == null || !dictionary.Any())
				{
					return null;
				}
				if (!dictionary.TryGetValue("Title", out var value))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Index", out var value2))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Status", out var value3))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Complete", out var value4))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Incomplete", out var value5))
				{
					return null;
				}
				dictionary.TryGetValue("LastWarning", out var value6);
				dictionary.TryGetValue("LastFatal", out var value7);
				if (!dictionary.TryGetValue("Nzb", out var value8))
				{
					return null;
				}
				if (!dictionary.TryGetValue("MessageId", out var value9))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Category", out var value10))
				{
					return null;
				}
				if (!dictionary.TryGetValue("Added", out var value11))
				{
					value11 = "0";
				}
				if (!dictionary.TryGetValue("Finished", out var value12))
				{
					value12 = "0";
				}
				if (!Enum.TryParse<DownloadStatus>(value3, ignoreCase: true, out var result))
				{
					return null;
				}
				if (!int.TryParse(value2, out var result2))
				{
					return null;
				}
				if (!int.TryParse(value10, out var result3))
				{
					return null;
				}
				if (result == DownloadStatus.Unknown)
				{
					result = DownloadStatus.Queued;
				}
				SpotnetDownloaderItemViewModel spotnetDownloaderItemViewModel = new SpotnetDownloaderItemViewModel(id, value, result, 0, 0.0, 0, result2, value5, value4, "", value9, result3, value8, Convert.ToInt64(value11), Convert.ToInt64(value12));
				spotnetDownloaderItemViewModel.LogQueue.LastWarning = value6;
				spotnetDownloaderItemViewModel.LogQueue.LastFatal = value7;
				SpotnetDownloaderItemViewModel spotnetDownloaderItemViewModel2 = spotnetDownloaderItemViewModel;
				if (result != spotnetDownloaderItemViewModel2.RawStatus && spotnetDownloaderItemViewModel2.IsHistory)
				{
					spotnetDownloaderItemViewModel2.SaveTheState();
				}
				return spotnetDownloaderItemViewModel2;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				return null;
			}
		}
	}

	private void SegmentOnDownloadedChanged(NNTPSegment seg, bool value)
	{
		if (base.RawStatus != DownloadStatus.Deleted && value)
		{
			_bpsMultiplier = (double)seg.ActualDataReceivedLength / (double)seg.ExpectedSize;
			_speedCalculator.AddNewValue(seg.ActualDataReceivedLength);
		}
	}

	private void SegmentOnSavedChanged(NNTPSegment seg, bool value)
	{
		if (base.RawStatus != DownloadStatus.Deleted && value && !IsDataRestoreInProgress)
		{
			seg.File.ScheduleSaveTheState();
		}
	}

	private void SegmentOnDownloadScheduledChanged(NNTPSegment segment, bool value)
	{
		if (base.RawStatus == DownloadStatus.Deleted)
		{
			return;
		}
		if (value)
		{
			int num = Interlocked.Increment(ref _downloadScheduledCount);
			if (base.IsQueued)
			{
				base.RawStatus = DownloadStatus.Downloading;
				if (num == 1)
				{
					LogQueue.Debug("Download started for: " + base.Titel);
				}
			}
		}
		else
		{
			Interlocked.Decrement(ref _downloadScheduledCount);
			if (!IsDownloadScheduled)
			{
				CheckForPostProcessAsync();
			}
		}
	}

	private void SegmentOnDataAvailableChanged(NNTPSegment seg, bool isDataAvailable)
	{
		if (base.RawStatus != DownloadStatus.Deleted && isDataAvailable)
		{
			StatsUpdateProgressOnBytesDownloaded(seg);
		}
	}

	public void RestoreStateOfFiles()
	{
		try
		{
			if (base.IsHistory)
			{
				return;
			}
			System.IO.FileInfo[] stateFiles = GetStateFiles(ID);
			foreach (System.IO.FileInfo fileInfo in stateFiles)
			{
				Match match = new Regex("_(\\d+).snet$").Match(fileInfo.FullName);
				if (!match.Success)
				{
					continue;
				}
				try
				{
					IsDataRestoreInProgress = true;
					int fileIndex = int.Parse(match.Groups[1].Value);
					NNTPInput nNTPInput = FilesToDownload.FirstOrDefault((NNTPInput f) => f.Index == fileIndex);
					if (nNTPInput == null)
					{
						LogQueue.Warn($"Failed to find the file with index {fileIndex} in {base.PathToNzb}");
						continue;
					}
					string[] array = System.IO.File.ReadAllLines(fileInfo.FullName);
					bool flag = false;
					string[] array2 = array;
					foreach (string text in array2)
					{
						if (flag)
						{
							string[] array3 = text.Split(',');
							if (array3.Length != 4)
							{
								break;
							}
							long indentBytes = long.Parse(array3[1]);
							int expectedSize = int.Parse(array3[2]);
							int id = int.Parse(array3[3]);
							NNTPSegment nNTPSegment = nNTPInput.Segments.FirstOrDefault((NNTPSegment s) => s.Index == id);
							if (nNTPSegment != null)
							{
								nNTPSegment.IndentBytes = indentBytes;
								nNTPSegment.ExpectedSize = expectedSize;
								nNTPSegment.IsDataAvailable = true;
								nNTPSegment.IsSaved = true;
							}
						}
						else if (text.StartsWith("Saved:"))
						{
							flag = true;
						}
					}
				}
				catch (Exception ex)
				{
					LogQueue.Warn("Failed to parse parts file: " + fileInfo.FullName + ". Error: " + ex.Message);
				}
				finally
				{
					IsDataRestoreInProgress = false;
				}
			}
			CheckForPostProcessAsync();
		}
		catch (Exception ex2)
		{
			Log.Exception(ex2);
		}
	}

	public void RemoveSavedState()
	{
		lock (LockSaveTheState)
		{
			try
			{
				try
				{
					string filenameOfStateFile = GetFilenameOfStateFile(ID);
					if (System.IO.File.Exists(filenameOfStateFile))
					{
						System.IO.File.Delete(filenameOfStateFile);
					}
					filenameOfStateFile = GetFilenameOfNzbFile(ID);
					if (System.IO.File.Exists(filenameOfStateFile))
					{
						System.IO.File.Delete(filenameOfStateFile);
					}
				}
				catch
				{
				}
				System.IO.FileInfo[] stateFiles = GetStateFiles(ID);
				foreach (System.IO.FileInfo fileInfo in stateFiles)
				{
					try
					{
						fileInfo.Attributes = FileAttributes.Normal;
						System.IO.File.Delete(fileInfo.FullName);
					}
					catch
					{
					}
				}
				try
				{
					string logPath = LogQueue.LogPath;
					if (System.IO.File.Exists(logPath))
					{
						System.IO.File.Delete(logPath);
					}
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}
	}

	private static System.IO.FileInfo[] GetStateFiles(int id)
	{
		return new System.IO.DirectoryInfo(DownloaderProps.QueueDir).GetFiles(id + "_*.snet");
	}

	private static string GetFilenameOfStateFile(int id)
	{
		return System.IO.Path.Combine(DownloaderProps.QueueDir, id + ".snet");
	}

	public static string GetFilenameOfStateFile(int id, int index)
	{
		return System.IO.Path.Combine(DownloaderProps.QueueDir, $"{id}_{index}.snet");
	}

	private static string GetFilenameOfNzbFile(int id)
	{
		return System.IO.Path.Combine(DownloaderProps.QueueDir, id + ".nzb");
	}

	public void StatsUpdateProgress(double percents)
	{
		base.Perc = percents;
	}

	public void StatsUpdateProgressOnBytesDownloaded(NNTPSegment segment)
	{
		if (!segment.File.IsParPiece)
		{
			StatsUpdateProgress(base.Perc + 1.0 * (double)segment.ExpectedSizeFromNzbFile / base.SizeMegaBytes * 100.0 / 1024.0 / 1024.0);
		}
		else if (base.RawStatus == DownloadStatus.Par2PieceDownloading)
		{
			StatsUpdateProgress(base.Perc + 1.0 * (double)segment.ExpectedSizeFromNzbFile / base.SizeOfPar2MegaBytes * 100.0 / 1024.0 / 1024.0);
		}
	}

	public void StatsUpdateSizeTotal()
	{
		long num = FilesToDownload.Aggregate(0L, (long c, NNTPInput file) => file.Segments.Aggregate(c, (long c2, NNTPSegment seg) => c2 + seg.ExpectedSizeFromNzbFile));
		base.SizeMegaBytes = (double)num / 1024.0 / 1024.0;
	}

	public bool DownloadParPieces(string par2Filename, int blocksMissed)
	{
		if (NNTPInput.IsParPieceByFilename(par2Filename))
		{
			par2Filename = NNTPInput.GetParPieceBase(par2Filename);
		}
		else if (par2Filename.EndsWith(".par2"))
		{
			par2Filename = par2Filename.Substring(0, par2Filename.Length - 5);
		}
		int num = FilesToDownloadParPieces.Sum((NNTPInput f) => f.ParPieceMinorNumber);
		if (blocksMissed > num)
		{
			LogQueue.Warn("Pieces to restore is not enough. Required: " + blocksMissed + ". Max: " + num);
			return false;
		}
		int num2 = 0;
		StartProgressForParPieces();
		foreach (NNTPInput item in FilesToDownloadParPieces.OrderBy((NNTPInput f) => f.ParPieceNumber))
		{
			if (item.ParPieceBase.Equals(par2Filename))
			{
				LogQueue.Debug("Par2 piece to download: " + item.Filename);
				List<NNTPInput> obj = new List<NNTPInput> { item };
				SpotDownloader.UpdatePriorities(obj);
				if (!WaitForFilesToDownload(obj))
				{
					return false;
				}
				if (item.Segments.All((NNTPSegment s) => s.IsSaved))
				{
					num2 += item.ParPieceMinorNumber;
				}
				if (num2 >= blocksMissed)
				{
					return true;
				}
			}
		}
		LogQueue.Warn($"Pieces to restore probably is not enough. Required: {blocksMissed}. Downloaded: {num2}. But still try to restore with it.");
		return true;
	}

	public void StartProgressForParPieces(NNTPInput parPieceFile = null)
	{
		base.RawStatus = DownloadStatus.Par2PieceDownloading;
		List<NNTPInput> list = ((parPieceFile == null) ? FilesToDownloadParPieces : new List<NNTPInput>(1) { parPieceFile });
		_healthChecker = new HealthChecker(list);
		int num = list.Sum((NNTPInput f) => f.Segments.Sum((NNTPSegment seg) => seg.ExpectedSizeFromNzbFile));
		base.SizeOfPar2MegaBytes = (double)num / 1024.0 / 1024.0;
		StatsUpdateProgress(0.0);
		list.ForEach(delegate(NNTPInput f)
		{
			f.Segments.ForEach(delegate(NNTPSegment s)
			{
				if (s.IsSaved)
				{
					SegmentOnDataAvailableChanged(s, isDataAvailable: true);
				}
				else if (s.IsFailed)
				{
					SegmentOnFailedChanged(s, isFailed: true);
				}
			});
		});
	}

	public static bool WaitForFilesToDownload(List<NNTPInput> filesToDownload)
	{
		foreach (NNTPInput item in filesToDownload)
		{
			foreach (NNTPSegment segment in item.Segments)
			{
				while (!segment.IsSaved && !segment.IsFailed)
				{
					Thread.Sleep(500);
				}
			}
		}
		DownloaderDataDecoder.WaitForDecodeTasksToComplete(filesToDownload);
		DownloaderDataStorer.WaitForAllCurrentItemsSave();
		filesToDownload.ForEach(DownloaderDataStorer.CloseFileStream);
		return true;
	}
}
