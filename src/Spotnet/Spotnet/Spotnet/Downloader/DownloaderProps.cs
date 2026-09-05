using System;
using System.Runtime.InteropServices;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Downloader;

public static class DownloaderProps
{
	public const string DefaultControlIp = "127.0.0.1";

	public const string DefaultControlPort = "6789";

	public const string DefaultControlUsername = "nzbget";

	public const string DefaultControlPassword = "tegbzn6789";

	public static string DefaultMainDir
	{
		get
		{
			if (Environment.OSVersion.Version.Major >= 6)
			{
				return GetFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
			}
			return Environment.ExpandEnvironmentVariables("%USERPROFILE%") + "\\Downloads";
		}
	}

	public static string DefaultDestDir => MainDir;

	public static string DefaultInterDir => Path.Combine(MainDir, "incomplete");

	public static string DefaultNzbDir => Path.Combine(MainDir, "nzb");

	public static string DefaultQueueDir => Path.Combine(DownloaderSettingsDir, "queue");

	private static string DownloaderSettingsDir => Path.Combine(AppHelper.SettingsFolder, "Downloader");

	public static string DefaultServer1Host => AppHelper.GetServer(ServerType.Download).Server.ToLower();

	public static string DefaultServer1Port => AppHelper.GetServer(ServerType.Download).Port.ToString();

	public static string DefaultServer1Username
	{
		get
		{
			string username = AppHelper.GetServer(ServerType.Download).Username;
			if (!AppHelper.IsSnelNlProvider)
			{
				return username;
			}
			return username.Replace('@', '_');
		}
	}

	public static string DefaultServer1Password => AppHelper.GetServer(ServerType.Download).Password;

	public static string DefaultServer1Encryption
	{
		get
		{
			if (!AppHelper.GetServer(ServerType.Download).SSL)
			{
				return "no";
			}
			return "yes";
		}
	}

	public static bool MainDirIsCustom
	{
		get
		{
			if (!Settings.Default.DownloadFolder.IsNullOrWhiteSpace())
			{
				return !Settings.Default.DownloadFolder.Equals("-");
			}
			return false;
		}
	}

	public static string MainDir
	{
		get
		{
			string text = (MainDirIsCustom ? Settings.Default.DownloadFolder : DefaultMainDir);
			if (text.Length == 2 && text[1] == ':')
			{
				text += "\\";
			}
			return text;
		}
	}

