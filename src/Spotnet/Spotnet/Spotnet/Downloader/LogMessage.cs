using System;
using NLog;

namespace Spotnet.Downloader;

public struct LogMessage
{
	public readonly LogLevel LogLevel;

	public readonly DateTime DateTime;

	public readonly string Message;

	public LogMessage(LogLevel logLevel, DateTime dateTime, string message)
	{
		LogLevel = logLevel;
		DateTime = dateTime;
		Message = message;
	}

	public override string ToString()
	{
		return string.Format("{0}|{1}|{2}", DateTime.ToString("yyyy-MM-dd HH:mm:ss.ffff"), LogLevel.ToString().ToUpper(), Message);
	}
}
