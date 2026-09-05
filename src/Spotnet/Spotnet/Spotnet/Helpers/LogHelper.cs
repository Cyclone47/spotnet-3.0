using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.Helpers;

public static class LogHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static void Exception(this Logger log, Exception ex, bool showToClient = false)
	{
		try
		{
			string name = new StackFrame(1).GetMethod().Name;
			Exception ex2 = ex.TheMostInnerException();
			string text = $"{name} [{ex2.GetType()}]: {ex2.Message}";
			if (Sys.IsShutdownRequested)
			{
				return;
			}
			log.Error(text);
			if (!ex.StackTrace.IsNullOrWhiteSpace())
			{
				log.Debug("Error StackTrace: " + ex.StackTrace);
			}
			if (ex2 != ex)
			{
				string stackTrace = ex2.StackTrace;
				if (!stackTrace.IsNullOrWhiteSpace())
				{
					log.Debug("Inner error StackTrace: " + stackTrace);
				}
			}
			if (ex.Data.Count > 0)
			{
				foreach (object datum in ex.Data)
				{
					Log.Debug("{0}: {1}", datum, ex.Data[datum]);
				}
			}
			if (showToClient)
			{
				AppHelper.Error(text);
			}
		}
		catch (Exception ex3)
		{
			try
			{
				log.Error("Exception on error logging: " + ex3.Message);
				log.Debug(ex.StackTrace);
			}
			catch
			{
			}
		}
	}

	internal static string ZipLogFiles()
	{
		List<string> list = new List<string>
		{
			Path.Combine(AppHelper.SettingsFolder, "Logs\\spotnet.log"),
			Path.Combine(Directory.GetParent(AppHelper.AppPath()).FullName, "SquirrelSetup.log"),
			Path.Combine(AppHelper.GetTempPath(), "SquirrelSetup.log"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquirrelTemp\\SquirrelSetup.log")
		}.Where((string f) => File.Exists(f) && new FileInfo(f).Length < 10485760).ToList();
		if (!list.Any())
		{
			return null;
		}
		string text = Path.Combine(AppHelper.GetTempPath(), "spotnet_logs.zip");
		List<Tuple<string, string>> files = new List<Tuple<string, string>>();
		foreach (string item in list)
		{
			string entryName = Path.GetFileName(item);
			if (item.EndsWith("SquirrelSetup.log"))
			{
				entryName = Path.Combine(Directory.GetParent(item).Name, entryName);
			}
			files.Add(Tuple.Create(item, entryName));
		}
		SafeZip.Create(text, files);
		return text;
	}
}
