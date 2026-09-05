using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using System.IO;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Downloader.PostProcessing;

public class ParRecover
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly SpotnetDownloaderItemViewModel _item;

	private readonly CancellationToken _cToken;

	private readonly LogQueue _logQueue;

	private int _blocksMissed;

	private Par2Results _retResult;

	private List<string> _filesMissed;

	private int _estimationOfParProcessSize;

	private int _parFilesProcessed;

	private bool _isScanExtraFilesStage;

	private string Par2ExePath => Path.Combine(AppHelper.AppPath(), "phpar2.exe");

	public event Action<double> ProgressChanged;

	public ParRecover(SpotnetDownloaderItemViewModel item, CancellationToken cToken)
	{
		_item = item;
		_cToken = cToken;
		_logQueue = item.LogQueue;
	}

	private bool TryToRecover(string parFile, bool includeExtraFiles, out Par2Results retResult)
	{
		_blocksMissed = 0;
		_cToken.ThrowIfCancellationRequested();
		retResult = Par2Results.OtherFailure;
		_estimationOfParProcessSize = 0;
		_parFilesProcessed = 0;
		string text = "";
		if (includeExtraFiles)
		{
			if (!_filesMissed.Any())
			{
				_logQueue.Warn("It should never be happen");
				return false;
			}
			List<string> list = (from f in _item.FilesToDownload
				where File.Exists(f.FullFilePath)
				select f.Filename).ToList();
			text = "\"" + string.Join("\" \"", list) + "\"";
			_estimationOfParProcessSize += list.Count;
		}
		string text2 = "\"" + Par2ExePath + "\" r \"" + parFile + "\" " + text;
		_logQueue.Debug("cmd: " + text2);
		_logQueue.Debug("directory: " + _item.IncompleteDir);
		ProcessEx processEx = new ProcessEx(text2, _item.IncompleteDir);
		processEx.OutputDataReceived += ProcOnOutputDataReceived;
		processEx.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Warn(a.Data);
			}
		};
		_filesMissed = new List<string>();
		_isScanExtraFilesStage = false;
		OnProgressChanged(0.0);
		_retResult = retResult;
		processEx.Start();
		processEx.Wait(_cToken);
		retResult = _retResult;
		if (_filesMissed.Any() && retResult == Par2Results.CannotRepair)
		{
			retResult = Par2Results.CannotRepairBecauseOfFilesAreMissed;
		}
		bool result = true;
		switch (retResult)
		{
		case Par2Results.NoRepairNeeded:
			_logQueue.Debug("Archive needs no repair for " + parFile);
			break;
		case Par2Results.Repaired:
			_logQueue.Debug("Repair succesfull for " + parFile);
			break;
		case Par2Results.CannotRepair:
			_logQueue.Warn("Cannot repair par2 archive (need more blocks): " + parFile);
			result = false;
			break;
		case Par2Results.CannotRepairBecauseOfFilesAreMissed:
			_logQueue.Warn("Cannot repair par2 archive (files missing): " + parFile);
			result = false;
			break;
		default:
			_logQueue.Warn("Generic failure (cannot verify par2 archive): " + parFile);
			result = false;
			break;
		}
		return result;
	}

	private void ProcOnOutputDataReceived(object s, DataReceivedEventArgs a)
	{
		if (a.Data == null || _cToken.IsCancellationRequested)
		{
			return;
		}
		if (!a.Data.EndsWith("%"))
		{
			Log.Debug("par2: " + a.Data);
		}
		if (a.Data.Contains("All files are correct, repair is not required"))
		{
			_retResult = Par2Results.NoRepairNeeded;
		}
		else if (a.Data.Contains("Repair complete"))
		{
			_retResult = Par2Results.Repaired;
		}
		else if (a.Data.Contains("Repair is not possible"))
		{
			_retResult = Par2Results.CannotRepair;
		}
		else if (a.Data.EndsWith(" more recovery blocks to be able to repair."))
		{
			int length = "You need ".Length;
			int length2 = " more recovery blocks to be able to repair.".Length;
			int.TryParse(a.Data.Substring(length, a.Data.Length - length - length2), out var result);
			_blocksMissed += result;
		}
		else if (a.Data.EndsWith(" recovery blocks available."))
		{
			int length3 = "You have ".Length;
			int length4 = " recovery blocks available.".Length;
			int.TryParse(a.Data.Substring(length3, a.Data.Length - length3 - length4), out var result2);
			_blocksMissed += result2;
		}
		else if (a.Data.StartsWith("Target: "))
		{
			OnProgressChanged();
			if (a.Data.TrimEnd().EndsWith(" - missing."))
			{
				int length5 = "Target: \"".Length;
				int length6 = "\" - missing.".Length;
				string item = a.Data.Substring(length5, a.Data.Length - length5 - length6);
				_filesMissed.Add(item);
			}
		}
		else if (a.Data.StartsWith("There are ") && a.Data.EndsWith(" other files."))
		{
			Match match = new Regex("There are (\\d+) recoverable files and (\\d+) other files.").Match(a.Data);
			if (match.Success)
			{
				_estimationOfParProcessSize += Convert.ToInt32(match.Groups[1].Value);
				OnProgressChanged();
			}
		}
		else if (a.Data.StartsWith("Scanning extra files:"))
		{
			_isScanExtraFilesStage = true;
		}
		else if (_isScanExtraFilesStage && a.Data.StartsWith("File: "))
		{
			OnProgressChanged();
		}
		else if (a.Data.StartsWith("Scanning: ") && a.Data.EndsWith("%"))
		{
			int num = a.Data.LastIndexOf(": ", StringComparison.Ordinal);
			if (num > 0 && double.TryParse(a.Data.Substring(num + 2, a.Data.Length - (num + 2) - 1).Replace(".", ","), out var result3))
			{
				OnProgressChanged(-1.0, result3);
			}
		}
		else if (a.Data.StartsWith("Repairing: ") && a.Data.EndsWith("%"))
		{
			int num2 = a.Data.LastIndexOf(": ", StringComparison.Ordinal);
			if (num2 > 0 && double.TryParse(a.Data.Substring(num2 + 2, a.Data.Length - (num2 + 2) - 1).Replace(".", ","), out var result4))
			{
				OnProgressChanged(result4);
			}
		}
	}

	private void OnProgressChanged(double forceProgress = -1.0, double minorProgressPerc = -1.0)
	{
		if (forceProgress < 0.0)
		{
			if (_estimationOfParProcessSize == 0)
			{
				_estimationOfParProcessSize = 1;
			}
			double num = ((minorProgressPerc < 0.0) ? ((double)_parFilesProcessed) : ((double)(_parFilesProcessed - 1) + minorProgressPerc / 100.0)) * 100.0 / (double)_estimationOfParProcessSize;
			this.ProgressChanged?.Invoke((num > 100.0) ? 100.0 : num);
			if (minorProgressPerc < 0.0)
			{
				_parFilesProcessed++;
			}
			if (_estimationOfParProcessSize < _parFilesProcessed)
			{
				_estimationOfParProcessSize = _parFilesProcessed;
			}
		}
		else
		{
			this.ProgressChanged?.Invoke(forceProgress);
		}
	}

	public bool Run()
	{
		if (_item.IncompleteDir.StartsWith("\\\\"))
		{
			_logQueue.Debug("Skip par recovery as UNC path (\\\\<address>) is not supported");
			return false;
		}
		_cToken.ThrowIfCancellationRequested();
		_logQueue.Debug("Running recovery");
		AppHelper.EnsureDirectoryExist(_item.IncompleteDir);
		DateTime now = DateTime.Now;
		List<string> par2Files = GetPar2Files();
		if (par2Files == null)
		{
			_logQueue.Debug("No par files");
			return false;
		}
		bool flag = true;
		foreach (string item in par2Files)
		{
			_cToken.ThrowIfCancellationRequested();
			if (Settings.Default.QuickCheck)
			{
				_item.RawStatus = DownloadStatus.Checking;
				if (new QuickCheck(_item, item, _logQueue, _cToken).Check())
				{
					_logQueue.Debug("QuickCheck is OK. Skip recovery for " + item + ".");
					continue;
				}
				_logQueue.Debug("QuickCheck failed. Try to recover with " + item + ".");
			}
			else
			{
				_logQueue.Debug("QuickCheck disabled in settings.");
			}
			_item.RawStatus = DownloadStatus.Repairing;
			flag = TryToRecover(item, includeExtraFiles: false, out var retResult);
			if (retResult == Par2Results.CannotRepairBecauseOfFilesAreMissed)
			{
				flag = TryToRecover(item, includeExtraFiles: true, out retResult);
			}
			if ((retResult == Par2Results.CannotRepair || retResult == Par2Results.CannotRepairBecauseOfFilesAreMissed) && _item.DownloadParPieces(item, _blocksMissed))
			{
				_item.RawStatus = DownloadStatus.Repairing;
				flag = TryToRecover(item, includeExtraFiles: false, out retResult);
				if (retResult == Par2Results.CannotRepairBecauseOfFilesAreMissed)
				{
					flag = TryToRecover(item, includeExtraFiles: true, out retResult);
				}
			}
			if (!flag)
			{
				break;
			}
		}
		_logQueue.Debug($"Par check done in {(int)(DateTime.Now - now).TotalSeconds} seconds");
		return flag;
	}

	private List<string> GetPar2Files()
	{
		List<string> list = (from f in _item.FilesToDownload
			where f.IsPar && File.Exists(f.FullFilePath)
			orderby f.Segments.Sum((NNTPSegment s) => s.ExpectedSize) descending
			select f.Filename).ToList();
		if (!list.Any())
		{
			List<IGrouping<string, NNTPInput>> list2 = (from f in _item.FilesToDownload
				where f.IsParPiece
				group f by f.ParPieceBase).ToList();
			if (!list2.Any())
			{
				return null;
			}
			List<NNTPInput> list3 = new List<NNTPInput>();
			foreach (IGrouping<string, NNTPInput> item in list2)
			{
				foreach (NNTPInput item2 in item.OrderBy((NNTPInput f) => f.ParPieceNumber))
				{
					List<NNTPInput> obj = new List<NNTPInput> { item2 };
					_item.LogQueue.Debug("Start download par2 piece: " + item2.Filename);
					_item.StartProgressForParPieces(item2);
					SpotDownloader.UpdatePriorities(obj);
					if (SpotnetDownloaderItemViewModel.WaitForFilesToDownload(obj) && File.Exists(item2.FullFilePath))
					{
						list3.Add(item2);
						break;
					}
				}
			}
			if (!list3.Any())
			{
				return null;
			}
			list = list3.Select((NNTPInput f) => f.Filename).ToList();
		}
		return list;
	}

	public void RemovePar2FilesAndWaitForDeleted()
	{
		_cToken.ThrowIfCancellationRequested();
		List<FileInfo> list = (from f in _item.FilesToDownload
			where f.Filename.TrimEnd().ToLower().EndsWith(".par2") && File.Exists(f.FullFilePath)
			select new FileInfo(f.FullFilePath)).ToList();
		bool flag = true;
		foreach (FileInfo item in list)
		{
			_cToken.ThrowIfCancellationRequested();
			_logQueue.Debug("Remove par2 file: " + item.Name);
			try
			{
				item.Delete();
				item.Refresh();
			}
			catch (Exception ex)
			{
				flag = false;
				Log.Debug(ex.Message);
			}
		}
		if (!flag)
		{
			return;
		}
		foreach (FileInfo item2 in list)
		{
			_cToken.ThrowIfCancellationRequested();
			while (item2.Exists)
			{
				Thread.Sleep(50);
				item2.Refresh();
			}
		}
	}
}
