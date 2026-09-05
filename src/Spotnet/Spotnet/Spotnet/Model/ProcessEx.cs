using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NLog;

namespace Spotnet.Model;

public class ProcessEx : IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly Process _process = new Process();

	private readonly object _eventLock = new object();

	public bool IsTerminated { get; private set; }

	public int ExitCode => _process.ExitCode;

	public ProcessStartInfo StartInfo => _process.StartInfo;

	public event EventHandler<DataReceivedEventArgs> OutputDataReceived = delegate
	{
	};

	public event EventHandler<DataReceivedEventArgs> ErrorDataReceived = delegate
	{
	};

	public ProcessEx(string cmd, string workingDirectory = null)
	{
		ProcessStartInfo startInfo = _process.StartInfo;
		startInfo.FileName = "cmd.exe";
		startInfo.Arguments = "/C \"" + cmd + "\"";
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		_process.StartInfo.RedirectStandardInput = true;
		if (workingDirectory != null)
		{
			startInfo.WorkingDirectory = workingDirectory;
		}
		startInfo.CreateNoWindow = true;
		startInfo.WindowStyle = ProcessWindowStyle.Hidden;
		_process.OutputDataReceived += OnOutputDataReceived;
		_process.ErrorDataReceived += OnErrorDataReceived;
		IsTerminated = false;
	}

	public BinaryWriter GetInputBinaryWriter()
	{
		return new BinaryWriter(_process.StandardInput.BaseStream);
	}

	~ProcessEx()
	{
		Dispose(disposing: false);
	}

	public void Start()
	{
		try
		{
			_process.Start();
			_process.BeginErrorReadLine();
			_process.BeginOutputReadLine();
		}
		catch (Win32Exception ex)
		{
			if (_process.StartInfo.WorkingDirectory.Length > 250)
			{
				throw new Exception("Path is too long. Try to use shorter one: \n" + _process.StartInfo.WorkingDirectory);
			}
			throw new InvalidOperationException(ex.Message, ex);
		}
	}

	public void Kill()
	{
		_process.Kill();
		IsTerminated = true;
	}

	public void Wait()
	{
		_process.WaitForExit();
	}

	public void Wait(CancellationToken token)
	{
		using SafeWaitHandle safeWaitHandle = new SafeWaitHandle(_process.Handle, ownsHandle: false);
		using ManualResetEvent manualResetEvent = new ManualResetEvent(initialState: false);
		manualResetEvent.SafeWaitHandle = safeWaitHandle;
		if (WaitHandle.WaitAny(new WaitHandle[2] { manualResetEvent, token.WaitHandle }) == 1)
		{
			Kill();
			throw new OperationCanceledException();
		}
		_process.WaitForExit();
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (disposing)
		{
			_process.Dispose();
		}
	}

	private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
	{
		lock (_eventLock)
		{
			this.ErrorDataReceived(sender, e);
		}
	}

	private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
	{
		lock (_eventLock)
		{
			this.OutputDataReceived(sender, e);
		}
	}
}
