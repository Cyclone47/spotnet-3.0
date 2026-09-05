using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Utilities;

internal class Tabs
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly string TabsFile = AppHelper.SettingsFolder + "\\tabs.dat";

	public List<string> TabList;

	private string _cachedTabsFileContent = "-";

	public Tabs()
	{
		TabList = new List<string>();
	}

	private void Lt()
	{
		if (!System.IO.File.Exists(TabsFile))
		{
			return;
		}
		StreamReader streamReader = new StreamReader(TabsFile, Encoding.UTF8);
		while (!streamReader.EndOfStream)
		{
			string text = streamReader.ReadLine();
			if (!text.IsNullOrEmpty())
			{
				TabList.Add(text);
			}
		}
		streamReader.Close();
	}

	public bool ClearTabs()
	{
		try
		{
			if (System.IO.File.Exists(TabsFile))
			{
				System.IO.File.Delete(TabsFile);
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	public bool LoadTabs()
	{
		TabList = new List<string>();
		try
		{
			Lt();
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	public bool SaveTabs(List<string> zTab)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (string item in zTab)
			{
				stringBuilder.AppendLine(item);
			}
			if (!_cachedTabsFileContent.Equals(stringBuilder.ToString()))
			{
				_cachedTabsFileContent = stringBuilder.ToString();
				System.IO.File.WriteAllText(TabsFile, _cachedTabsFileContent, Encoding.UTF8);
			}
			TabList = zTab;
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}
}
