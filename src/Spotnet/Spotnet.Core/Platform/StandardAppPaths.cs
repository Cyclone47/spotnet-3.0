using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Spotnet.Platform;

public class StandardAppPaths : IAppPaths
{
	private readonly string _customDataFolder;
	private readonly string _customDownloadsFolder;

	public string DataFolder
	{
		get
		{
			if (!string.IsNullOrEmpty(_customDataFolder))
			{
				return _customDataFolder;
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				return Path.Combine(home, "Library", "Application Support", "Spotnet");
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Spotnet");
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".spotnet");
		}
	}

	public string CacheFolder
	{
		get
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				return Path.Combine(home, "Library", "Caches", "Spotnet");
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet", "Cache");
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "spotnet");
		}
	}

	public string LogsFolder
	{
		get
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				return Path.Combine(home, "Library", "Logs", "Spotnet");
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet", "Logs");
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".spotnet", "logs");
		}
	}

	public string FiltersFolder => Path.Combine(DataFolder, "Filters.v2");

	public string DownloadsFolder
	{
		get
		{
			if (!string.IsNullOrEmpty(_customDownloadsFolder))
			{
				return _customDownloadsFolder;
			}
			string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
			if (Directory.Exists(downloads))
			{
				return downloads;
			}
			return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		}
	}

	public string TempFolder => Path.Combine(Path.GetTempPath(), "Spotnet");

	public StandardAppPaths(string customDataFolder = null, string customDownloadsFolder = null)
	{
		_customDataFolder = customDataFolder;
		_customDownloadsFolder = customDownloadsFolder;
	}

	public string GetDatabasePath(string serverAddress)
	{
		string sanitized = string.Concat(serverAddress.Split(Path.GetInvalidFileNameChars()));
		return Path.Combine(DataFolder, sanitized + ".db");
	}

	public string GetTempFileName(string ext = null, string filename = null)
	{
		if (string.IsNullOrEmpty(ext))
		{
			ext = "tmp";
		}
		if (string.IsNullOrEmpty(filename))
		{
			filename = Guid.NewGuid().ToString();
		}
		EnsureDirectoriesExist();
		return Path.Combine(TempFolder, filename + "." + ext);
	}

	public void EnsureDirectoriesExist()
	{
		Directory.CreateDirectory(DataFolder);
		Directory.CreateDirectory(CacheFolder);
		Directory.CreateDirectory(LogsFolder);
		Directory.CreateDirectory(FiltersFolder);
		Directory.CreateDirectory(TempFolder);
	}
}
