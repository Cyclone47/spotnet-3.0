using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Model.StatsReporter;

internal class GoogleAnalyticsStatsReporter : StatsReporter
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private DateTime _startTime;

	private string UserAgent
	{
		get
		{
			try
			{
				return "Spotnet/1.0 (" + base.OsVersion + ")";
			}
			catch
			{
				return "Spotnet/1.0 (+http://www.spot.net/)";
			}
		}
	}

	private Dictionary<string, string> BuildBasePayload()
	{
		return new Dictionary<string, string>
		{
			{ "v", "1" },
			{ "tid", "UA-53153749-1" },
			{
				"cid",
				AppHelper.HostUniqueId
			}
		};
	}

	private Dictionary<string, string> BuildStartPayload()
	{
		_startTime = DateTime.UtcNow;
		string value = $"{SystemParameters.VirtualScreenWidth}x{SystemParameters.VirtualScreenHeight}";
		ServerInfo server = AppHelper.GetServer(ServerType.Download);
		return new Dictionary<string, string>
		{
			{ "an", "Spotnet" },
			{
				"av",
				AppHelper.AppVersion.ToString()
			},
			{ "sr", value },
			{ "t", "event" },
			{ "ec", "All" },
			{ "ea", "Start" },
			{ "cd1", base.OsVersion },
			{ "cd3", server.Server },
			{
				"cd4",
				$"{server.Port}"
			},
			{
				"cd5",
				UserLanguageHelper.Language
			}
		};
	}

	private Dictionary<string, string> BuildEndPayload()
	{
		return new Dictionary<string, string>
		{
			{ "t", "event" },
			{ "ec", "All" },
			{ "ea", "Exit" },
			{
				"cm1",
				$"{(DateTime.UtcNow - _startTime).TotalMinutes:F0}"
			}
		};
	}

	private Dictionary<string, string> BuildOpenTabPayload(string messageId)
	{
		string value = AppHelper.Sha1(SpotHelper.MakeMsg(messageId, tag: false));
		return new Dictionary<string, string>
		{
			{ "t", "pageview" },
			{ "dh", "spot.net" },
			{ "dp", value },
			{ "dt", "spot" }
		};
	}

	private Dictionary<string, string> BuildSpotnetUpdateDownloadedPayload(Version version)
	{
		return new Dictionary<string, string>
		{
			{ "t", "event" },
			{ "ec", "All" },
			{ "ea", "Update" },
			{
				"cd6",
				version.ToString()
			}
		};
	}

	private Dictionary<string, string> BuildSpotnetUpdateFinishedPayload(Version version, bool isSuccess)
	{
		return new Dictionary<string, string>
		{
			{ "t", "event" },
			{ "ec", "All" },
			{ "ea", "Update" },
			{
				isSuccess ? "cd7" : "cd8",
				version.ToString()
			}
		};
	}

	private StringBuilder BuildPayload(Dictionary<string, string> basePayload, Dictionary<string, string> actionPayload)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(string.Join("&", basePayload.Select(delegate(KeyValuePair<string, string> x)
		{
			KeyValuePair<string, string> keyValuePair2 = x;
			string key2 = keyValuePair2.Key;
			keyValuePair2 = x;
			return key2 + "=" + Uri.EscapeDataString(keyValuePair2.Value);
		})));
		stringBuilder.Append('&');
		stringBuilder.Append(string.Join("&", actionPayload.Select(delegate(KeyValuePair<string, string> x)
		{
			KeyValuePair<string, string> keyValuePair = x;
			string key = keyValuePair.Key;
			keyValuePair = x;
			return key + "=" + Uri.EscapeDataString(keyValuePair.Value);
		})));
		return stringBuilder;
	}

	private HttpWebRequest BuildRequest(StringBuilder data)
	{
		HttpWebRequest obj = (HttpWebRequest)WebRequest.Create(string.Format("{0}?{1}", "https://ssl.google-analytics.com/collect", data));
		obj.UserAgent = UserAgent;
		return obj;
	}

	private void SendRequest(StringBuilder data)
	{
		using (BuildRequest(data).GetResponse())
		{
		}
	}

	private bool Send(Func<Dictionary<string, string>> func)
	{
		try
		{
			StringBuilder data = BuildPayload(BuildBasePayload(), func());
			SendRequest(data);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	protected override bool Send(bool startApp)
	{
		return Send(() => (!startApp) ? BuildEndPayload() : BuildStartPayload());
	}

	protected override bool SendOnSpotOpen(string messageId)
	{
		return Send(() => BuildOpenTabPayload(messageId));
	}

	protected override bool SendOnSpotnetUpdateDownloaded(Version version)
	{
		return Send(() => BuildSpotnetUpdateDownloadedPayload(version));
	}

	protected override bool SendOnSpotnetUpdatePerformed(Version version, bool isSuccess)
	{
		return Send(() => BuildSpotnetUpdateFinishedPayload(version, isSuccess));
	}
}
