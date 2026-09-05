using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Phuse;

internal static class SpotnetUpgradeNzb
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	internal static List<string> Download(Stream xml, string pathToDownload, Func<Spot, string, bool, bool> spotVerifier, out string errorMsg)
	{
		errorMsg = "";
		List<string> list = new List<string>();
		List<NNTPInput> list2 = Parse(xml);
		if (list2 == null)
		{
			errorMsg = "Empty files list";
			return list;
		}
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(AppHelper.HeaderPhuse);
		foreach (NNTPInput item in list2)
		{
			string path = System.IO.Path.GetFileName(AppHelper.GetTempFileName());
			Match match = new Regex("\\((.+)\\)").Match(item.Subject);
			if (match.Success)
			{
				path = match.Groups[1].Value;
			}
			string text = System.IO.Path.Combine(pathToDownload, path);
			string allGroups = string.Join(",", item.Groups);
			foreach (string group in item.Groups)
			{
				if (!errorMsg.IsNullOrEmpty())
				{
					Log.Debug("Update group: " + group + ". Error: " + errorMsg);
					errorMsg = "";
				}
				if (group.IsNullOrEmpty())
				{
					errorMsg = "Group not found";
					continue;
				}
				if (!VerifySpots(nNTP, group, allGroups, item.Segments.Select((NNTPSegment s) => s.MessageId).ToList(), spotVerifier))
				{
					errorMsg = "Segments verification failed";
					continue;
				}
				using (FileStream fileStream = System.IO.File.Open(text, FileMode.Create))
				{
					foreach (NNTPSegment item2 in item.Segments.OrderBy((NNTPSegment s) => s.Index))
					{
						if (nNTP.GetBody(group, item2.MessageId, out string resp, out int _, out errorMsg))
						{
							if (resp.Length < 10)
							{
								errorMsg = "No binary bytes received";
								break;
							}
							if (resp.Substring(resp.Length - 3) != ".\r\n")
							{
								errorMsg = "Binary bytes received have no dot at the end";
								break;
							}
							byte[] array = DecodeBinary(resp);
							fileStream.Write(array, 0, array.Length);
							continue;
						}
						if (!errorMsg.Equals("Removed"))
						{
							errorMsg = $"Error on getting segment: {errorMsg}";
						}
						break;
					}
				}
				if (!errorMsg.IsNullOrEmpty())
				{
					continue;
				}
				break;
			}
			if (errorMsg.IsNullOrEmpty())
			{
				list.Add(text);
			}
		}
		return list;
	}

	private static bool VerifySpots(Spotnet.Model.NNTP nntp, string group, string allGroups, List<string> messageIDs, Func<Spot, string, bool, bool> spotVerifier)
	{
		if (messageIDs == null || !messageIDs.Any())
		{
			return true;
		}
		foreach (string messageID in messageIDs)
		{
			if (!nntp.GetHeader(group, SpotHelper.MakeMsg(messageID), out var resp, out var _, out var errorMsg))
			{
				if (!errorMsg.Equals("Cancelled"))
				{
					Log.Error("Failed to get header for " + messageID + ". Error: " + errorMsg);
				}
				return false;
			}
			Spot spot = new Spot();
			string[] array = Strings.Split(resp, "\r\n");
			int num = 0;
			string[] array2 = array;
			foreach (string text in array2)
			{
				string text2 = "subject: ";
				if (text.ToLower().StartsWith(text2))
				{
					spot.Title = text.Substring(text2.Length);
					num++;
				}
				text2 = "from: ";
				if (text.ToLower().StartsWith(text2))
				{
					spot.Poster = text.Substring(text2.Length);
					num++;
				}
				text2 = "message-id: ";
				if (text.ToLower().StartsWith(text2))
				{
					spot.MessageId = text.Substring(text2.Length);
					num++;
				}
				if (num == 3)
				{
					break;
				}
			}
			if (num != 3)
			{
				Log.Error("Wrong header for " + messageID + ". Group " + group);
				return false;
			}
			if (!spotVerifier(spot, allGroups, arg3: false))
			{
				Log.Error("Failed to verify " + messageID + ". Group: " + group);
				return false;
			}
		}
		return true;
	}

	public static byte[] DecodeBinary(string str)
	{
		int num = str.IndexOf("\r\n", StringComparison.Ordinal) + 2;
		str = str.Substring(num, str.Length - num - 3);
		bool flag = false;
		if (str.StartsWith("=ybegin"))
		{
			int num2 = str.IndexOf("\r\n", StringComparison.Ordinal) + 2;
			str = str.Substring(num2, str.Length - num2);
			if (!str.StartsWith("=ypart"))
			{
				Log.Error("Bad formed yEnc received, no =ypart");
				return null;
			}
			int num3 = str.IndexOf("\r\n", StringComparison.Ordinal) + 2;
			str = str.Substring(num3, str.Length - num3);
			if (str.Substring(str.Length - 2) != "\r\n")
			{
				Log.Error("Bad formed yEnc received, no new line at the end of yEnc");
				return null;
			}
			str = str.Substring(0, str.Length - 2);
			int num4 = str.LastIndexOf("=yend", StringComparison.Ordinal);
			if (num4 < 0)
			{
				Log.Error("Bad formed yEnc received, no =yend");
				return null;
			}
			int num5 = str.Length - num4;
			str = str.Substring(0, str.Length - num5 - 2);
			flag = true;
		}
		if (str.StartsWith(".."))
		{
			str = str.Substring(1);
		}
		str = str.Replace("\r\n..", "\r\n.");
		byte[] array = SpotHelper.MakeLatin(str);
		if (flag)
		{
			using MemoryStream memoryStream = new MemoryStream();
			if (!new YEnc(memoryStream).DecodeBytes(array))
			{
				Log.Error("Failed to decode binary yEnc");
				return null;
			}
			array = memoryStream.ToArray();
		}
		return array;
	}

	internal static List<NNTPInput> Parse(Stream xXml)
	{
		List<NNTPInput> list = new List<NNTPInput>();
		try
		{
			XmlReader xmlReader = XmlReader.Create(xXml, Module.ReaderSettings);
			while (xmlReader.ReadToFollowing("file"))
			{
				NNTPInput nNTPInput = ParseSegments(xmlReader.ReadSubtree(), xmlReader.GetAttribute("subject"));
				if (nNTPInput != null && nNTPInput.Segments.Count > 0)
				{
					list.Add(nNTPInput);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return null;
		}
		if (list.Count != 0)
		{
			return list;
		}
		return null;
	}

	private static NNTPInput ParseSegments(XmlReader sR, string subject)
	{
		try
		{
			NNTPInput nNTPInput = new NNTPInput(null, subject);
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(sR);
			foreach (XmlElement item in xmlDocument.GetElementsByTagName("group"))
			{
				if (!item.InnerText.IsNullOrEmpty())
				{
					nNTPInput.Groups.Add(item.InnerText);
				}
			}
			if (nNTPInput.Groups == null || !nNTPInput.Groups.Any())
			{
				Log.Debug("Groups section is empty");
				return null;
			}
			foreach (XmlNode item2 in xmlDocument.GetElementsByTagName("segment"))
			{
				if (item2.Attributes == null)
				{
					Log.Warn("Invalid xml. No attributes.");
					return null;
				}
				int num = 0;
				int num2 = 0;
				foreach (XmlAttribute attribute in item2.Attributes)
				{
					if (!attribute.Name.EqualsIgnoreCase("bytes"))
					{
						if (attribute.Name.EqualsIgnoreCase("number"))
						{
							num2 = Convert.ToInt32(attribute.InnerText);
						}
					}
					else
					{
						num = Convert.ToInt32(attribute.InnerText);
					}
				}
				string innerText = item2.InnerText;
				if (innerText.Length < 1)
				{
					Log.Debug("messageId is wrong");
					return null;
				}
				if (num2 > 0 && num > 0)
				{
					nNTPInput.Segments.Add(new NNTPSegment(num2, num, innerText, nNTPInput));
				}
			}
			return (nNTPInput.Segments.Count > 0) ? nNTPInput : null;
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			return null;
		}
	}
}
