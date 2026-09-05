using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Utilities;

internal class History
{
	private const string sFile = "\\history.dat";

	private static readonly Logger logger = LogManager.GetCurrentClassLogger();

	public List<string> HistoryItems;

	public History()
	{
		HistoryItems = new List<string>();
	}

	private void LH()
	{
		if (!System.IO.File.Exists(AppHelper.SettingsFolder + "\\history.dat"))
		{
			return;
		}
		StreamReader streamReader = new StreamReader(AppHelper.SettingsFolder + "\\history.dat", Encoding.UTF8);
		while (!streamReader.EndOfStream)
		{
			string text = streamReader.ReadLine();
			if (!string.IsNullOrEmpty(text))
			{
				HistoryItems.Add(text);
			}
		}
		streamReader.Close();
	}

	public bool ClearHistory()
	{
		try
		{
			System.IO.File.Delete(AppHelper.SettingsFolder + "\\history.dat");
			return true;
		}
		catch (Exception ex)
		{
			logger.Exception(ex);
			return false;
		}
	}

	public bool LoadHistory()
	{
		HistoryItems = new List<string>();
		try
		{
			LH();
			if (HistoryItems.Count > 1000)
			{
				StreamWriter streamWriter = new StreamWriter(AppHelper.SettingsFolder + "\\history.dat", append: false, Encoding.UTF8);
				long num = 1L;
				foreach (string historyItem in HistoryItems)
				{
					num = checked(num + 1);
					if (num > 500)
					{
						streamWriter.WriteLine(historyItem);
					}
				}
				streamWriter.Close();
				HistoryItems = new List<string>();
				LH();
			}
			return true;
		}
		catch (Exception ex)
		{
			logger.Exception(ex);
			return false;
		}
	}

	public bool SaveHistory(string zHis)
	{
		try
		{
			if (!string.IsNullOrEmpty(zHis) && !HistoryItems.Contains(zHis))
			{
				StreamWriter streamWriter = new StreamWriter(AppHelper.SettingsFolder + "\\history.dat", append: true, Encoding.UTF8);
				streamWriter.WriteLine(zHis);
				streamWriter.Close();
				HistoryItems.Add(zHis);
			}
			return true;
		}
		catch (Exception ex)
		{
			logger.Exception(ex);
			return false;
		}
	}
}
