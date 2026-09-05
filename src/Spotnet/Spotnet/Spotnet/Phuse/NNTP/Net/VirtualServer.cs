using System;
using System.Collections.Concurrent;
using System.Xml;
using NLog;

namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualServer : IndexedObject
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly ConcurrentQueue<string> _debugLog = new ConcurrentQueue<string>();

	private readonly ConcurrentQueue<string> _statusLog = new ConcurrentQueue<string>();

	public string Host { get; }

	internal bool SSL { get; }

	internal int Port { get; }

	internal string Username { get; }

	internal string Password { get; }

	internal int MaxConnectionsAllowed { get; }

	internal ServerPriority Priority { get; }

	internal Connections Connections { get; }

	public string LogFormat => LStatus + Environment.NewLine + LDebug;

	private string LDebug => Module.ReadLog(_debugLog, 500);

	private string LStatus => Module.ReadLog(_statusLog, 500);

	public int ID { get; set; }

	public int Index { get; set; }

	internal VirtualServer(Connections lConnections, string host, string username = "", string password = "", int port = 119, int connections = 1, bool ssl = false, ServerPriority priority = ServerPriority.Default)
	{
		SSL = ssl;
		Host = host;
		Port = port;
		Connections = lConnections;
		Priority = priority;
		MaxConnectionsAllowed = connections;
		Username = username ?? "";
		if (password != null)
		{
			Password = password;
		}
		else
		{
			Password = "";
		}
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	internal void WriteDebug(string sCode, string sMsg)
	{
		_debugLog.Enqueue(Module.MakeMsg(sCode, sMsg));
	}

	internal void WriteStatus(string sMsg)
	{
		_statusLog.Enqueue(Module.MakeMsg("000", sMsg));
	}

	internal void LogError(int commandID, NNTPError zErr)
	{
		WriteStatus("Command #" + Convert.ToString(commandID) + " - Error " + Module.MakeErr(zErr));
	}

	internal bool WriteXml(XmlWriter xR)
	{
		return ApiXML.Server(xR, this);
	}
}
