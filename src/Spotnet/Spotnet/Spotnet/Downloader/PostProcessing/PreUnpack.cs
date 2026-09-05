using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader.PostProcessing;

public class PreUnpack
{
	private SpotnetDownloaderItemViewModel _item;

	private CancellationTokenSource _cTokenSource;

	private LogQueue _logQueue;

	private List<NNTPSegment> _archivedSegmentsToProcess;

	private List<string> _listOfMultipartArchivesFailed;

	private Task _preUnpackerTask;

	private readonly AutoResetEvent _segmentStatusFlag = new AutoResetEvent(initialState: true);

	private bool _isPreunpackOnPause;

	private ProcessEx _proc;

	public int SegmentsInQueueCount => _archivedSegmentsToProcess.Count;

	public bool IsPreUnpackRunning { get; private set; }

	private string PreUnpackDir => System.IO.Path.Combine(_item.IncompleteDir, "__preunpack\\");

	private static event Action PauseAllPreunpacks;

	public event Action Stopped;

	public void Initialize(SpotnetDownloaderItemViewModel item)
	{
		if (_item != null)
		{
			_logQueue.Debug("Preunpack initialized already");
			return;
		}
		_logQueue = item.LogQueue;
		_item = item;
		PauseAllPreunpacks += Pause;
	}

	public static void PauseAll()
	{
		PreUnpack.PauseAllPreunpacks?.Invoke();
	}

	public void Stop()
	{
		if (IsPreUnpackRunning)
		{
			_cTokenSource?.Cancel();
		}
	}

	public void Pause()
	{
		if (!_isPreunpackOnPause)
		{
			_logQueue.Debug("Pause preunpack");
		}
		_isPreunpackOnPause = true;
	}

	private void Resume()
	{
		if (_isPreunpackOnPause)
		{
			_logQueue.Debug("Resume preunpack");
		}
		_isPreunpackOnPause = false;
	}

	public void Wait()
	{
		_preUnpackerTask?.Wait();
	}

	public void RunAsync()
	{
		if (_item == null)
		{
			throw new Exception("Initialize preunpack first");
		}
		PauseAll();
		Resume();
		if (IsPreUnpackRunning)
		{
			return;
		}
		_cTokenSource = new CancellationTokenSource();
		if (!AppHelper.DeleteDirectoryHard(PreUnpackDir))
		{
			_logQueue.Debug("Failed to remove preunpack dir before the start.");
		}
		_preUnpackerTask = Task.Run(delegate
		{
			if (IsPreUnpackRunning)
			{
				return false;
			}
			IsPreUnpackRunning = true;
			try
			{
				_listOfMultipartArchivesFailed = new List<string>();
				FillTheQueue();
				return ExecuteUnrar();
			}
			catch (Exception ex)
			{
				_logQueue.Warn("Pre-unpack failed: " + ex.Message);
				return false;
			}
			finally
			{
				IsPreUnpackRunning = false;
				this.Stopped?.Invoke();
			}
		}, _cTokenSource.Token);
	}

	private void FillTheQueue()
	{
		if (!_item.FilesToDownload.Any())
		{
			return;
		}
		_archivedSegmentsToProcess = new List<NNTPSegment>();
		foreach (NNTPInput item in _item.FilesToDownload)
		{
			if (ArchiveHelper.IsRarFile(item.Filename))
			{
				_archivedSegmentsToProcess.AddRange(item.Segments);
			}
		}
	}

