using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Helpers;

internal static class OtherInstancesCommunicator
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static Mutex _mutex;

	public const string ExitOnUninstallCommand = "--exitOnUninstall";

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetForegroundWindow(IntPtr hwnd);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

	public static bool SetForeground(Process process)
	{
		IntPtr intPtr = FindWindow(null, "Spotnet :: Tray");
		if (IsWindow(intPtr))
		{
			ShowWindow(intPtr, 9);
			return SetForegroundWindow(intPtr);
		}
		intPtr = process.MainWindowHandle;
		if (IsWindow(intPtr))
		{
			ShowWindow(intPtr, 9);
			return SetForegroundWindow(intPtr);
		}
		return false;
	}

	internal static Process GetOtherInstance()
	{
		_mutex = new Mutex(initiallyOwned: true, "Local\\Spotnet", out var createdNew);
		Process result = null;
		if (!createdNew)
		{
			result = OtherSpotnetProcessesRunning().FirstOrDefault();
		}
		return result;
	}

	internal static bool TryToBringSpotnetToTheTop(Process otherProcess)
	{
		if (!SetForeground(otherProcess))
		{
			try
			{
				if (!_mutex.WaitOne(TimeSpan.FromSeconds(10.0), exitContext: false) && OtherSpotnetProcessesRunning().FirstOrDefault() != null)
				{
					AppHelper.Error(Words.ProcessSpotnetAlreadyExists, Words.Error);
					return true;
				}
			}
			catch (AbandonedMutexException)
			{
			}
			return false;
		}
		return true;
	}

	public static IEnumerable<Process> OtherSpotnetProcessesRunning()
	{
		Process currProc = Process.GetCurrentProcess();
		return from p in Process.GetProcesses()
			where p.ProcessName.Equals(currProc.ProcessName, StringComparison.Ordinal) && p.Id != currProc.Id
			select p;
	}

	public static IEnumerable<Process> OtherVlcProcessesRunning()
	{
		return from p in Process.GetProcesses()
			where p.ProcessName.Equals("vlc") && p.Id != 1
			select p;
	}

	public static void SendParamsToPipe(List<string> agrs)
	{
		try
		{
			NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "Pipe\\Spotnet", PipeDirection.Out, PipeOptions.Asynchronous);
			namedPipeClientStream.Connect(2000);
			using BinaryWriter binaryWriter = new BinaryWriter(namedPipeClientStream);
			foreach (string agr in agrs)
			{
				binaryWriter.Write(agr);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public static void SendExitCommandToPipe()
	{
		SendParamsToPipe(new List<string> { "--exitOnUninstall" });
	}
}
