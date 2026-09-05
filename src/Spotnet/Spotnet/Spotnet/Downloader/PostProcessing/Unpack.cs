using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader.PostProcessing;

public class Unpack
{
	private readonly CancellationToken _cToken;

	private readonly SpotnetDownloaderItemViewModel _downloaderItem;

	private readonly LogQueue _logQueue;

	private List<string> _filesUnpackedSuccessfully;

	public string UnpackTargetDir => System.IO.Path.Combine(_downloaderItem.IncompleteDir, "__unpack/");

	public Unpack(SpotnetDownloaderItemViewModel item, CancellationToken cToken)
	{
		_downloaderItem = item;
		_cToken = cToken;
		_logQueue = item.LogQueue;
	}

	public bool Run(out bool passwordProblem)
	{
		_downloaderItem.RawStatus = DownloadStatus.Unpacking;
		_filesUnpackedSuccessfully = new List<string>();
		DetectPasswordIfNotSetYet();
		bool flag = ExecuteUnrar(withPath: true, out passwordProblem);
		if (passwordProblem)
		{
			return false;
		}
		if (flag)
		{
			flag = ExecuteSevenZip("*.zip");
		}
		if (flag)
		{
			flag = ExecuteSevenZip("*.7z");
		}
		if (flag)
		{
			flag = ExecuteSevenZip("*.7z.001");
		}
		if (!flag)
		{
			_filesUnpackedSuccessfully.Clear();
		}
		CleanUpArchiveFiles();
		return flag;
	}

	/// <summary>
	/// Fills in the archive password from what the download itself carries, when nothing
	/// has set one yet.
	/// </summary>
	/// <remarks>
	/// A password the user typed into ChangeUnpackPasswordWindow, or one the spot body
	/// already supplied when the download was queued, is left exactly as it is - this only
	/// ever fills an empty value. The NZB stays on disk in the queue directory, so this
	/// still works for a download resumed in a later session, where the spot body is long
	/// gone. When it finds nothing, or finds the wrong thing, unrar reports the password
	/// problem and the manual dialog takes over as before.
	///
	/// The password is never written to the log; only the fact that one was found is.
	/// </remarks>
	private void DetectPasswordIfNotSetYet()
	{
		if (!_downloaderItem.UnpackPassword.IsNullOrEmpty())
		{
			return;
		}
		try
		{
			string detected = UnpackPasswordDetector.Detect(_downloaderItem.PathToNzb, _downloaderItem.Titel);
			if (detected.IsNullOrEmpty())
			{
				return;
			}
			_downloaderItem.UnpackPassword = detected;
			_logQueue.Debug("Unpack password taken from the NZB metadata or the spot text.");
		}
		catch (Exception ex)
		{
			_logQueue.Debug("Failed to look for an unpack password: " + ex.Message);
		}
	}

	private void CleanUpArchiveFiles()
	{
		try
		{
			if (_filesUnpackedSuccessfully.Any())
			{
				RemoveFiles(_filesUnpackedSuccessfully);
				AppHelper.MoveFilesRecursively(UnpackTargetDir, _downloaderItem.IncompleteDir, _cToken);
			}
			if (System.IO.Directory.Exists(UnpackTargetDir))
			{
				System.IO.Directory.Delete(UnpackTargetDir, recursive: true);
			}
		}
		catch (IOException ex)
		{
			_logQueue.Debug(ex.Message);
		}
	}

