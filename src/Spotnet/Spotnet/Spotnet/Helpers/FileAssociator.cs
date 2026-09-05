using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NLog;

namespace Spotnet.Helpers;

internal class FileAssociator
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static void SetAssociation(string extension, string keyName, string openWith, string fileDescription, string iconPath, string appFriendlyName)
	{
		try
		{
			Log.Debug("Associate {0} with Spotnet", extension);
			RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software", writable: true);
			registryKey.CreateSubKey("Classes");
			RegistryKey registryKey2 = registryKey.OpenSubKey("Classes", writable: true);
			registryKey2.DeleteSubKeyTree(extension, throwOnMissingSubKey: false);
			registryKey2.CreateSubKey(extension);
			RegistryKey registryKey3 = registryKey2.OpenSubKey(extension, writable: true);
			registryKey3.SetValue("", keyName);
			registryKey3.Close();
			RegistryKey registryKey4 = Registry.CurrentUser.OpenSubKey("Software", writable: true);
			registryKey4.CreateSubKey("Classes");
			RegistryKey registryKey5 = registryKey4.OpenSubKey("Classes", writable: true);
			registryKey5.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
			registryKey5.CreateSubKey(keyName);
			RegistryKey registryKey6 = registryKey5.OpenSubKey(keyName, writable: true);
			registryKey6.SetValue("", fileDescription);
			registryKey6.SetValue("FriendlyTypeName", appFriendlyName);
			registryKey6.CreateSubKey("DefaultIcon");
			RegistryKey registryKey7 = registryKey6.OpenSubKey("DefaultIcon", writable: true);
			registryKey7.SetValue("", "\"" + iconPath + "\"");
			registryKey7.Close();
			RegistryKey registryKey8 = Registry.CurrentUser.OpenSubKey("Software", writable: true);
			registryKey8.CreateSubKey("Classes");
			RegistryKey registryKey9 = registryKey8.OpenSubKey("Classes", writable: true);
			registryKey9.CreateSubKey(keyName);
			RegistryKey registryKey10 = registryKey9.OpenSubKey(keyName, writable: true);
			registryKey10.CreateSubKey("shell");
			RegistryKey registryKey11 = registryKey10.OpenSubKey("shell", writable: true);
			registryKey11.CreateSubKey("open");
			RegistryKey registryKey12 = registryKey11.OpenSubKey("open", writable: true);
			registryKey12.CreateSubKey("command");
			RegistryKey registryKey13 = registryKey12.OpenSubKey("command", writable: true);
			registryKey13.SetValue("", openWith + " \"\\\"%1\\\"\"");
			registryKey13.Close();
			SHChangeNotify(134217728u, 0u, IntPtr.Zero, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public static void SetProtocolAssociation(string protocol, string openWith, string iconPath)
	{
		try
		{
			bool flag = false;
			string value = openWith + " \"\\\"%1\\\"\"";
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{protocol}\\shell\\open\\command");
				if (registryKey != null)
				{
					object value2 = registryKey.GetValue("");
					flag = value2 != null && ((string)value2).Equals(value);
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			if (flag)
			{
				return;
			}
			Log.Debug("Associate {0}: protocol with Spotnet", protocol);
			using (RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software", writable: true))
			{
				registryKey2.CreateSubKey("Classes");
				using RegistryKey registryKey3 = registryKey2.OpenSubKey("Classes", writable: true);
				registryKey3.DeleteSubKeyTree(protocol, throwOnMissingSubKey: false);
				registryKey3.CreateSubKey(protocol);
				using RegistryKey registryKey4 = registryKey3.OpenSubKey(protocol, writable: true);
				registryKey4.SetValue("", $"URL:{protocol[0].ToString().ToUpper()}{protocol.Substring(1, protocol.Length - 1)} Protocol");
				registryKey4.SetValue("URL Protocol", "");
				registryKey4.CreateSubKey("DefaultIcon");
				using (RegistryKey registryKey5 = registryKey4.OpenSubKey("DefaultIcon", writable: true))
				{
					registryKey5.SetValue("", "\"" + iconPath + "\"");
				}
				registryKey4.CreateSubKey("shell");
				using RegistryKey registryKey6 = registryKey4.OpenSubKey("shell", writable: true);
				registryKey6.CreateSubKey("open");
				using RegistryKey registryKey7 = registryKey6.OpenSubKey("open", writable: true);
				registryKey7.CreateSubKey("command");
				using RegistryKey registryKey8 = registryKey7.OpenSubKey("command", writable: true);
				registryKey8.SetValue("", openWith + " \"\\\"%1\\\"\"");
			}
			SHChangeNotify(134217728u, 0u, IntPtr.Zero, IntPtr.Zero);
		}
		catch (Exception ex2)
		{
			Log.Exception(ex2);
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
