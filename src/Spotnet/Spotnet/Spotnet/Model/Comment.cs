using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse;
using Spotnet.Properties;

namespace Spotnet.Model;

public class Comment
{
	public long Article;

	public string Body;

	public DateTime Created;

	public string From;

	public string MessageId;

	public UserInfo User;

	private static readonly List<string> LinksToAllowAlways = new List<string> { "binsearch.info", "nzbindex.nl" };

	private static long _cachedArticleId;

	private static DateTime _cachedCreated;

	public bool HasLinks()
	{
		Match match = new Regex("http(s)?://(([\\w-]+.)+[\\w-]+)(/[\\w- ./?%&=])?", RegexOptions.IgnoreCase).Match(Body);
		if (match.Success)
		{
			string input = match.Groups[2].ToString();
			foreach (string linksToAllowAlway in LinksToAllowAlways)
			{
				if (Regex.IsMatch(input, "^(http(s)?://)?(www\\.)?" + linksToAllowAlway + "*", RegexOptions.IgnoreCase))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	public void RemoveAvastMessageFromBody()
	{
		int num = Body.Substring(0, Body.Length - 2).LastIndexOf("\r\n", StringComparison.Ordinal);
		if (num <= 0 || (!Body.Substring(num).Contains("http://www.avast.com") && !Body.Substring(num).Contains("https://www.avast.com")))
		{
			return;
		}
		int num2 = Body.Substring(0, num).LastIndexOf("\r\n", StringComparison.Ordinal);
		if (num2 != -1)
		{
			int length = Body.Substring(0, num2).LastIndexOf("\r\n", StringComparison.Ordinal);
			if (num2 != -1)
			{
				Body = Body.Substring(0, length);
			}
		}
	}

	public void RemovePromoteSpotnetMessageFromBody()
	{
		Body = Body.Replace(Configuration.PromoteSpotnetText, "");
	}

	internal bool GetCommentDateFromTheNet(Engine phuse, NntpSettings xParam, out string errorMsg)
	{
		errorMsg = "";
		if (_cachedArticleId == Article)
		{
			Created = _cachedCreated;
			return true;
		}
		if (!new NNTP(phuse).GetHeader(xParam.GroupName, Article.ToString(CultureInfo.InvariantCulture), out var resp, out var _, out errorMsg))
		{
			return false;
		}
		if (resp.Substring(resp.Length - 3) != ".\r\n")
		{
			errorMsg = "Invalid ending: " + resp.Substring(resp.Length - 3);
			return false;
		}
		string[] array = resp.Split('\n', '\r');
		foreach (string text in array)
		{
			if (text.ToUpper().StartsWith("DATE: "))
			{
				Created = SpotHelper.ConvertDate(Strings.Mid(text, text.IndexOf(":", StringComparison.InvariantCulture) + 3));
				_cachedArticleId = Article;
				_cachedCreated = Created;
				break;
			}
		}
		return true;
	}

	internal bool GetCommentFieldsFromTheNet(Engine tPhuse, NntpSettings xParam, bool includeBody, ref string sError)
	{
		if (Article <= 0)
		{
			throw new Exception("Article is not set or wrong: " + Article);
		}
		NNTP nNTP = new NNTP(tPhuse);
		string resp = null;
		if (!nNTP.GetArticle(xParam.GroupName, Article.ToString(CultureInfo.InvariantCulture), ref resp, out var result, ref sError))
		{
			if (result == 423)
			{
				return false;
			}
			throw new Exception(sError);
		}
		if (resp.Substring(resp.Length - 3) == ".\r\n")
		{
			return Parse(resp, xParam, ref sError);
		}
		sError = "Invalid ending";
		return false;
	}

	private bool Parse(string sArt, NntpSettings xParam, ref string sError)
	{
		try
		{
			string text = "";
			User = new UserInfo();
			string[] array = Strings.Split(sArt, "\r\n\r\n");
			string expression = array[0];
			string text2 = sArt.Substring(array[0].Length + 4);
			string[] array2 = Strings.Split(expression, "\r\n");
			for (int i = 1; i < array2.Length - 1; i++)
			{
				if (Strings.UCase(array2[i]).StartsWith("FROM: "))
				{
					From = Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
					for (int j = i + 1; j < array2.Length - 1 && (array2[j].IndexOf(":", StringComparison.InvariantCulture) == -1 || array2[j].StartsWith(" ") || array2[j].StartsWith("\t")); j++)
					{
						From += array2[j];
					}
				}
				if (Strings.UCase(array2[i]).StartsWith("DATE: "))
				{
					Created = SpotHelper.ConvertDate(Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3));
				}
				if (Strings.UCase(array2[i]).StartsWith("MESSAGE-ID: "))
				{
					MessageId = SpotHelper.MakeMsg(Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3));
				}
				if (Strings.UCase(array2[i]).StartsWith("X-USER-AVATAR: "))
				{
					text += Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
				}
				if (Strings.UCase(array2[i]).StartsWith("X-USER-KEY: "))
				{
					User.Modulus = Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
					if (User.Modulus.ToLower().Contains("<modulus>"))
					{
						User.Modulus = User.Modulus.Substring(User.Modulus.ToLower().IndexOf("<modulus>", StringComparison.InvariantCulture) + 9);
						if (User.Modulus.Contains("<"))
						{
							User.Modulus = User.Modulus.Substring(0, User.Modulus.IndexOf("<", StringComparison.InvariantCulture));
						}
					}
					else
					{
						User.Modulus = SpotHelper.FixPadding(SpotHelper.UnSpecialString(User.Modulus));
					}
				}
				if (Strings.UCase(array2[i]).StartsWith("X-USER-SIGNATURE: "))
				{
					User.Signature = SpotHelper.UnSpecialString(Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3));
				}
				if (Strings.UCase(array2[i]).StartsWith("ORGANIZATION: "))
				{
					User.Organisation = Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
					User.Organisation = User.Organisation.Substring(0, 1).ToUpper() + User.Organisation.Substring(1);
				}
				if (Strings.UCase(array2[i]).StartsWith("X-TRACE: "))
				{
					UserInfo user = User;
					user.Trace = user.Trace + "\r\n" + Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
				}
				if (Strings.UCase(array2[i]).StartsWith("NNTP-POSTING-HOST: "))
				{
					string text3 = Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
					if (text3.IndexOf(" (", StringComparison.InvariantCulture) > 0)
					{
						text3 = text3.Replace(")", "");
						text3 = text3.Substring(0, text3.IndexOf(" (", StringComparison.InvariantCulture));
					}
					UserInfo user2 = User;
					user2.Trace = user2.Trace + "\r\n" + text3;
				}
				if (Strings.UCase(array2[i]).StartsWith("X-ORIGINATING-IP: "))
				{
					UserInfo user3 = User;
					user3.Trace = user3.Trace + "\r\n" + Strings.Mid(array2[i], array2[i].IndexOf(":", StringComparison.InvariantCulture) + 3);
				}
			}
			if (xParam.BlackList.Contains(User.Modulus))
			{
				sError = "Blacklist";
				return false;
			}
			User.Avatar = SpotHelper.FixPadding(SpotHelper.UnSpecialString(text));
			User.Trace = User.Trace.Replace("\r\n" + User.Organisation, "").Trim();
			if (User.Trace == "\r\n")
			{
				User.Trace = "";
			}
			From = Strings.Trim(Strings.Split(From, "<")[0]);
			Body = text2.Substring(0, text2.Length - 5);
			Body = Body.Replace("\r\n..", ".");
			if (Strings.Len(From) == 0)
			{
				sError = "Sip1";
				return false;
			}
			if (Body.IsNullOrEmpty())
			{
				sError = "Sip3";
				return false;
			}
			if (Strings.Len(MessageId) == 0)
			{
				sError = "Sip2";
				return false;
			}
			User.ValidSignature = false;
			if (!xParam.CheckSignatures)
			{
				return true;
			}
			User.ValidSignature = SpotHelper.CheckUserSignature(MessageId, User.Signature, User.Modulus);
			if (!User.ValidSignature)
			{
				User.ValidSignature = SpotHelper.CheckUserSignature(MessageId + Body + "\r\n" + From, User.Signature, User.Modulus);
			}
			if (!User.ValidSignature)
			{
				sError = "Invalid signature";
				return false;
			}
		}
		catch (Exception ex)
		{
			sError = ex.Message;
			return false;
		}
		return true;
	}
}
