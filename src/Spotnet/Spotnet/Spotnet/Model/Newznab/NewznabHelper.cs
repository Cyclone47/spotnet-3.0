using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.ViewModel;

namespace Spotnet.Model.Newznab;

public static class NewznabHelper
{
	private const string ApiKey = "dc08a7bb0371bee90a767a822e68cb07";

	private const string AttrsToRequest = "size,guid,poster,usenetdate";

	private const string QueryJsonSearchCat = "http://51.15.59.166/api?o=json&t=search&attrs=size,guid,poster,usenetdate&apikey=dc08a7bb0371bee90a767a822e68cb07&cat={0}&limit={1}&offset={2}";

	private const string QueryJsonDetails = "http://51.15.59.166/api?o=json&t=details&apikey=dc08a7bb0371bee90a767a822e68cb07&id={0}";

	private static readonly Logger Log;

	internal static NewznabSpotsCache Cache;

	internal static readonly Dictionary<int, string> Categories;

	private static readonly Dictionary<int, string> ErrorCodes;

	private const int MinDatabaseSpotId = 100000000;

	private static int _id;

	private static string MessageIdPrefix;

	static NewznabHelper()
	{
		Log = LogManager.GetCurrentClassLogger();
		Cache = new NewznabSpotsCache();
		Categories = new Dictionary<int, string>
		{
			{ 1000, "Console" },
			{ 1010, "Console/NDS" },
			{ 1020, "Console/PSP" },
			{ 1040, "Console/XBox" },
			{ 1050, "Console/XBox 360" },
			{ 1070, "Console/XBox 360 DLC" },
			{ 1030, "Console/Wii" },
			{ 1060, "Console/Wiiware" },
			{ 2000, "Movies" },
			{ 2010, "Movies/Foreign" },
			{ 2030, "Movies/SD" },
			{ 2040, "Movies/HD" },
			{ 2050, "Movies/BluRay" },
			{ 2060, "Movies/3D" },
			{ 2020, "Movies/Other" },
			{ 3000, "Audio" },
			{ 3010, "Audio/MP3" },
			{ 3020, "Audio/Video" },
			{ 3030, "Audio/Audiobook" },
			{ 3040, "Audio/Lossless" },
			{ 4000, "PC" },
			{ 4010, "PC/0day" },
			{ 4020, "PC/ISO" },
			{ 4030, "PC/Mac" },
			{ 4050, "PC/Games" },
			{ 4060, "PC/Mobile-iOS" },
			{ 4070, "PC/Mobile-Android" },
			{ 4040, "PC/Mobile-Other" },
			{ 5000, "TV" },
			{ 5020, "TV/Foreign" },
			{ 5030, "TV/SD" },
			{ 5040, "TV/HD" },
			{ 5060, "TV/Sport" },
			{ 5050, "TV/Other" },
			{ 7000, "Other" },
			{ 7010, "Misc" },
			{ 7020, "EBook" },
			{ 7030, "Comics" },
			{ 100000, "Custom" }
		};
		ErrorCodes = new Dictionary<int, string>
		{
			{ 100, "Incorrect user credentials" },
			{ 101, "Account suspended" },
			{ 102, "Insufficient privileges/not authorized" },
			{ 103, "Registration denied" },
			{ 104, "Registrations are closed" },
			{ 105, "Invalid registration (Email Address Taken)" },
			{ 106, "Invalid registration (Email Address Bad Format)" },
			{ 107, "Registration Failed (Data error)" },
			{ 200, "Missing parameter" },
			{ 201, "Incorrect parameter" },
			{ 202, "No such function (Function not defined in this specification)" },
			{ 203, "Function not available (Optional function is not implemented)" },
			{ 300, "No such item / item already exists" },
			{ 900, "Unknown error" },
			{ 910, "API disabled" }
		};
		_id = 100000000;
		MessageIdPrefix = "newznab:";
	}

	public static bool IsNewznabQuery(string query)
	{
		return GetCategoryFromQuery(query) >= 1000;
	}

	public static bool IsNewznabMessageId(string msgId)
	{
		return msgId.ToLower().StartsWith(MessageIdPrefix);
	}