	private bool ExecuteUnrar()
	{
		if (_item == null || !_item.FilesToDownload.Any() || _item.IsHistory || _item.IsPostProcess)
		{
			return false;
		}
		_logQueue.Debug("Start pre-unpack");
		AppHelper.EnsureDirectoryExist(PreUnpackDir);
		string text = string.Concat("\"" + ArchiveHelper.UnRarPath + "\" x -kb -y -p- ", "-o+ \"", PreUnpackDir, "\" -");
		_logQueue.Debug("cmd: " + text);
		_logQueue.Debug("directory: " + _item.IncompleteDir);
		_proc = new ProcessEx(text, _item.IncompleteDir);
		List<string> rarFilesUnpacked = new List<string>();
		bool allOkMessageReceived = false;
		_proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Debug("Pre-Unpack: " + a.Data);
				if (a.Data.StartsWith("Extracting from "))
				{
					rarFilesUnpacked.Add(a.Data.Substring("Extracting from ".Length));
				}
				else if (a.Data.Equals("All OK"))
				{
					allOkMessageReceived = true;
				}
			}
		};
		_proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Warn(a.Data);
			}
		};
		DateTime now = DateTime.Now;
		BinaryWriter binaryWriter = null;
		NNTPSegment nNTPSegment = null;
		try
		{
			_proc.Start();
			Thread.Sleep(1000);
			binaryWriter = _proc.GetInputBinaryWriter();
			try
			{
				int num = 0;
				while (!_proc.IsTerminated && !_cTokenSource.IsCancellationRequested && num < _archivedSegmentsToProcess.Count)
				{
					WaitForResume();
					nNTPSegment = _archivedSegmentsToProcess[num++];
					if (IsFileInFailedArchives(nNTPSegment.File.Filename))
					{
						continue;
					}
					if (_cTokenSource.IsCancellationRequested)
					{
						break;
					}
					WaitForSegmentDataSaved(nNTPSegment);
					if (_cTokenSource.IsCancellationRequested)
					{
						break;
					}
					MemoryStream data = DownloaderDataStorer.GetData(nNTPSegment);
					if (data == null)
					{
						_logQueue.Warn("Cannot get preunpack bytes for segment: " + nNTPSegment.MessageId);
						Stop();
						break;
					}
					try
					{
						byte[] buffer = data.GetBuffer();
						binaryWriter.BaseStream.Write(buffer, 0, buffer.Length);
						if (num % 10 == 0)
						{
							binaryWriter.Flush();
						}
					}
					catch (Exception ex)
					{
						_logQueue.Warn("Failed to write to the preunpack input: " + ex.Message);
						Stop();
						break;
					}
				}
			}
			catch (Exception ex2)
			{
				if (!_cTokenSource.IsCancellationRequested)
				{
					_logQueue.Warn("PreUnpack error: " + ex2.Message + "\nStack: " + ex2.StackTrace);
				}
				Stop();
			}
		}
		finally
		{
			if (binaryWriter != null)
			{
				binaryWriter.Flush();
				binaryWriter.Dispose();
			}
		}
		if (_cTokenSource.IsCancellationRequested)
		{
			_proc.Kill();
			_logQueue.Debug("Pre-unpack stopped");
			return true;
		}
		_proc.Wait(_cTokenSource.Token);
		_logQueue.Debug($"Pre-Unpack finished in {(DateTime.Now - now).Seconds} seconds");
		bool flag = _proc.ExitCode == 0 && allOkMessageReceived;
		if (flag)
		{
			_logQueue.Debug("Pre-Unpack process completed successfully");
		}
		else
		{
			switch (_proc.ExitCode)
			{
			case -1:
				_logQueue.Warn("Failed to run pre-unpack cmd: " + text);
				return false;
			case 0:
				_logQueue.Warn("All OK message is not received from Pre-Unpack (unrar)");
				break;
			case 5:
				_logQueue.Warn("Pre-Unpack space error");
				return false;
			case 9:
				_logQueue.Warn("Unrar error code: " + _proc.ExitCode);
				_logQueue.Warn("Probably it's a long path problem. Failed to unpack.");
				return false;
			case 11:
				_logQueue.Warn("Pre-Unpack password error");
				return false;
			default:
				if (_proc.ExitCode > 0)
				{
					_logQueue.Warn("Pre-Unpack (unrar) error code: " + _proc.ExitCode);
				}
				break;
			}
			if (nNTPSegment != null)
			{
				MarkArchiveAsFailed(nNTPSegment.File.Filename);
				return ExecuteUnrar();
			}
		}
		return flag;
	}

	private void WaitForResume()
	{
		while (_isPreunpackOnPause && !_proc.IsTerminated && !_cTokenSource.IsCancellationRequested)
		{
			Thread.Sleep(TimeSpan.FromSeconds(1.0));
		}
	}

	private void WaitForSegmentDataSaved(NNTPSegment seg)
	{
		seg.FailedChanged += SetSegmentStatusFlag;
		seg.SavedInternalChanged += SetSegmentStatusFlag;
		while (!seg.IsSaved && !seg.IsFailed && seg.File.DownloaderItem.RawStatus != DownloadStatus.Deleted && !_proc.IsTerminated && !_cTokenSource.IsCancellationRequested)
		{
			_segmentStatusFlag.WaitOne(TimeSpan.FromSeconds(10.0));
		}
		seg.FailedChanged -= SetSegmentStatusFlag;
		seg.SavedInternalChanged -= SetSegmentStatusFlag;
	}

	private void SetSegmentStatusFlag(NNTPSegment nntpSegment, bool b)
	{
		_segmentStatusFlag.Set();
	}

	private void MarkArchiveAsFailed(string path)
	{
		_listOfMultipartArchivesFailed.Add(path);
	}

	private bool IsFileFromTheSameMultipartArchive(string path1, string path2)
	{
		return GetBodyOfMultipartArchive(path1)?.Equals(GetBodyOfMultipartArchive(path2)) ?? false;
	}

	private bool IsFileInFailedArchives(string path)
	{
		return _listOfMultipartArchivesFailed.Any((string multipartArchiveFailed) => IsFileFromTheSameMultipartArchive(path, multipartArchiveFailed));
	}

	private string GetBodyOfMultipartArchive(string path)
	{
		path = path.Trim();
		if (!ArchiveHelper.IsRarFile(path))
		{
			return null;
		}
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(path);
		if (fileNameWithoutExtension.IsNullOrEmpty())
		{
			return null;
		}
		Match match = new Regex("(.+)\\.part\\d+", RegexOptions.IgnoreCase).Match(fileNameWithoutExtension);
		if (!match.Success)
		{
			return fileNameWithoutExtension;
		}
		return match.Groups[1].Value;
	}
}
