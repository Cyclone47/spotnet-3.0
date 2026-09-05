using System;
using System.IO;
using NLog;
using Splat;
using Spotnet.Helpers;

namespace Spotnet.Deployment;

internal class SetupSplatLogger : Splat.ILogger, IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly object _gate = 42;

	private FileStream _fs;

	private StreamWriter _sw;

	public Splat.LogLevel Level { get; set; }

	public SetupSplatLogger()
	{
		try
		{
			string text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquirrelTemp\\SquirrelSetup.log");
			AppHelper.EnsureDirectoryExist(System.IO.Path.GetDirectoryName(text));
			if (!System.IO.File.Exists(text))
			{
				System.IO.File.WriteAllText(text, "");
			}
			long length = new System.IO.FileInfo(text).Length;
			_fs = ((length < 5248000) ? System.IO.File.Open(text, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite) : System.IO.File.Open(text, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
			_sw = new StreamWriter(_fs);
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
		}
	}

	public void Dispose()
	{
		if (_sw != null)
		{
			_sw.Dispose();
			_sw = null;
			_fs.Dispose();
			_fs = null;
		}
		GC.SuppressFinalize(this);
	}

	public void Write(string message, Splat.LogLevel logLevel)
	{
		if (_sw != null)
		{
			string value = $"{DateTime.Now:MM/dd/yy H:mm:ss zzz} [{logLevel}] {message}";
			lock (_gate)
			{
				_sw.WriteLine(value);
			}
		}
	}
}
