using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spotnet.Helpers;

internal static class WindowActivatedHelper
{
	public static bool ApplicationIsActivated()
	{
		IntPtr foregroundWindow = GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return false;
		}
		int id = Process.GetCurrentProcess().Id;
		GetWindowThreadProcessId(foregroundWindow, out var processId);
		return processId == id;
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);
}
