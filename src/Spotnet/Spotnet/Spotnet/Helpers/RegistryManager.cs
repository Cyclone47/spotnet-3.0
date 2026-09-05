using System;
using System.Security;
using Microsoft.Win32;
using NLog;

namespace Spotnet.Helpers;

internal static class RegistryManager
{
	private const string RegistryRoot = "Spotnet";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static string GetKeyValueCu(string keyName, bool tryToCreateIfRootNotExist = true)
	{
		RegistryKey registryKey = null;
		try
		{
			registryKey = Registry.CurrentUser.OpenSubKey("Software\\Spotnet") ?? Registry.CurrentUser.OpenSubKey("Software\\Wow6432Node\\Spotnet");
		}
		catch (SecurityException)
		{
		}
		if (registryKey == null)
		{
			if (tryToCreateIfRootNotExist)
			{
				try
				{
					Registry.CurrentUser.CreateSubKey("Software\\Spotnet");
					return GetKeyValueCu(keyName, tryToCreateIfRootNotExist: false);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2.Message);
				}
			}
			Log.Error("Failed to get registry root CU");
			return null;
		}
		return (string)registryKey.GetValue(keyName);
	}

	public static void SetKeyValueCu(string keyName, string keyValue, bool tryToCreateIfRootNotExist = true)
	{
		RegistryKey registryKey = null;
		try
		{
			registryKey = Registry.CurrentUser.OpenSubKey("Software\\Spotnet", writable: true) ?? Registry.CurrentUser.OpenSubKey("Software\\Wow6432Node\\Spotnet", writable: true);
		}
		catch (SecurityException)
		{
		}
		if (registryKey == null)
		{
			if (tryToCreateIfRootNotExist)
			{
				try
				{
					Registry.CurrentUser.CreateSubKey("Software\\Spotnet");
					SetKeyValueCu(keyName, keyValue, tryToCreateIfRootNotExist: false);
					return;
				}
				catch (Exception ex2)
				{
					Log.Error(ex2.Message);
				}
			}
			Log.Error("Failed to set registry root CU");
		}
		else
		{
			registryKey.SetValue(keyName, keyValue, RegistryValueKind.String);
		}
	}
}
