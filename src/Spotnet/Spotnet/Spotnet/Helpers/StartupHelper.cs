using System;
using System.Reflection;
using Microsoft.Win32;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

/// <summary>
/// Adds and removes the entry that starts Spotnet when Windows starts, behind the
/// "Automatisch opstarten" checkbox on the Common settings page.
/// </summary>
/// <remarks>
/// The app ships two ways and each needs its own mechanism, because only one of them has a
/// stable executable path:
/// <para>
/// An Inno install (<see cref="Deployment.InstalledProfile" />) lives at one fixed path for
/// its whole life, so a plain <c>HKCU\...\Run</c> value is enough. It is rewritten on every
/// launch by <see cref="SyncRunValue" />, so an install that was moved or repaired heals
/// itself instead of leaving a dead entry that Windows reports as a failed startup app.
/// </para>
/// <para>
/// A Squirrel install lives in a versioned folder (<c>app-3.0.16.0\</c>) that is replaced on
/// every update, so a Run value pointing at the exe would break at the first upgrade. Those
/// get a shortcut in the Startup folder instead, created through Squirrel itself so it is
/// owned by the same mechanism that owns the Start Menu and Desktop shortcuts.
/// <see cref="Deployment.SquirrelStuff" /> re-points it at the new version during the
/// post-update launch.
/// </para>
/// </remarks>
internal static class StartupHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string RunValueName = "Spotnet";

	/// <summary>
	/// The exe Windows should launch. <see cref="Environment.ProcessPath" /> is preferred over
	/// <see cref="Assembly.Location" /> because the latter is empty for a single-file publish.
	/// </summary>
	private static string ExecutablePath
	{
		get
		{
			string path = Environment.ProcessPath;
			return path.IsNullOrEmpty() ? Assembly.GetExecutingAssembly().Location : path;
		}
	}

	/// <summary>The command line to store: a quoted path, quoted again if it holds spaces.</summary>
	private static string RunCommand
	{
		get
		{
			string path = ExecutablePath;
			if (path.IsNullOrEmpty())
			{
				return null;
			}
			return path.Contains(' ') ? "\"" + path + "\"" : path;
		}
	}

	/// <summary>
	/// Turns autostart on or off. Returns <see langword="false" /> when the registration could
	/// not be changed, so the caller leaves the stored setting alone instead of claiming a
	/// state Windows does not have.
	/// </summary>
	internal static bool SetEnabled(bool enabled)
	{
		try
		{
			return Deployment.InstalledProfile.Enabled
				? WriteRunValue(enabled)
				: Deployment.SquirrelStuff.SetStartupShortcut(enabled);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	/// <summary>
	/// Re-points the Run value at the exe that is actually running. Called on startup for Inno
	/// installs; a no-op anywhere else, including when autostart is off.
	/// </summary>
	internal static void SyncRunValue()
	{
		if (!Deployment.InstalledProfile.Enabled || !Properties.Settings.Default.RunAtStartup)
		{
			return;
		}

		try
		{
			string wanted = RunCommand;
			if (wanted == null)
			{
				return;
			}

			using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
			if (key != null && !wanted.Equals(key.GetValue(RunValueName) as string, StringComparison.OrdinalIgnoreCase))
			{
				key.SetValue(RunValueName, wanted, RegistryValueKind.String);
				Log.Debug("Run-at-startup entry refreshed to {0}", wanted);
			}
		}
		catch (Exception ex)
		{
			// Startup is the wrong moment to interrupt the user: the checkbox still reports the
			// intent, and the next launch retries.
			Log.Warn(ex, "Failed to refresh the run-at-startup entry");
		}
	}

	private static bool WriteRunValue(bool enabled)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
		if (key == null)
		{
			Log.Error("Cannot open {0}", RunKeyPath);
			return false;
		}

		if (enabled)
		{
			string command = RunCommand;
			if (command == null)
			{
				Log.Error("Cannot determine the executable path for the run-at-startup entry");
				return false;
			}
			key.SetValue(RunValueName, command, RegistryValueKind.String);
		}
		else
		{
			key.DeleteValue(RunValueName, throwOnMissingValue: false);
		}

		Log.Debug("Run at startup {0}", enabled ? "enabled" : "disabled");
		return true;
	}
}