	internal static IList<ISpotRow> ExecuteQuery(string query, int offset, int limit, out int overallCount, CancellationToken cancellationToken)
	{
		List<ISpotRow> list = Cache.Get(query, offset, limit, out overallCount);
		if (list != null)
		{
			return list;
		}
		int categoryFromQuery = GetCategoryFromQuery(query);
		if (categoryFromQuery <= 0)
		{
			Log.Error("Is not newznab query: " + query);
			return new List<ISpotRow>();
		}
		int num = 0;
		List<ISpotRow> list2 = new List<ISpotRow>();
		int num2 = offset;
		int num3 = limit;
		if (num3 > 100)
		{
			num3 = 100;
		}
		while (num3 > 0)
		{
			IList<ISpotRow> list3 = ExecuteQuery(categoryFromQuery, num3, num2, out overallCount, cancellationToken);
			if (list3 == null)
			{
				string text = "Newznab query result is null";
				Log.Error(text);
				AppHelper.Error(text);
				return list2;
			}
			int count = list3.Count;
			list2.AddRange(list3);
			num += count;
			if (offset + num == overallCount || offset + limit == num2 + num3)
			{
				break;
			}
			num2 += num3;
			if (num2 + num3 > offset + limit)
			{
				num3 = offset + limit - num2;
			}
		}
		if (list2.Any())
		{
			Cache.AddOrUpdate(query, offset, limit, overallCount, list2);
		}
		return list2;
	}

	private static int GetCategoryFromQuery(string query)
	{
		int result = -1;
		if (!query.IsNullOrWhiteSpace())
		{
			string text = query.ToLower().Trim().Replace(" ", "");
			if (text.StartsWith("cat="))
			{
				text = text.Substring(4);
				int.TryParse(text, out result);
			}
		}
		return result;
	}

	private static IList<ISpotRow> ExecuteQuery(int cat, int limit, int offset, out int overallCount, CancellationToken cancel)
	{
		string response;
		try
		{
			using WebClient webClient = new WebClient();
			string address = $"http://51.15.59.166/api?o=json&t=search&attrs=size,guid,poster,usenetdate&apikey=dc08a7bb0371bee90a767a822e68cb07&cat={cat}&limit={limit}&offset={offset}";
			response = webClient.DownloadString(address);
		}
		catch (WebException ex)
		{
			Log.Warn("Newznab server error on search (cat/limit/offset): {0}/{1}{2}. Error: {3}", cat, limit, offset, ex.Message);
			overallCount = 0;
			return null;
		}
		return ParseSearchResponse(response, cat, out overallCount, cancel);
	}

	private static List<ISpotRow> ParseSearchResponse(string response, int cat, out int overallCount, CancellationToken cancel)
	{
		List<ISpotRow> result = new List<ISpotRow>();
		overallCount = 0;
		if (cancel.IsCancellationRequested)
		{
			return result;
		}
		if (!response.IsNullOrEmpty())
		{
			JObject jObject = null;
			try
			{
				jObject = JsonConvert.DeserializeObject<JObject>(response);
			}
			catch (JsonReaderException)
			{
				string text = "Error on parsing newznab response: " + response;
				Log.Warn(text);
				AppHelper.Error(text);
			}
			if (jObject != null && jObject.GetValue("channel") is JObject jObject2)
			{
				if (jObject2.GetValue("response") is JObject jObject3 && jObject3.GetValue("@attributes") is JObject jObject4 && jObject4.GetValue("total") is JValue value)
				{
					int.TryParse(value.Value<string>(), out overallCount);
				}
				if (jObject2.GetValue("item") is JArray source)
				{
					result = (from o in source.OfType<JObject>()
						select GenerateSpot(o, cat)).ToList();
				}
			}
		}
		return result;
	}

	private static ISpotRow GenerateSpot(JObject item, int category)
	{
		string text = FindAttributeWithName(item, "size");
		long filesize = (text.IsNullOrEmpty() ? 0 : Convert.ToInt64(text));
		string text2 = FindAttributeWithName(item, "guid");
		string messageId = MessageIdPrefix + text2;
		DateTime date = DateTime.Parse(FindAttributeWithName(item, "usenetdate"));
		string posterName = GetPosterName(FindAttributeWithName(item, "poster"));
		string title = item.GetValue("title").ToString();
		string tag = item.GetValue("category").ToString().Replace(" ", "")
			.Replace(">", "");
		SpotRowChild spot = default(SpotRowChild);
		spot.ID = ++_id;
		spot.SubCat = 100;
		spot.ExtCat = 100;
		spot.Stamp = date.ToUnixTime();
		spot.Filesize = filesize;
		spot.Title = title;
		spot.Poster = posterName;
		spot.Tag = tag;
		spot.Modulus = "";
		spot.MessageId = messageId;
		spot.NumberOfSpamReports = 0;
		spot.Cat = category;
		spot.ValidSignature = true;
		SpotRowViewModel spotRowViewModel = SpotRowViewModel.InitializeNewSpotRow(spot);
		spotRowViewModel.PosterIdent = PosterIdentType.Verified;
		return spotRowViewModel;
	}

