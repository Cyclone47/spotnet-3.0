using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader.PostProcessing;

public class QuickCheck
{
	private readonly CancellationToken _cToken;

	private readonly LogQueue _logQueue;

	private readonly string _par2FilePath;

	private readonly SpotnetDownloaderItemViewModel _item;

	private readonly string[] _quickCheckExtToIgnore = new string[3] { ".nfo", ".sfv", ".srr" };

	public QuickCheck(SpotnetDownloaderItemViewModel item, string par2File, LogQueue logQueue, CancellationToken cToken)
	{
		_item = item;
		_par2FilePath = Path.Combine(_item.IncompleteDir, par2File);
		_cToken = cToken;
		_logQueue = logQueue;
	}

	public bool Check()
	{
		List<Md5HashPair> list = Par2File.Parse(_par2FilePath);
		if (list == null || !list.Any())
		{
			return false;
		}
		bool result = true;
		List<NNTPInput> list2 = _item.FilesToDownload.Where((NNTPInput f) => !f.IsParOrParPiece && File.Exists(f.FullFilePath)).ToList();
		foreach (Md5HashPair item in list)
		{
			bool flag = false;
			bool flag2 = _quickCheckExtToIgnore.Contains(Path.GetExtension(item.Filename).ToLower());
			foreach (NNTPInput item2 in list2)
			{
				if (_cToken.IsCancellationRequested)
				{
					return false;
				}
				if (item.Filename.Equals(item2.Filename))
				{
					flag = true;
					list2.Remove(item2);
					if (!item2.Md5Sum.IsNullOrEmpty() && item2.Md5Sum.Equals(item.Hash))
					{
						_logQueue.Debug("QuickCheck on file \"" + item.Filename + "\" PASSED");
						break;
					}
					if (flag2)
					{
						_logQueue.Debug("QuickCheck on file \"" + item.Filename + "\" ignored");
						break;
					}
					_logQueue.Debug("QuickCheck on file \"" + item.Filename + "\" failed!");
					result = false;
					break;
				}
				if (!item2.Md5Sum.IsNullOrEmpty() && item2.Md5Sum.Equals(item.Hash))
				{
					try
					{
						_logQueue.Debug("QuickCheck will rename \"" + item2.Filename + "\" to \"" + item.Filename + "\"");
						string text = item2.CheckOrFixFilenameIsUnique(item.Filename);
						string newPath = Path.Combine(_item.IncompleteDir, text);
						AppHelper.RenameHard(item2.FullFilePath, newPath);
						item2.Filename = text;
						flag = true;
						list2.Remove(item2);
					}
					catch (Exception ex)
					{
						_logQueue.Debug("Failed to rename the file: " + ex.Message);
					}
					break;
				}
			}
			if (!flag)
			{
				if (flag2)
				{
					_logQueue.Debug("QuickCheck ignoring missing file " + item.Filename);
					continue;
				}
				_logQueue.Debug("QuickCheck missing the file " + item.Filename);
				result = false;
			}
		}
		return result;
	}
}
