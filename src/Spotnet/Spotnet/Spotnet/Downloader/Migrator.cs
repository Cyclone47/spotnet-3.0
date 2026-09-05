using System;
using System.Collections.Generic;
using NLog;
using System.IO;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Downloader;

public static class Migrator
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static bool Run()
	{
		if (!Directory.Exists(DownloaderProps.NzbGetQueueDir))
		{
			return true;
		}
		string text = Path.Combine(DownloaderProps.NzbGetQueueDir, "queue");
		if (!File.Exists(text))
		{
			return true;
		}
		return ParseNzbGetStyleQueueFile(text);
	}

	public static bool ParseNzbGetStyleQueueFile(string oldQueueFilename)
	{
		try
		{
			string[] array = File.ReadAllLines(oldQueueFilename);
			Dictionary<int, KeyValuePair<byte, string>> dictionary = ParseMessageIdList();
			bool flag = false;
			int result = -1;
			for (int i = 0; i < array.Length; i++)
			{
				if (flag)
				{
					if (++i >= array.Length)
					{
						break;
					}
					string text = array[i];
					if (++i >= array.Length || ++i >= array.Length)
					{
						break;
					}
					string text2 = array[i];
					if (File.Exists(text2))
					{
						if (++i >= array.Length)
						{
							break;
						}
						string title = array[i];
						if (++i >= array.Length || ++i >= array.Length)
						{
							break;
						}
						string text3 = array[i];
						if (text3.Length < 3 || !int.TryParse(text3[2].ToString(), out var result2) || ++i >= array.Length)
						{
							break;
						}
						DownloadStatus downloadStatus = DetermineStatus(result2, array[i]);
						string incompleteDir = ((downloadStatus == DownloadStatus.Success) ? "" : text);
						string completeDir = ((downloadStatus == DownloadStatus.Success) ? text : "");
						string messageId = "";
						byte category = 0;
						if (dictionary.ContainsKey(result))
						{
							category = dictionary[result].Key;
							messageId = dictionary[result].Value;
						}
						((SpotnetDownloaderItemViewModel)DownloaderItemFactory.New(-1, title, downloadStatus, 0, 0.0, 0, -1, incompleteDir, completeDir, "", messageId, category, text2, 0L, 0L)).SaveTheState();
					}
					flag = false;
				}
				else
				{
					flag = array[i] == "0" && ++i < array.Length && array[i].IsNullOrEmpty();
					if (flag && !int.TryParse(array[i - 2], out result))
					{
						result = -1;
					}
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	private static Dictionary<int, KeyValuePair<byte, string>> ParseMessageIdList()
	{
		string[] array = File.ReadAllLines(Path.Combine(AppHelper.SettingsFolder, "downloads_msgids.txt"));
		Dictionary<int, KeyValuePair<byte, string>> dictionary = new Dictionary<int, KeyValuePair<byte, string>>();
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			string[] array3 = array2[i].Split(' ');
			byte result;
			if (array3.Length == 2)
			{
				result = 0;
			}
			else if (array3.Length != 3 || !byte.TryParse(array3[2], out result))
			{
				continue;
			}
			if (int.TryParse(array3[0], out var result2))
			{
				string value = array3[1];
				dictionary.Add(result2, new KeyValuePair<byte, string>(result, value));
			}
		}
		return dictionary;
	}

	private static DownloadStatus DetermineStatus(int postStage, string postStatusLine)
	{
		if (postStatusLine.Equals("0,0,0,0,0,0,0"))
		{
			return DownloadStatus.Paused;
		}
		if (postStage > 0 && postStage < 8)
		{
			return DownloadStatus.Paused;
		}
		if (!int.TryParse(postStatusLine[4].ToString(), out var result))
		{
			return DownloadStatus.Paused;
		}
		if (result != 2)
		{
			return DownloadStatus.Failure;
		}
		return DownloadStatus.Success;
	}
}
