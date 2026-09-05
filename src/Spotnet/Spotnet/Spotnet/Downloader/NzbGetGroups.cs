using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.Downloader;

public class NzbGetGroups
{
	public bool OnGlobalPause { get; private set; }

	public List<NzbGetGroup> Groups { get; private set; }

	public List<NzbGetGroup> HistoryGroups { get; private set; }

	public NzbGetGroups()
	{
		if (Sys.Downloader is NzbGetDownloader nzbGetDownloader)
		{
			JObject jObject = nzbGetDownloader.JsonRpcRequest("listgroups", default(TimeSpan), 0);
			if (jObject == null)
			{
				throw new ExternalException("listgroups response is null");
			}
			JToken value = jObject.GetValue("result");
			if (value == null)
			{
				throw new ExternalException("items result error: " + jObject.ToErrorString());
			}
			JObject jObject2 = nzbGetDownloader.JsonRpcRequest("history", default(TimeSpan), false);
			if (jObject2 == null)
			{
				throw new ExternalException("history response is null");
			}
			JToken value2 = jObject2.GetValue("result");
			if (value2 == null)
			{
				throw new ExternalException("historyItems result error: " + jObject2.ToErrorString());
			}
			JObject jObject3 = nzbGetDownloader.JsonRpcRequest("status", default(TimeSpan));
			if (jObject3 == null)
			{
				throw new ExternalException("status response is null");
			}
			JObject status = jObject3.GetValue("result") as JObject;
			if (status == null)
			{
				throw new ExternalException("status result error: " + jObject3.ToErrorString());
			}
			OnGlobalPause = status.GetValue("DownloadPaused").Value<bool>();
			Groups = value.Select((JToken i) => new NzbGetGroup(i as JObject, status, isHistory: false)).ToList();
			HistoryGroups = value2.Select((JToken i) => new NzbGetGroup(i as JObject, null, isHistory: true)).ToList();
		}
	}
}
