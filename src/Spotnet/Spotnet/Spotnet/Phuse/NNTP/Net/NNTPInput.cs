using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Phuse.NNTP.Net;

public class NNTPInput
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly ManualResetEvent _saveTheStateFlag = new ManualResetEvent(initialState: true);

	private readonly System.Timers.Timer _timerToSaveTheState;

	public readonly SpotnetDownloaderItemViewModel DownloaderItem;

	public long Filesize = -1L;

	public List<string> Groups = new List<string>();

	public int Index = -1;

	public List<NNTPSegment> Segments = new List<NNTPSegment>();

	private readonly object _lockSaveTheState = new object();

	private int _segmentsDataReceived;

	private readonly object _lockDispose = new object();

	private string _md5Sum;

	private readonly object _lockMd5Calc = new object();

	private string _filename;

	public bool IsDisposed { get; private set; }

	public string Md5Sum
	{
		get
		{
			if (_md5Sum.IsNullOrEmpty())
			{
				lock (_lockMd5Calc)
				{
					if (_md5Sum.IsNullOrEmpty() && System.IO.File.Exists(FullFilePath))
					{
						using MD5 mD = MD5.Create();
						using FileStream inputStream = System.IO.File.OpenRead(FullFilePath);
						_md5Sum = AppHelper.MakeMd5(mD.ComputeHash(inputStream));
					}
				}
			}
			return _md5Sum;
		}
	}

	public string Subject { get; }

	public bool IsAllSegmentsDataReceived => Segments.Count == _segmentsDataReceived;

	public string Filename
	{
		get
		{
			return _filename;
		}
		set
		{
			if (_filename == null || !_filename.Equals(value))
			{
				_filename = CheckOrFixFilenameIsUnique(value);
			}
		}
	}

	public bool IsParPiece => IsParPieceByFilename(Filename);

	public string ParPieceBase => GetParPieceBase(Filename);

	public int ParPieceNumber => GetParPieceNumber(Filename);

	public int ParPieceMinorNumber
	{
		get
		{
			Match match = new Regex("^(.*)\\.vol(\\d+)\\+(\\d+)\\.par2$", RegexOptions.IgnoreCase).Match(Filename.Trim());
			if (match.Success)
			{
				try
				{
					return int.Parse(match.Groups[3].Value);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					return 0;
				}
			}
			return 0;
		}
	}

	public bool IsPar
	{
		get
		{
			if (IsParOrParPiece)
			{
				return !IsParPiece;
			}
			return false;
		}
	}

	public bool IsParOrParPiece => new Regex("(\\.par2$)", RegexOptions.IgnoreCase).IsMatch(Filename.Trim());

	public string FullFilePath => System.IO.Path.Combine(DownloaderItem.IncompleteDir, Filename);

	public string FullPreUnpackPath => System.IO.Path.Combine(System.IO.Path.Combine(DownloaderItem.IncompleteDir, "__preunpack/"), Filename);

	internal NNTPInput(SpotnetDownloaderItemViewModel item, string subject, int index = -1)
	{
		DownloaderItem = item;
		subject = subject.Trim();
		Subject = subject;
		if (!subject.IsNullOrWhiteSpace() && !subject.Equals("D"))
		{
			Filename = ParseSubjectToFilename(subject);
			Index = ParseSubjectToIndex(subject);
		}
		if (Index == -1)
		{
			Index = index;
		}
		if (item == null)
		{
			return;
		}
		_timerToSaveTheState = new System.Timers.Timer(500.0)
		{
			AutoReset = false
		};
		_timerToSaveTheState.Elapsed += delegate
		{
			if (!Monitor.TryEnter(_lockSaveTheState))
			{
				ScheduleSaveTheState();
				return;
			}
			try
			{
				SaveTheState();
			}
			finally
			{
				Monitor.Exit(_lockSaveTheState);
			}
		};
	}

	public void Dispose()
	{
		lock (_lockDispose)
		{
			if (IsDisposed)
			{
				return;
			}
			IsDisposed = true;
		}
		_timerToSaveTheState?.Dispose();
	}

	public string CheckOrFixFilenameIsUnique(string filename)
	{
		if (DownloaderItem != null && DownloaderItem.IsFilesToDownloadInitialized)
		{
			while (DownloaderItem.FilesToDownload.Any((NNTPInput f) => f.Filename.Equals(filename)))
			{
				filename = UpdateFilenameDuplicatePart(filename);
			}
		}
		return filename;
	}

	public static string UpdateFilenameDuplicatePart(string filename)
	{
		if (filename.IsNullOrEmpty())
		{
			return "duplicate1";
		}
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filename);
		string extension = System.IO.Path.GetExtension(filename);
		Match match = new Regex("^(.*)duplicate(\\d+)$").Match(fileNameWithoutExtension);
		if (match.Success)
		{
			string value = match.Groups[1].Value;
			int num = Convert.ToInt32(match.Groups[2].Value);
			return $"{value}duplicate{num + 1}{extension}";
		}
		return fileNameWithoutExtension + ".duplicate1" + extension;
	}

	public static bool IsParPieceByFilename(string filename)
	{
		return new Regex("(\\.vol(\\d+)\\+(\\d+)\\.par2$)", RegexOptions.IgnoreCase).IsMatch(filename.Trim());
	}

	public static string GetParPieceBase(string filename)
	{
		Match match = new Regex("^(.*)\\.vol(\\d+)\\+(\\d+)\\.par2$", RegexOptions.IgnoreCase).Match(filename.Trim());
		if (match.Success)
		{
			return match.Groups[1].Value;
		}
		return "";
	}

	public static int GetParPieceNumber(string filename)
	{
		Match match = new Regex("^(.*)\\.vol(\\d+)\\+(\\d+)\\.par2$", RegexOptions.IgnoreCase).Match(filename.Trim());
		if (match.Success)
		{
			try
			{
				return int.Parse(match.Groups[2].Value) + int.Parse(match.Groups[3].Value);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				return 0;
			}
		}
		return 0;
	}

	internal void AddNewSegment(NNTPSegment segment)
	{
		Segments.Add(segment);
		segment.DataAvailableChanged += delegate(NNTPSegment s, bool value)
		{
			if (value)
			{
				Interlocked.Increment(ref _segmentsDataReceived);
			}
			else
			{
				Interlocked.Decrement(ref _segmentsDataReceived);
			}
		};
	}

	private string ParseSubjectToFilename(string subject)
	{
		subject = subject.Trim();
		Match match = new Regex("^(.+) yEnc (\\d+/\\d+)$").Match(subject);
		if (match.Success)
		{
			subject = match.Groups[1].Value;
		}
		Match match2 = new Regex("\"(.+)\"").Match(subject);
		if (match2.Success)
		{
			string value = match2.Groups[1].Value;
			if (FilenamePathIsValid(value))
			{
				return value;
			}
		}
		string[] array = Regex.Split(subject, "(?<=^[^\"]*(?:\"[^\"]*\"[^\"]*)*) (?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
		if (array.Length != 0)
		{
			foreach (string item in array.Reverse())
			{
				if (item.Contains('.'))
				{
					return item.Replace("\"", string.Empty);
				}
			}
		}
		subject = AppHelper.MakeFilename(subject);
		DownloaderItem.LogQueue.Debug("Failed to parse nzb file subject. Use subject as a filename: " + subject);
		return subject;
	}

	private int ParseSubjectToIndex(string subject)
	{
		int result = -1;
		Match match = new Regex("\\[(\\d+)\\/(\\d+)\\]").Match(subject);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result2))
		{
			return result2;
		}
		match = new Regex("yEnc \\((\\d+)\\/(\\d+)\\)").Match(subject);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result3))
		{
			return result3;
		}
		return result;
	}

	private static bool FilenamePathIsValid(string path)
	{
		if (!path.IsNullOrEmpty() && path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) == 0)
		{
			return System.IO.Path.GetDirectoryName(path).IsNullOrEmpty();
		}
		return false;
	}

	public void SaveTheState()
	{
		try
		{
			List<NNTPSegment> list = Segments.Where((NNTPSegment s) => s.IsSaved).ToList();
			if (!list.Any())
			{
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("Path: " + Filename);
			stringBuilder.AppendLine($"TotalSegments: {Segments.Count}");
			stringBuilder.AppendLine("Saved: ");
			foreach (NNTPSegment item in list)
			{
				stringBuilder.AppendLine($"0,{item.IndentBytes},{item.ExpectedSize},{item.Index}");
			}
			System.IO.File.WriteAllText(SpotnetDownloaderItemViewModel.GetFilenameOfStateFile(DownloaderItem.ID, Index), stringBuilder.ToString());
		}
		finally
		{
			_saveTheStateFlag.Set();
		}
	}

	public void ScheduleSaveTheState()
	{
		if (!IsDisposed)
		{
			_saveTheStateFlag.Reset();
			_timerToSaveTheState.Start();
		}
	}

	public void WaitForSaveTheState()
	{
		_saveTheStateFlag.WaitOne();
	}

	public void MarkAsFailed()
	{
		Segments.ForEach(delegate(NNTPSegment s)
		{
			if (!s.IsSaved)
			{
				s.MarkAsFailed();
			}
		});
	}
}