	public static bool DestDirIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetDestDir.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetDestDir.Equals("-");
			}
			return false;
		}
	}

	public static string DestDir
	{
		get
		{
			if (!DestDirIsCustom)
			{
				return DefaultDestDir;
			}
			return Settings.Default.NzbGetDestDir;
		}
	}

	public static bool InterDirIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetInterDir.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetInterDir.Equals("-");
			}
			return false;
		}
	}

	public static string InterDir
	{
		get
		{
			if (!InterDirIsCustom)
			{
				return DefaultInterDir;
			}
			return Settings.Default.NzbGetInterDir;
		}
	}

	public static bool NzbDirIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetNzbDir.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetNzbDir.Equals("-");
			}
			return false;
		}
	}

	public static string QueueNzbDir
	{
		get
		{
			if (!NzbDirIsCustom)
			{
				return DefaultNzbDir;
			}
			return Settings.Default.NzbGetNzbDir;
		}
	}

	public static bool QueueDirIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetQueueDir.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetQueueDir.Equals("-");
			}
			return false;
		}
	}

	public static string QueueDir
	{
		get
		{
			string obj = (QueueDirIsCustom ? Settings.Default.NzbGetQueueDir : DefaultQueueDir);
			AppHelper.EnsureDirectoryExist(obj);
			return obj;
		}
	}

	public static string QueueFile => Path.Combine(QueueDir, "queue.snet");

	public static string NzbGetQueueDir
	{
		get
		{
			if (!QueueDirIsCustom)
			{
				return Path.Combine(AppHelper.SettingsFolder, "nzbget/queue");
			}
			return Settings.Default.NzbGetQueueDir;
		}
	}

	public static bool Server1HostIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetServer1Host.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetServer1Host.Equals("-");
			}
			return false;
		}
	}

	public static string Server1Host
	{
		get
		{
			if (!Server1HostIsCustom)
			{
				return DefaultServer1Host;
			}
			return Settings.Default.NzbGetServer1Host;
		}
	}

	public static bool Server1PortIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetServer1Port.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetServer1Port.Equals("-");
			}
			return false;
		}
	}

	public static string Server1Port
	{
		get
		{
			if (!Server1PortIsCustom)
			{
				return DefaultServer1Port;
			}
			return Settings.Default.NzbGetServer1Port;
		}
	}

	public static bool Server1UsernameIsCustom => !Settings.Default.NzbGetServer1Username.Equals("-");

	public static string Server1Username
	{
		get
		{
			if (!Server1UsernameIsCustom)
			{
				return DefaultServer1Username;
			}
			return Settings.Default.NzbGetServer1Username;
		}
	}

	public static bool Server1PasswordIsCustom => !Settings.Default.NzbGetServer1Password.Equals("-");

	public static string Server1Password
	{
		get
		{
			if (!Server1PasswordIsCustom)
			{
				return DefaultServer1Password;
			}
			return Settings.Default.NzbGetServer1Password;
		}
	}

	public static bool Server1EncryptionIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetServer1Encryption.Equals("yes"))
			{
				return Settings.Default.NzbGetServer1Encryption.Equals("no");
			}
			return true;
		}
	}

	public static string Server1Encryption
	{
		get
		{
			if (!Server1EncryptionIsCustom)
			{
				return DefaultServer1Encryption;
			}
			return Settings.Default.NzbGetServer1Encryption;
		}
	}

	public static string Server1Connections => AppHelper.GetServer(ServerType.Download).Connections.ToString();

	public static bool ControlIpIsCustom
	{
		get
		{
			if (!Settings.Default.NzbGetControlIP.IsNullOrWhiteSpace())
			{
				return !Settings.Default.NzbGetControlIP.Equals("-");
			}
			return false;
		}
	}

	public static string ControlIp
	{
		get
		{
			if (!ControlIpIsCustom)
			{
				return "127.0.0.1";
			}
			return Settings.Default.NzbGetControlIP;
		}
	}

	public static bool ControlPortIsCustom
	{
		get
		{
			if (int.TryParse(Settings.Default.NzbGetControlPort, out var result) && result > 0)
			{
				return result < 65536;
			}
			return false;
		}
	}

	public static string ControlPort
	{
		get
		{
			if (!ControlPortIsCustom)
			{
				return "6789";
			}
			return Settings.Default.NzbGetControlPort;
		}
	}

	public static bool ControlUsernameIsCustom => !Settings.Default.NzbGetControlUsername.Equals("-");

	public static string ControlUsername
	{
		get
		{
			if (!ControlUsernameIsCustom)
			{
				return "nzbget";
			}
			return Settings.Default.NzbGetControlUsername;
		}
	}

	public static bool ControlPasswordIsCustom => !Settings.Default.NzbGetControlPassword.Equals("-");

	public static string ControlPassword
	{
		get
		{
			if (!ControlPasswordIsCustom)
			{
				return "tegbzn6789";
			}
			return Settings.Default.NzbGetControlPassword;
		}
	}

	private static string GetFolder(Guid pGuid)
	{
		string result = "";
		IntPtr path = default(IntPtr);
		if (SHGetKnownFolderPath(ref pGuid, 0u, IntPtr.Zero, ref path) == 0)
		{
			result = Marshal.PtrToStringUni(path);
			Marshal.FreeCoTaskMem(path);
		}
		return result;
	}

	[DllImport("shell32", CharSet = CharSet.Ansi, SetLastError = true)]
	private static extern int SHGetKnownFolderPath(ref Guid knownFolder, uint flags, IntPtr htoken, ref IntPtr path);
}
