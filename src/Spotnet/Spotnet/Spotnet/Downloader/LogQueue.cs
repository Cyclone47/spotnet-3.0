using System;
using NLog;
using System.IO;
using Spotnet.Helpers;

namespace Spotnet.Downloader;

public class LogQueue
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public readonly int Id;

	public readonly object LockWriteToTheLog = new object();

	public bool HasWarnings { get; private set; }

	public bool HasFatals { get; private set; }

	public string LastWarning { get; set; }

	public string LastFatal { get; set; }

	public string LogPath => Path.Combine(DownloaderProps.QueueDir, Id + ".log");

	public LogQueue(int id)
	{
		Id = id;
	}

	private void Add(LogLevel logLevel, DateTime dateTime, string message)
	{
		LogMessage message2 = new LogMessage(logLevel, dateTime, message);
		Log.Log(logLevel, "[{0}] {1}", Id, message);
		SaveToLogFile(message2);
	}

	private void SaveToLogFile(LogMessage message)
	{
		try
		{
			string logPath = LogPath;
			string contents = string.Concat(message, "\r\n");
			lock (LockWriteToTheLog)
			{
				File.AppendAllText(logPath, contents);
			}
		}
		catch (Exception ex)
		{
			Log.Debug("Exception on write to download log");
			Log.Exception(ex);
		}
	}

	public void Debug(string message)
	{
		Add(LogLevel.Debug, DateTime.Now, message);
	}

	public void Warn(string message)
	{
		HasWarnings = true;
		LastWarning = message;
		Add(LogLevel.Warn, DateTime.Now, message);
	}

	public void Fatal(string message)
	{
		HasFatals = true;
		LastFatal = message;
		Add(LogLevel.Fatal, DateTime.Now, message);
	}
}
