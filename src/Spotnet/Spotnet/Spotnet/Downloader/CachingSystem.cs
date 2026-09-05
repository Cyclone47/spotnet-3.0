using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Downloader;

public static class CachingSystem
{
	public const int PipelinedToMasterSize = 100;

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static string MasterHostnameSnelNl = "cache.snelnl.com";

	public static string MasterHostname5Euro = "cache.usenetsys.com";

	public static bool IsEnabled = Settings.Default.IsCachingEnabled;

	private static readonly object LockSlavePhuses = new object();

	private static readonly Dictionary<string, Engine> SlavePhuses = new Dictionary<string, Engine>();

	private static bool _isInfoMessageShown;

	private static readonly ConcurrentDictionary<string, DateTime> HostStartToProcessDateTime = new ConcurrentDictionary<string, DateTime>();

	internal static bool DoesSlaveUseSslCalculated;

	private static bool _doesSlaveUseSsl;

	private static int _numberOfBridgeServers = 2;

	private static bool _isBridgedModeOn;

	private const int BridgeModeConnectionsNumber = 20;

	public const int MasterConnections = 2;

	public static bool IsBridgedModeOn
	{
		get
		{
			return _isBridgedModeOn;
		}
		set
		{
			_isBridgedModeOn = value;
			if (value)
			{
				DownloadQueue.ChangeDownloadThreadsNumber(20);
			}
		}
	}

	public static int NumberOfBridgeServers
	{
		get
		{
			return _numberOfBridgeServers;
		}
		set
		{
			int num = ((value >= 1) ? value : 2);
			if (_numberOfBridgeServers != num)
			{
				_numberOfBridgeServers = num;
				Log.Debug("Number of bridge servers: " + _numberOfBridgeServers);
			}
		}
	}

	public static bool GetBody(string group, NNTPSegment segment, out Stream resp, out int resCode, out string errorMsg)
	{
		if (segment == null || segment.SlaveHostname.IsNullOrEmpty())
		{
			resp = null;
			resCode = -1;
			errorMsg = ((segment == null) ? "Segment is null" : ("Info about slave is absent for the article: " + segment.MessageId));
			return false;
		}
		if (IsHostUnderTimeout(segment.SlaveHostname))
		{
			resp = null;
			resCode = -1;
			errorMsg = "Slave under timeout";
			return false;
		}
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(GetSlavePhuse(segment.SlaveHostname));
		string messageId = SpotHelper.MakeMsg(segment.MessageId) + segment.BridgedInfo;
		bool body = nNTP.GetBody(group, messageId, out resp, out resCode, out errorMsg);
		if (!body)
		{
			ProcessError(resCode, errorMsg, segment.SlaveHostname);
		}
		return body;
	}