	private bool ExecuteUnrar(bool withPath, out bool passwordProblem)
	{
		passwordProblem = false;
		bool flag = IsAnyNonStdRarFiles();
		if (!FilesExist(_downloaderItem.IncompleteDir, "*.rar") && !flag)
		{
			_logQueue.Debug("No *.rar files to unpack");
			return true;
		}
		AppHelper.EnsureDirectoryExist(_downloaderItem.IncompleteDir);
		string text = ".rar";
		if (!flag)
		{
			_logQueue.Debug("Unpacking *.rar files");
		}
		else
		{
			_logQueue.Debug("There are non standart rar archives. Unpacking files");
			text = ".*";
		}
		string text2 = (_downloaderItem.UnpackPassword.IsNullOrEmpty() ? "-" : ("\"" + _downloaderItem.UnpackPassword.Replace("\"", "") + "\""));
		string text3 = ((!_downloaderItem.IncompleteDir.StartsWith("\\\\")) ? string.Format("\"{0}\" x -y -p{3} {1}-o+ -kb *{2} ./__unpack\\", ArchiveHelper.UnRarPath, withPath ? "" : "-ep ", text, text2) : string.Format("\"{0}\" x -y -p{4} {1}-o+ -kb \"{2}/*{3}\" \"{2}/__unpack\\\"", ArchiveHelper.UnRarPath, withPath ? "" : "-ep ", _downloaderItem.IncompleteDir, text, text2));
		_logQueue.Debug("cmd: " + text3);
		_logQueue.Debug("directory: " + _downloaderItem.IncompleteDir);
		ProcessEx processEx = new ProcessEx(text3, _downloaderItem.IncompleteDir);
		List<string> rarFilesUnpacked = new List<string>();
		bool allOkMessageReceived = false;
		Regex progressRegex = new Regex("^.* ([0-9]+)%.*$");
		bool isPasswordProblemPosibility = false;
		processEx.OutputDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Debug("Unrar: " + a.Data);
				string text4 = "Extracting from ";
				if (a.Data.StartsWith(text4))
				{
					rarFilesUnpacked.Add(a.Data.Substring(text4.Length));
				}
				else if (a.Data.Equals("All OK"))
				{
					allOkMessageReceived = true;
				}
				Match match = progressRegex.Match(a.Data);
				if (match.Success)
				{
					int num = Convert.ToInt32(match.Groups[1].Value);
					if (num >= 0 && num < 100)
					{
						_downloaderItem.StatsUpdateProgress(num);
					}
				}
			}
		};
		processEx.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Warn("Unrar: " + a.Data);
				if (a.Data.Contains("wrong password"))
				{
					isPasswordProblemPosibility = true;
				}
			}
		};
		DateTime now = DateTime.Now;
		processEx.Start();
		processEx.Wait(_cToken);
		if (isPasswordProblemPosibility)
		{
			passwordProblem = true;
		}
		_logQueue.Debug($"Unpack finished in {(DateTime.Now - now).Seconds} seconds");
		_filesUnpackedSuccessfully = _filesUnpackedSuccessfully.Concat(rarFilesUnpacked).ToList();
		bool flag2 = processEx.ExitCode == 0 && allOkMessageReceived;
		if (!flag2)
		{
			if (processEx.ExitCode == -1)
			{
				_logQueue.Fatal("Failed to run unpack cmd: " + text3);
			}
			else if (processEx.ExitCode == 5)
			{
				_logQueue.Warn("Unpack space error");
			}
			else
			{
				if (processEx.ExitCode == 9 && withPath)
				{
					_logQueue.Warn("Unrar error code: " + processEx.ExitCode);
					_logQueue.Warn("Probably it's a long path problem, so try to extract with no path.");
					try
					{
						if (System.IO.Directory.Exists(UnpackTargetDir))
						{
							System.IO.Directory.Delete(UnpackTargetDir, recursive: true);
						}
					}
					catch (IOException ex)
					{
						_logQueue.Debug(ex.Message);
					}
					return ExecuteUnrar(withPath: false, out passwordProblem);
				}
				if (processEx.ExitCode == 11)
				{
					passwordProblem = true;
				}
			}
			if (processEx.ExitCode > 0)
			{
				_logQueue.Warn("Unrar error code: " + processEx.ExitCode);
			}
			else if (processEx.ExitCode == 0)
			{
				_logQueue.Warn("All OK message is not received from unrar");
			}
		}
		return flag2;
	}

	private bool IsAnyNonStdRarFiles()
	{
		Regex nonStdRegex = new Regex("^.*\\.[0-9]+$");
		return (from f in _downloaderItem.FilesToDownloadNoParPieces
			where System.IO.File.Exists(f.FullFilePath) && nonStdRegex.IsMatch(f.Filename)
			select f.FullFilePath).ToList().Any(IsFileHasRarSignature);
	}

	private bool IsFileHasRarSignature(string filename)
	{
		if (!System.IO.File.Exists(filename))
		{
			return false;
		}
		byte[] array = new byte[7] { 82, 97, 114, 33, 26, 7, 0 };
		byte[] array2 = new byte[8] { 82, 97, 114, 33, 26, 7, 1, 0 };
		byte[] array3 = new byte[8];
		int num;
		try
		{
			using FileStream fileStream = System.IO.File.OpenRead(filename);
			num = fileStream.Read(array3, 0, array3.Length);
		}
		catch (UnauthorizedAccessException ex)
		{
			_logQueue.Warn("Failed to determine is file a rar archive or not : " + ex.Message);
			return false;
		}
		if (num == array3.Length)
		{
			if (!array3.Take(array.Length).SequenceEqual(array))
			{
				return array3.Take(array2.Length).SequenceEqual(array2);
			}
			return true;
		}
		return false;
	}

	private bool ExecuteSevenZip(string filesMask)
	{
		if (!FilesExist(_downloaderItem.IncompleteDir, filesMask))
		{
			_logQueue.Debug("No " + filesMask + " files to unpack");
			return true;
		}
		_logQueue.Debug("Unpacking " + filesMask + " files");
		AppHelper.EnsureDirectoryExist(_downloaderItem.IncompleteDir);
		AppHelper.EnsureDirectoryExist(UnpackTargetDir);
		string text = (_downloaderItem.UnpackPassword.IsNullOrEmpty() ? "-" : ("\"" + _downloaderItem.UnpackPassword.Replace("\"", "") + "\""));
		string text2 = ((!_downloaderItem.IncompleteDir.StartsWith("\\\\")) ? string.Format("\"{0}\" x -y -p{2} -o./__unpack\\ {1}", ArchiveHelper.SevenZipPath, filesMask, text) : string.Format("\"{0}\" x -y -p{3} -o\"{2}/__unpack\\\" \"{2}/{1}\"", ArchiveHelper.SevenZipPath, filesMask, _downloaderItem.IncompleteDir, text));
		_logQueue.Debug("cmd: " + text2);
		_logQueue.Debug("directory: " + _downloaderItem.IncompleteDir);
		ProcessEx proc = new ProcessEx(text2, _downloaderItem.IncompleteDir);
		List<string> archiveFilesUnpacked = new List<string>();
		bool noErrors = true;
		proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs a)
		{
			if (!a.Data.IsNullOrEmpty())
			{
				_logQueue.Debug("7z: " + a.Data);
				string text3 = "Extracting archive: ";
				if (a.Data.StartsWith(text3))
				{
					archiveFilesUnpacked.Add(a.Data.Substring(text3.Length));
				}
				else if (a.Data.StartsWith("Archives with Errors: "))
				{
					noErrors = false;
				}
				else if (a.Data.StartsWith("ERROR: "))
				{
					_logQueue.Fatal("Cancelling unpack " + filesMask + " due to " + a.Data);
					proc.Kill();
				}
			}
		};
		DateTime now = DateTime.Now;
		proc.Start();
		proc.Wait(_cToken);
		_logQueue.Debug($"Unpack finished in {(DateTime.Now - now).Seconds} seconds");
		if (_logQueue.HasFatals)
		{
			return false;
		}
		bool num = proc.ExitCode == 0 && noErrors;
		if (num)
		{
			_filesUnpackedSuccessfully = _filesUnpackedSuccessfully.Concat(archiveFilesUnpacked).ToList();
			return num;
		}
		if (proc.ExitCode == -1)
		{
			_logQueue.Fatal("Failed to run 7z cmd: " + text2);
			return num;
		}
		if (proc.ExitCode > 0)
		{
			_logQueue.Fatal("7za error code: " + proc.ExitCode);
			return num;
		}
		if (proc.ExitCode == 0 && !proc.IsTerminated)
		{
			_logQueue.Fatal("There are archives with errors from 7za");
		}
		return num;
	}

	private bool FilesExist(string path, string mask)
	{
		return System.IO.Directory.GetFiles(path, mask, SearchOption.TopDirectoryOnly).Any();
	}

	private void RemoveFiles(List<string> filesToRemove)
	{
		bool flag = true;
		List<System.IO.FileInfo> list = filesToRemove.Select((string f) => new System.IO.FileInfo(System.IO.Path.Combine(_downloaderItem.IncompleteDir, f))).ToList();
		foreach (System.IO.FileInfo item in list)
		{
			_cToken.ThrowIfCancellationRequested();
			try
			{
				item.Delete();
				item.Refresh();
			}
			catch (Exception ex)
			{
				flag = false;
				_logQueue.Debug(ex.Message);
			}
		}
		if (!flag)
		{
			return;
		}
		foreach (System.IO.FileInfo item2 in list)
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
