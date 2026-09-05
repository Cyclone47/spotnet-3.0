using System;
using System.Collections.Generic;
using System.Timers;
using NLog;
using System.IO;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

public static class Remover
{
	private static readonly Logger Log;

	private static readonly Timer TimerToRemove;

	private static readonly HashSet<string> DirectoriesToRemove;

	private static readonly HashSet<string> FilesToRemove;

	static Remover()
	{
		Log = LogManager.GetCurrentClassLogger();
		DirectoriesToRemove = new HashSet<string>();
		FilesToRemove = new HashSet<string>();
		TimerToRemove = new Timer(300000.0);
		TimerToRemove.Elapsed += delegate
		{
			DirectoriesToRemove.RemoveWhere(RemoveDirectory);
			FilesToRemove.RemoveWhere(RemoveFile);
		};
		TimerToRemove.Start();
	}

	public static void ScheduleDirectoryRemove(string path)
	{
		if (!path.IsNullOrWhiteSpace())
		{
			path = path.Trim();
			if (!path.IsNullOrEmpty() && !RemoveDirectory(path))
			{
				DirectoriesToRemove.Add(path);
			}
		}
	}

	public static void ScheduleFileRemove(string path)
	{
		if (!path.IsNullOrWhiteSpace())
		{
			path = path.Trim();
			if (!path.IsNullOrEmpty() && !RemoveFile(path))
			{
				FilesToRemove.Add(path);
			}
		}
	}

	private static bool RemoveDirectory(string path)
	{
		if (!AppHelper.DeleteDirectoryHard(path))
		{
			Log.Warn("Failed to remove directory: " + path);
			return false;
		}
		return true;
	}

	private static bool RemoveFile(string path)
	{
		if (!File.Exists(path))
		{
			return true;
		}
		bool result = false;
		try
		{
			File.Delete(path);
			result = true;
		}
		catch (Exception)
		{
		}
		return result;
	}
}