	private static void ProcessError(int code, string errorMsg, string host)
	{
		if (errorMsg.Equals("Removed") || errorMsg.Equals("Cancelled"))
		{
			return;
		}
		Log.Debug("Cache[{0}]: {1}", host.Equals("master") ? "M" : "S", errorMsg);
		switch (code)
		{
		case 381:
		case 400:
		case 450:
		case 452:
		case 480:
		case 481:
		case 482:
		case 502:
			if (!errorMsg.Contains("connection"))
			{
				SetHostTimeout(host, TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec * 5));
			}
			break;
		case 931:
		case 941:
		case 950:
		case 952:
		case 995:
			SetHostTimeout(host, TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec));
			break;
		}
	}

	private static bool DoesSlaveUseSSL(ServerInfo server)
	{
		if (!DoesSlaveUseSslCalculated)
		{
			_doesSlaveUseSsl = server.DoesProviderUseSsl();
			DoesSlaveUseSslCalculated = true;
		}
		return _doesSlaveUseSsl;
	}

	private static Engine GetSlavePhuse(string slaveHostname)
	{
		lock (LockSlavePhuses)
		{
			if (!SlavePhuses.TryGetValue(slaveHostname, out var value))
			{
				SlavePhuses.Add(slaveHostname, null);
				ServerInfo server = AppHelper.GetServer(ServerType.SlaveCache);
				server.Server = slaveHostname;
				server.SSL = DoesSlaveUseSSL(server);
				server.Connections = (int)Math.Ceiling(20.0 / (double)NumberOfBridgeServers);
				value = AppHelper.CreatePhuse(server, isSlave: true);
				SlavePhuses[slaveHostname] = value;
			}
			return value;
		}
	}

	public static void ClearSlavePhuses(string slaveName = null)
	{
		lock (LockSlavePhuses)
		{
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, Engine> slavePhuse in SlavePhuses)
			{
				if (slaveName == null || slavePhuse.Key.Equals(slaveName))
				{
					try
					{
						slavePhuse.Value.Close();
						list.Add(slavePhuse.Key);
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
					}
				}
			}
			foreach (string item in list)
			{
				SlavePhuses.Remove(item);
			}
		}
	}

	public static bool IsCacheServer(string hostname)
	{
		if (!IsCacheMaster(hostname))
		{
			return IsCacheSlave(hostname);
		}
		return true;
	}

	public static bool IsCacheMaster(string hostname)
	{
		if (!hostname.Equals(MasterHostnameSnelNl))
		{
			return hostname.Equals(MasterHostname5Euro);
		}
		return true;
	}

	public static bool IsCacheSlave(string hostname)
	{
		lock (LockSlavePhuses)
		{
			return SlavePhuses.ContainsKey(hostname);
		}
	}

	private static void SetHostTimeout(string host, TimeSpan timeout)
	{
		if (!Sys.IsShutdownRequested)
		{
			Log.Debug("Pause cache[{0}] for {1} sec", host.Equals("master") ? "M" : "S", timeout.TotalSeconds);
			DateTime newTime = DateTime.Now + timeout;
			HostStartToProcessDateTime.AddOrUpdate(host, newTime, (string s, DateTime time) => newTime);
			if (host.Equals("master"))
			{
				AppHelper.ClearMasterCachePhuse();
			}
			else
			{
				AppHelper.ClearSlavesCachePhuse(host);
			}
		}
	}

	private static bool IsHostUnderTimeout(string host)
	{
		if (!HostStartToProcessDateTime.TryGetValue(host, out var value))
		{
			return false;
		}
		return value > DateTime.Now;
	}

	public static bool GetSlaves(List<NNTPSegment> pipeliningSegments, int port = -1)
	{
		if (!pipeliningSegments.Any())
		{
			return true;
		}
		if (IsHostUnderTimeout("master"))
		{
			return false;
		}
		if (port > 0)
		{
			Log.Debug("Switch master cache to port " + port);
			AppHelper.ClearMasterCachePhuse();
			AppHelper.ServersDb.OMasterCache.Port = port;
			Thread.Sleep(100);
		}
		Engine masterCachePhuse = AppHelper.MasterCachePhuse;
		if (masterCachePhuse == null)
		{
			return false;
		}
		if (!_isInfoMessageShown)
		{
			_isInfoMessageShown = true;
			Log.Debug("Cache support enabled");
		}
		List<NNTPSegment> list = pipeliningSegments.Where((NNTPSegment segment) => segment.SlaveHostname.IsNullOrEmpty() && !segment.IsFailed).ToList();
		if (!list.Any())
		{
			return true;
		}
		if (!new Spotnet.Model.NNTP(masterCachePhuse).GetBodies("none", list, out var resp, out var resCode, out var _))
		{
			if (resCode == 2000 && port < 0)
			{
				Log.Debug("AVG usenet commands block problem detected");
				return GetSlaves(pipeliningSegments, 80);
			}
			SetHostTimeout("master", TimeSpan.FromSeconds(10.0));
			return false;
		}
		string[] array = resp.Split(new string[1] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != list.Count)
		{
			Log.Debug($"Lines count is wrong: {array.Length}/{list.Count}");
			SetHostTimeout("master", TimeSpan.FromSeconds(10.0));
			return false;
		}
		try
		{
			int num = 0;
			foreach (NNTPSegment item in list)
			{
				string input = array[num];
				num++;
				Match match = Regex.Match(input, "^430 Slave = (.+)$");
				if (match.Success)
				{
					string value = match.Groups[1].Value;
					if (AppHelper.IsDomainName(value) || AppHelper.IsIp(value))
					{
						item.SlaveHostname = value;
					}
					continue;
				}
				match = Regex.Match(input, "^201 Data = (.+):(.+):(.+)$");
				if (match.Success)
				{
					string value2 = match.Groups[1].Value;
					string value3 = match.Groups[2].Value;
					string value4 = match.Groups[3].Value;
					if (AppHelper.IsDomainName(value3) || AppHelper.IsIp(value3))
					{
						item.SlaveHostname = value3;
						item.BridgedInfo = ":" + value2 + ":" + value3 + ":" + value4;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			SetHostTimeout("master", TimeSpan.FromSeconds(10.0));
			return false;
		}
		return true;
	}
}