	private static string GetPosterName(string posterWithMail)
	{
		if (posterWithMail.IsNullOrWhiteSpace())
		{
			return "";
		}
		if (posterWithMail.Contains('<'))
		{
			return posterWithMail.Split('<')[0].Trim();
		}
		if (posterWithMail.Contains("("))
		{
			return posterWithMail.Split('(')[1].Replace(")", "").Trim();
		}
		return posterWithMail;
	}

	private static string FindAttributeWithName(JObject item, string attrName)
	{
		string result = null;
		if (!item.TryGetValue("attr", out var value))
		{
			return null;
		}
		if (value is JArray jArray)
		{
			foreach (JToken item2 in jArray)
			{
				if (item2 is JObject jObject && jObject.GetValue("@attributes") is JObject jObject2 && jObject2.GetValue("name") is JValue value2 && value2.Value<string>().Equals(attrName))
				{
					result = ((JValue)jObject2.GetValue("value")).Value<string>();
				}
			}
		}
		return result;
	}

	private static SpotEx ParseDetailsResponse(string response)
	{
		if (response.IsNullOrEmpty())
		{
			return null;
		}
		JObject jObject;
		try
		{
			jObject = JsonConvert.DeserializeObject<JObject>(response);
		}
		catch (JsonReaderException)
		{
			string text = "Error on parsing newznab response: " + response;
			Log.Warn(text);
			AppHelper.Error(text);
			return null;
		}
		if (jObject == null)
		{
			return null;
		}
		if (!(jObject.GetValue("channel") is JObject jObject2))
		{
			return null;
		}
		if (!(jObject2.GetValue("item") is JObject jObject3))
		{
			return null;
		}
		string text2 = FindAttributeWithName(jObject3, "size");
		long filesize = (text2.IsNullOrEmpty() ? 0 : Convert.ToInt64(text2));
		string text3 = FindAttributeWithName(jObject3, "guid");
		string messageId = MessageIdPrefix + text3;
		DateTime date = DateTime.Parse(FindAttributeWithName(jObject3, "usenetdate"));
		string posterName = GetPosterName(FindAttributeWithName(jObject3, "poster"));
		string title = jObject3.GetValue("title").ToString();
		string tag = jObject3.GetValue("category").ToString().Replace(" ", "")
			.Replace(">", "");
		string body = jObject3.GetValue("description").ToString();
		int.TryParse(FindAttributeWithName(jObject3, "category"), out var result);
		string web = jObject3.GetValue("guid").ToString();
		string nZB = jObject3.GetValue("link").ToString();
		UserInfo user = new UserInfo
		{
			Organisation = "newznab",
			ValidSignature = true
		};
		return new SpotEx
		{
			Article = ++_id,
			Body = body,
			Category = result,
			Filesize = filesize,
			Image = "",
			ImageSource = null,
			Modulus = "",
			NumberOfSpamReports = 0,
			Stamp = date.ToUnixTime(),
			Title = title,
			Poster = posterName,
			MessageId = messageId,
			PosterIdent = PosterIdentType.Verified,
			KeyID = 7,
			Newsreader = "Newznab",
			SubCat = 100,
			Web = web,
			NZB = nZB,
			Tag = tag,
			SubCats = "",
			User = user,
			OldInfo = null
		};
	}

	public static bool GetSpot(string messageId, string postString, ref SpotEx spotOut, ref string errorMsg)
	{
		string arg = messageId.Substring(MessageIdPrefix.Length);
		string response;
		try
		{
			using WebClient webClient = new WebClient();
			string address = $"http://51.15.59.166/api?o=json&t=details&apikey=dc08a7bb0371bee90a767a822e68cb07&id={arg}";
			response = webClient.DownloadString(address);
		}
		catch (WebException ex)
		{
			Log.Warn("Newznab server error on details: " + ex.Message);
			response = null;
		}
		spotOut = ParseDetailsResponse(response);
		return spotOut != null;
	}
}
