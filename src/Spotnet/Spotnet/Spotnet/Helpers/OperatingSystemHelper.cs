using System;
using System.Diagnostics;
using System.Management;
using NLog;

namespace Spotnet.Helpers;

public static class OperatingSystemHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static void ShutdownComputerNow()
	{
		Log.Debug("Shutdown PC");
		Process.Start(new ProcessStartInfo("shutdown", "/p /f")
		{
			CreateNoWindow = true,
			UseShellExecute = false
		});
	}

	public static ulong GetFreeSpaceOfPathInBytes(string path)
	{
		if (new Uri(path).IsUnc)
		{
			throw new NotImplementedException("Cannot find free space for UNC path " + path);
		}
		ulong result = 0uL;
		int num = 0;
		foreach (ManagementBaseObject item in new ManagementObjectSearcher("Select * from Win32_Volume").Get())
		{
			if (uint.Parse(item["DriveType"].ToString()) > 1 && item["Name"] != null && path.StartsWith(item["Name"].ToString(), StringComparison.OrdinalIgnoreCase))
			{
				int length = item["Name"].ToString().Length;
				if ((num == 0 || length > num) && item["FreeSpace"] != null)
				{
					result = ulong.Parse(item["FreeSpace"].ToString());
					num = item["Name"].ToString().Length;
				}
			}
		}
		if (num > 0)
		{
			return result;
		}
		throw new Exception("Could not find Volume Information for path " + path);
	}
}
