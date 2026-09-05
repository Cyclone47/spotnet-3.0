using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Downloader.PostProcessing;

public class PostProcessCoordinator
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly CancellationToken _cToken;

	private readonly SpotnetDownloaderItemViewModel _downloaderItem;

	private readonly LogQueue _logQueue;

	public PostProcessCoordinator(SpotnetDownloaderItemViewModel item, CancellationToken cToken)
	{
		_downloaderItem = item;
		_cToken = cToken;
		_logQueue = item.LogQueue;
	}

	public bool Run()
	{
		bool flag = true;
		try
		{
			_downloaderItem.PreUnpackWaitForFinish();
			_downloaderItem.PreUnpackStopAndWait();
			_logQueue.Debug("Start PostProcess");
			Unpack unpack = new Unpack(_downloaderItem, _cToken);
			ParRecover parRecover = new ParRecover(_downloaderItem, _cToken);
			parRecover.ProgressChanged += _downloaderItem.StatsUpdateProgress;
			ProcessSplittedFiles();
			if (!parRecover.Run())
			{
				if (_downloaderItem.FilesToDownloadNoParPieces.Any((NNTPInput f) => !f.Segments.All((NNTPSegment s) => s.IsSaved)))
				{
					_logQueue.Fatal("Failed to recover and there are failed segments: " + _downloaderItem.Titel);
					return false;
				}
				_logQueue.Warn("Failed to recover: \"" + _downloaderItem.Titel + "\". But continue processing due to all segments are on the disk.");
			}
			_downloaderItem.StatsUpdateProgress(-1.0);
			if (!unpack.Run(out var passwordProblem))
			{
				string message = Words.UnpackFailed + ": " + _downloaderItem.Titel + ". \n" + Words.UnpackFailedPostMessage;
				_logQueue.Warn(message);
				flag = false;
				if (passwordProblem)
				{
					_downloaderItem.RawStatus = DownloadStatus.WrongPassword;
					return false;
				}
			}
			if (Settings.Default.RemovePar2FilesAfterDownload)
			{
				parRecover.RemovePar2FilesAndWaitForDeleted();
			}
			try
			{
				MoveFilesToCompleteDir();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				_logQueue.Warn(string.Format(Words.MoveToCompleteDirFailedMessage, _downloaderItem.Titel) + " " + ex.Message);
				flag = false;
				_downloaderItem.CompleteDir = _downloaderItem.IncompleteDir;
			}
			if (flag)
			{
				Sys.DownloadsPlayer.SwitchToAnotherDirectory(_downloaderItem.CompleteDir, _downloaderItem);
				AppHelper.DeleteDirectoryHard(_downloaderItem.IncompleteDir);
			}
		}
		catch (Exception ex2)
		{
			if (!_cToken.IsCancellationRequested)
			{
				Log.Exception(ex2);
				_logQueue.Fatal(ex2.Message);
			}
			return false;
		}
		finally
		{
			_downloaderItem.StatsUpdateProgress(100.0);
			_logQueue.Debug("Post Process complete " + (flag ? "successfully" : "with problems"));
		}
		return flag;
	}

	private void ProcessSplittedFiles()
	{
		List<string> list = (from f in _downloaderItem.FilesToDownloadNoParPieces
			where System.IO.File.Exists(f.FullFilePath)
			select f.Filename).ToList();
		List<string> splittedBases = GetSplittedBases(list);
		if (!splittedBases.Any())
		{
			return;
		}
		_downloaderItem.RawStatus = DownloadStatus.Verifying;
		_downloaderItem.StatsUpdateProgress(-1.0);
		foreach (string item in splittedBases)
		{
			if (System.IO.File.Exists(item))
			{
				continue;
			}
			List<string> list2 = new List<string>();
			foreach (string item2 in list)
			{
				Match match = new Regex("^(.*\\.[a-zA-Z0-9]{3})\\.([0-9]{3})").Match(item2);
				if (item2.StartsWith(item) && match.Success)
				{
					list2.Add(item2);
				}
			}
			JoinSplittedFiles(item, list2);
		}
	}

	private void JoinSplittedFiles(string baseFilename, List<string> splittedFiles)
	{
		splittedFiles.Sort();
		string incompleteDir = _downloaderItem.IncompleteDir;
		string path = System.IO.Path.Combine(incompleteDir, baseFilename);
		try
		{
			using (FileStream destination = System.IO.File.Create(path))
			{
				foreach (string splittedFile in splittedFiles)
				{
					using (FileStream fileStream = System.IO.File.OpenRead(System.IO.Path.Combine(incompleteDir, splittedFile)))
					{
						fileStream.CopyTo(destination);
					}
					_logQueue.Debug("The splitted part " + splittedFile + " copied to " + baseFilename);
				}
			}
			foreach (string splittedFile2 in splittedFiles)
			{
				try
				{
					System.IO.File.Delete(System.IO.Path.Combine(incompleteDir, splittedFile2));
				}
				catch (Exception ex)
				{
					_logQueue.Warn("Filed to remove file " + splittedFile2 + ". Error: " + ex.Message);
				}
			}
			string subject = "splitted.file - [1/1] - &quot;" + baseFilename + "&quot; yEnc (1/1)";
			_downloaderItem.FilesToDownload.Add(new NNTPInput(_downloaderItem, subject, 0));
		}
		catch (Exception ex2)
		{
			_logQueue.Warn("Failed to join splitted files: " + ex2.Message);
		}
	}

	private List<string> GetSplittedBases(List<string> completeFilesList)
	{
		List<string> list = new List<string>();
		Regex regex = new Regex("^(.*\\.[a-zA-Z0-9]{3})\\.([0-9]{3})");
		Regex regex2 = new Regex("^(.*\\.[a-zA-Z0-9]{3})\\.001");
		List<string> list2 = new List<string>();
		foreach (string completeFiles in completeFilesList)
		{
			Match match = regex2.Match(completeFiles);
			if (match.Success)
			{
				list2.Add(match.Groups[1].Value);
			}
		}
		string incompleteDir = _downloaderItem.IncompleteDir;
		foreach (string item in list2)
		{
			List<int> list3 = new List<int>();
			long length = new System.IO.FileInfo(System.IO.Path.Combine(incompleteDir, item + ".001")).Length;
			int num = -1;
			bool flag = true;
			foreach (string completeFiles2 in completeFilesList)
			{
				if (!completeFiles2.StartsWith(item))
				{
					continue;
				}
				Match match2 = regex.Match(completeFiles2);
				if (!match2.Success)
				{
					continue;
				}
				int num2 = Convert.ToInt32(match2.Groups[2].Value);
				long length2 = new System.IO.FileInfo(System.IO.Path.Combine(incompleteDir, completeFiles2)).Length;
				if (length2 != length)
				{
					if (num != -1 || length2 > length)
					{
						flag = false;
						break;
					}
					num = num2;
				}
				list3.Add(num2);
			}
			if (!list3.Any() || num == -1)
			{
				flag = false;
			}
			else
			{
				list3.Sort();
				for (int i = 1; i <= num; i++)
				{
					if (list3[i - 1] != i)
					{
						flag = false;
						break;
					}
				}
			}
			if (flag)
			{
				list.Add(item);
			}
		}
		return list;
	}

	private void MoveFilesToCompleteDir()
	{
		_cToken.ThrowIfCancellationRequested();
		_downloaderItem.RawStatus = DownloadStatus.Moving;
		_logQueue.Debug("Move files to complete dir");
		AppHelper.MoveFilesRecursively(_downloaderItem.IncompleteDir, _downloaderItem.CompleteDir, _cToken, new string[1] { "__preunpack" });
		_cToken.ThrowIfCancellationRequested();
	}
}
