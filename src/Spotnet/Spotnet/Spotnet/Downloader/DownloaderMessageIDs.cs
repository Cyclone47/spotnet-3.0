using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Downloader;

public class DownloaderMessageIDs
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly Dictionary<int, string> _dictionary = new Dictionary<int, string>();

	private readonly string _messageIdFilePath = Path.Combine(AppHelper.SettingsFolder, "downloads_msgids.txt");

	public DownloaderMessageIDs()
	{
		if (!File.Exists(_messageIdFilePath))
		{
			return;
		}
		bool flag = false;
		string[] array = File.ReadAllLines(_messageIdFilePath);
		for (int i = 0; i < array.Length; i++)
		{
			FromLine(array[i], out var key, out var messageId, out var category);
			if (key >= 0)
			{
				if (_dictionary.ContainsKey(key))
				{
					_dictionary[key] = MsgIdAndCat(messageId, category);
					flag = true;
				}
				else
				{
					_dictionary.Add(key, MsgIdAndCat(messageId, category));
				}
			}
		}
		if (flag)
		{
			SaveAllToFile();
		}
	}

	private void SaveAllToFile()
	{
		lock (_dictionary)
		{
			File.WriteAllLines(_messageIdFilePath, _dictionary.Select((KeyValuePair<int, string> k) => ToLine(k.Key, k.Value)).ToList());
		}
	}

	public void AddOrUpdate(int id, string messageId, int category)
	{
		if (id < 0)
		{
			throw new ArgumentNullException("id");
		}
		if (messageId.IsNullOrEmpty())
		{
			throw new ArgumentNullException("messageId");
		}
		lock (_dictionary)
		{
			if (_dictionary.ContainsKey(id))
			{
				Update(id, messageId, category);
			}
			else
			{
				Add(id, messageId, category);
			}
		}
	}

	public void RemoveAllExcept(params int[] ids)
	{
		lock (_dictionary)
		{
			List<int> list = _dictionary.Keys.Except(ids).ToList();
			if (!list.Any())
			{
				return;
			}
			foreach (int item in list)
			{
				_dictionary.Remove(item);
			}
			SaveAllToFile();
		}
	}

	private void Add(int id, string messageId, int category)
	{
		lock (_dictionary)
		{
			if (!_dictionary.ContainsKey(id))
			{
				File.AppendAllLines(_messageIdFilePath, new string[1] { ToLine(id, MsgIdAndCat(messageId, category)) });
				_dictionary.Add(id, MsgIdAndCat(messageId, category));
			}
		}
	}

	private string MsgIdAndCat(string messageId, int category)
	{
		return $"{messageId} {category}";
	}

	private void Update(int id, string messageId, int category)
	{
		lock (_dictionary)
		{
			if (_dictionary.ContainsKey(id) && !_dictionary[id].Equals(MsgIdAndCat(messageId, category)))
			{
				_dictionary[id] = MsgIdAndCat(messageId, category);
				SaveAllToFile();
			}
		}
	}

	public bool Get(int id, out string messageId, out int category)
	{
		lock (_dictionary)
		{
			messageId = "";
			category = 0;
			if (!_dictionary.TryGetValue(id, out var value))
			{
				return false;
			}
			FromLine(ToLine(id, value), out id, out messageId, out category);
			return true;
		}
	}

	private static void FromLine(string line, out int key, out string messageId, out int category)
	{
		key = -1;
		messageId = "";
		category = 0;
		string[] array = line.Split(new char[1] { ' ' }, 3);
		if ((array.Length == 3 || array.Length == 2) && int.TryParse(array[0], out key))
		{
			messageId = array[1];
			if (array.Length == 3)
			{
				int.TryParse(array[2], out category);
			}
		}
	}

	private static string ToLine(int key, string msgIdAndCat)
	{
		return $"{key} {msgIdAndCat}";
	}
}
