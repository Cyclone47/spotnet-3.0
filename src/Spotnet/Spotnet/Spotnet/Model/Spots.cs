using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Xml;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model.Newznab;
using Spotnet.Phuse;
using Spotnet.Properties;

namespace Spotnet.Model;

public class Spots
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	internal Spots()
	{
	}

	public static bool CreateComment(Engine tPhuse, string cFrom, string cDesc, string cGroup, string cOrgMessageId, string cOrgTitle, byte[] bAvatar, RSACryptoServiceProvider cRsa, string hashMessageId, ref string zErr)
	{
		cDesc = cDesc.Trim();
		cFrom = cFrom.Trim();
		if (Strings.Len(cDesc) == 0)
		{
			zErr = Words.PleaseEnterComment;
			return false;
		}
		if (Strings.Len(cDesc) < 3)
		{
			zErr = Words.CommentIsTooShort;
			return false;
		}
		if (Strings.Len(cDesc) > 900)
		{
			zErr = Words.CommentIsTooLong;
			return false;
		}
		if (Strings.Len(cFrom) < 3)
		{
			zErr = Words.SenderNameIsTooShort;
			return false;
		}
		if (Strings.Len(cFrom) > 22)
		{
			zErr = Words.SenderIsTooLong;
			return false;
		}
		if (!SpotHelper.CheckFrom(cFrom))
		{
			zErr = Words.SenderNameIsReserved;
			return false;
		}
		if (cFrom != SpotHelper.StripNonAlphaNumericCharacters(cFrom))
		{
			zErr = Words.SenderNameInvalidCharacters;
			return false;
		}
		if (bAvatar != null && Information.UBound(bAvatar) > 4000)
		{
			zErr = Words.AvatarIsTooLarge;
			return false;
		}
		if (Strings.Len(hashMessageId) < 7)
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (!SpotHelper.CheckHash(hashMessageId))
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (!hashMessageId.StartsWith("<"))
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (cRsa == null)
		{
			zErr = Words.RSAKeyIsWrong;
			return false;
		}
		if (cRsa.PublicOnly)
		{
			zErr = Words.RSAKeyIsWrong;
			return false;
		}
		cOrgTitle = cOrgTitle.Replace("\r\n", "");
		cOrgMessageId = SpotHelper.MakeMsg(cOrgMessageId, tag: false);
		string text = Strings.Left(cDesc, 999).Trim();
		string text2 = Strings.Left(SpotHelper.StripNonAlphaNumericCharacters(cFrom), 22).Trim().Replace(" ", "");
		while (text.Contains("\r\n\r\n\r\n"))
		{
			text = text.Replace("\r\n\r\n\r\n", "\r\n\r\n");
		}
		while (text.StartsWith("\r\n"))
		{
			text = text.Substring(2);
		}
		while (text.EndsWith("\r\n"))
		{
			text = text.Substring(0, text.Length - 2);
		}
		text += "\r\n";
		if (Settings.Default.PromoteSpotnetInComment && new Random().Next(100) == 1)
		{
			text += Configuration.PromoteSpotnetText;
		}
		string subject = "Re: " + cOrgTitle;
		List<string> data = SpotHelper.SplitLines(text, allowBlankLines: true, 911);
		string text3 = SpotHelper.CreateUserSignature(SpotHelper.MakeMsg(hashMessageId), cRsa);
		string text4 = "References: <" + cOrgMessageId + ">\r\nX-User-Signature: " + text3 + "\r\nX-User-Key: " + cRsa.ToXmlString(includePrivateParameters: false).Replace("\t", "").Replace("\r\n", "") + "\r\n";
		if (bAvatar != null)
		{
			text4 += SpotHelper.SplitLinesXml(Convert.ToBase64String(bAvatar).Replace("=", ""), "X-User-Avatar:", 911);
		}
		string xOutId = "";
		string text5 = SpotHelper.SpecialString(Convert.ToBase64String(cRsa.ExportParameters(includePrivateParameters: false).Modulus));
		if (!SpotHelper.PostData(tPhuse, data, subject, text2 + " <" + text5 + "." + text3 + "@spot.net>", cGroup, text4, ref xOutId, hashMessageId, ref zErr))
		{
			zErr = Words.CommentCannotPost + ". " + Words.ContactYourProvider + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		zErr = Words.MsgIDInvalid + ": " + xOutId;
		return xOutId.Length > 0;
	}

	public static SolidColorBrush CategoryToColor(int cat)
	{
		return cat switch
		{
			1 => ThemeBrushes.Frozen("#21409A"),
			2 => ThemeBrushes.Frozen("#FFFFAA"),
			3 => ThemeBrushes.Frozen("#FF4D25"),
			4 => ThemeBrushes.Frozen("#FF7BAC"),
			5 => ThemeBrushes.Frozen("#7AC943"),
			6 => ThemeBrushes.Frozen("#3FA9F5"),
			9 => ThemeBrushes.Frozen("#BDCCD4"),
			_ => Brushes.OrangeRed, 
		};
	}

	public static bool CreateSpot(Engine tPhuse, string newsgroup, string sTitle, string sDesc, byte bCat, string sSubCats, string sUrl, string sLanguage, long sizeX, long sizeY, string sNzb, string encryptedNzb, string sFrom, string sTag, string sNzbGroup, RSACryptoServiceProvider cRsa, string sHashMsgId, byte[] bImage, byte[] bAvatar, bool signLocal, ref NntpSettings settings, ref string zErr, ref string postString, bool isFakeCreation = false)
	{
		int lPos = 0;
		string text = "";
		sUrl = sUrl.Trim();
		sNzb = sNzb.Trim();
		encryptedNzb = encryptedNzb.Trim();
		sDesc = sDesc.Trim();
		sNzbGroup = sNzbGroup.Trim();
		sTag = SpotHelper.StripNonAlphaNumericCharacters(sTag.Trim());
		sSubCats = SpotHelper.StripNonAlphaNumericCharacters(sSubCats.Trim().ToLower());
		sTitle = ((sTitle != null) ? sTitle.Replace("!!", "").Replace("**", "").Replace("_", " ")
			.Trim() : "");
		if (bCat < 1)
		{
			zErr = Words.PleaseEnterCategory;
			return false;
		}
		if (sTitle.Length < 2)
		{
			zErr = Words.PleaseEnterSubject;
			return false;
		}
		if (sDesc == null || sDesc.Length < 30)
		{
			zErr = ((sDesc.Length == 0) ? Words.PleaseEnterDescription : Words.DescriptionIsTooShort);
			return false;
		}
		if (sSubCats == null || sSubCats.Length < 3)
		{
			zErr = Words.PleaseSelectSubCat;
			return false;
		}
		if (sSubCats.Length % 3 != 0)
		{
			zErr = Words.SubcatsInvalid;
			return false;
		}
		if (!sSubCats.StartsWith("a"))
		{
			zErr = Words.SubcatsInvalid;
			return false;
		}
		if (sFrom == null || sFrom.Length < 3)
		{
			zErr = Words.SenderNameIsTooShort;
			return false;
		}
		if (!SpotHelper.CheckFrom(sFrom))
		{
			zErr = Words.SenderInvalid;
			return false;
		}
		if (sFrom.Length > 22)
		{
			zErr = Words.SenderIsTooLong;
			return false;
		}
		if (sFrom != SpotHelper.StripNonAlphaNumericCharacters(Strings.Trim(sFrom)))
		{
			zErr = Words.SenderNameInvalidCharacters;
			return false;
		}
		if (sTitle.ToLower().Contains("www."))
		{
			zErr = Words.SubjectShouldNotContainURL;
			return false;
		}
		if (sTitle.ToLower().Contains("http:/"))
		{
			zErr = Words.SubjectShouldNotContainURL;
			return false;
		}
		if (sTitle.ToLower().Contains(sFrom.ToLower().Trim()))
		{
			zErr = Words.SubjectShouldNotContainYouName;
			return false;
		}
		if (Strings.Len(sTitle) > 6 && sTitle.ToUpper() == sTitle)
		{
			zErr = Words.SubjectShouldNotBeAllUpperCase;
			return false;
		}
		if (sNzb.IsNullOrEmpty())
		{
			zErr = Words.PleaseAddNZBFile;
			return false;
		}
		if (sNzbGroup.IsNullOrEmpty())
		{
			zErr = Words.NZBGroupPleaseSpecify;
			return false;
		}
		if (Strings.Len(sHashMsgId) < 7)
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (!SpotHelper.CheckHash(sHashMsgId))
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (!sHashMsgId.StartsWith("<"))
		{
			zErr = Words.MessageIDIsWrong;
			return false;
		}
		if (bImage == null)
		{
			zErr = Words.PictureNotAdded;
			return false;
		}
		if (Information.UBound(bImage) < 10)
		{
			zErr = Words.PictureNotAdded;
			return false;
		}
		if (Information.UBound(bImage) > 1048576)
		{
			zErr = Words.PictureSizeMoreThan1MB;
			return false;
		}
		if (bAvatar != null && Information.UBound(bAvatar) > 4000)
		{
			zErr = Words.AvatarIsTooLarge;
			return false;
		}
		if (cRsa == null)
		{
			zErr = Words.RSAKeyIsWrong;
			return false;
		}
		if (cRsa.PublicOnly)
		{
			zErr = Words.RSAKeyIsWrong;
			return false;
		}
		string text2;
		string errInfo;
		if (sNzb.ToLower().Contains("<nzb") & sNzb.ToLower().Contains("<?xml"))
		{
			text2 = sNzb;
		}
		else
		{
			if (!File.Exists(sNzb))
			{
				zErr = Words.NZBCannotBeFoundCheckFile;
				return false;
			}
			errInfo = "";
			text2 = SpotHelper.GetFileContents(sNzb, ref errInfo);
		}
		long sSize = 0L;
		if (!SpotHelper.IsNzb(text2, ref sSize))
		{
			zErr = Words.NZBFileInvalid;
			return false;
		}
		if (!encryptedNzb.IsNullOrEmpty())
		{
			if (!File.Exists(encryptedNzb))
			{
				zErr = Words.EncryptedNZBCannotBeFoundCheckFile;
				return false;
			}
			errInfo = "";
			text = SpotHelper.GetFileContents(encryptedNzb, ref errInfo);
			long sSize2 = 0L;
			if (!SpotHelper.IsNzb(text, ref sSize2))
			{
				zErr = Words.EncryptedNZBFileInvalid;
				return false;
			}
			text = NzrDecoder.Encode(text, 1);
			if (text == null)
			{
				zErr = "Failed to encode backup NZB";
				return false;
			}
		}
		string text3 = Strings.Left(sDesc, 9000).Trim();
		text3 = text3.Replace("\r\n", "[br]").Replace("\n", "[br]").Replace("\t", "[tab]");
		string text4 = Strings.Left(SpotHelper.StripNonAlphaNumericCharacters(sFrom), 50).Trim().Replace(" ", "");
		string text5 = Strings.Left(SpotHelper.StripNonAlphaNumericCharacters(sTag), 100).Trim().Replace("|", "")
			.Replace(";", "")
			.Replace(" ", "");
		string sLink = SpotHelper.AddHttp(Strings.Left(sUrl, 450).Trim().Replace("\t", "")
			.Replace("\r", "")
			.Replace("\n", ""));
		string text6 = Strings.Left(sTitle, 450).Trim().Replace("\t", "")
			.Replace("\r", "")
			.Replace("\n", "")
			.Replace("|", "");
		while (text6.Contains("  "))
		{
			text6 = text6.Replace("  ", " ");
		}
		string xOutId = sHashMsgId;
		string text7 = SpotHelper.SpecialString(Convert.ToBase64String(cRsa.ExportParameters(includePrivateParameters: false).Modulus));
		text6 = text6.Substring(0, 1).ToUpper() + text6.Substring(1);
		string zFrom = text4 + " <" + text7 + "@spot.net>";
		if (!isFakeCreation && !SpotHelper.PostData(tPhuse, SpotHelper.SplitLinesGzip(SpotHelper.ZipStr(SpotHelper.MakeLatin(text2))), Guid.NewGuid().ToString().Replace("-", ""), zFrom, sNzbGroup, "", ref xOutId, "", ref zErr))
		{
			zErr = Words.NZBCannotPost + ". " + Words.ContactYourProvider + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		string xOutId2 = "";
		if (!isFakeCreation && !text.IsNullOrEmpty() && !SpotHelper.PostData(tPhuse, SpotHelper.SplitLinesGzip(SpotHelper.ZipStr(SpotHelper.MakeLatin(text))), Guid.NewGuid().ToString().Replace("-", ""), zFrom, sNzbGroup, "", ref xOutId2, "", ref zErr))
		{
			zErr = Words.EncryptedNZBCannotPost + ". " + Words.ContactYourProvider + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		string[] xOut = new string[1];
		zErr = "";
		string xOutId3 = null;
		List<string> list = SpotHelper.SplitLinesGzip(SpotHelper.GetLatin(bImage));
		if (list == null || list.Count > 1)
		{
			if (list == null)
			{
				Log.Debug("zInput is null");
			}
			zErr = Words.PictureIsTooBig;
			return false;
		}
		if (!isFakeCreation && !SpotHelper.PostData(tPhuse, list, Guid.NewGuid().ToString().Replace("-", ""), text4 + " <" + text7 + "@spot.net>", sNzbGroup, "", ref xOutId3, "", ref zErr))
		{
			zErr = Words.PictureCannotPost + ". " + Words.ContactYourProvider + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		zErr = "";
		if (!SpotHelper.CreateSpotLocal(text4, text6, xOutId, sSize, xOutId2, sLink, xOutId3, text3, bCat, sSubCats, text5, ref xOut, cRsa, sizeX, sizeY, ref zErr))
		{
			zErr = Words.SpotCannotAdd + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		string sIn = xOut[1];
		string text8 = SpotHelper.SpecialString(xOut[2]);
		string text9 = SpotHelper.SpecialString(xOut[0]);
		zErr = null;
		string text10 = SpotHelper.SplitLinesXml(sIn, "X-XML:", 911) + "X-XML-Signature: " + text8 + "\r\n";
		string text11 = SpotHelper.CreateUserSignature(SpotHelper.MakeMsg(sHashMsgId), cRsa);
		text10 = text10 + "X-User-Key: " + cRsa.ToXmlString(includePrivateParameters: false).Replace("\t", "").Replace("\r\n", "") + "\r\nX-User-Signature: " + text11 + "\r\n";
		if (bAvatar != null)
		{
			text10 += SpotHelper.SplitLinesXml(Convert.ToBase64String(bAvatar).Replace("=", ""), "X-User-Avatar:", 911);
		}
		string text12 = $"{text4} <{text7}.{text11}@{text9}>";
		string text13 = text6 + ((text5.Length > 0) ? (" | " + text5) : null);
		if (!SpotHelper.IsAscii(text10, ref lPos))
		{
			zErr = Words.SpotXMLIsNotASCII + ": " + Strings.Mid(text10, lPos, 10) + "..";
			return false;
		}
		if (new Worker().ParseSpot(text13, text12, sHashMsgId, settings) == null)
		{
			zErr = Words.SpotCannotBeParsed + ": " + zErr;
			return false;
		}
		errInfo = "";
		if (!isFakeCreation && !SpotHelper.PostData(tPhuse, SpotHelper.SplitLines(sDesc, allowBlankLines: true, 911), text13, text12, newsgroup, text10, ref errInfo, sHashMsgId, ref zErr))
		{
			zErr = Words.SpotCannotPost + ". " + Words.ContactYourProvider + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		if (isFakeCreation)
		{
			string text14 = string.Concat("From: ", text12, "\r\nSubject: ", text13, "\r\nNewsgroups: ", newsgroup, "\r\nMessage-ID: ", sHashMsgId, "\r\nX-Newsreader: Spotnet ", AppHelper.AppVersion, "\r\n", text10, "Content-Type: text/plain; charset=ISO-8859-1\r\nContent-Transfer-Encoding: 8bit");
			if (!sDesc.EndsWith("\r\n"))
			{
				sDesc += "\r\n";
			}
			postString = postString + text14 + "\r\n\r\n" + sDesc + ".";
		}
		return true;
	}

	internal static int GetMaxConnectionsNumber(ServerInfo server)
	{
		int nConnections = 0;
		List<Engine> engineList = new List<Engine>();
		ServerInfo testServer = (ServerInfo)server.Clone();
		testServer.Connections = 1;
		List<Task> list = new List<Task>();
		bool connectionFailed = false;
		for (int i = 0; i < 20; i++)
		{
			list.Add(Task.Run(delegate
			{
				Engine engine = AppHelper.CreatePhuse(testServer);
				engineList.Add(engine);
				if (!TestConnection(engine, Settings.Default.HeaderGroup, out var _))
				{
					connectionFailed = true;
				}
				else
				{
					nConnections++;
				}
			}));
			while (list.Count((Task t) => !t.IsCompleted) >= 4)
			{
				Thread.Sleep(500);
			}
			if (connectionFailed)
			{
				break;
			}
		}
		Task.WaitAll(list.ToArray());
		foreach (Engine item in engineList)
		{
			item.Dispose();
		}
		Log.Debug("Test performed. Max number of connections allowed: " + nConnections);
		return nConnections;
	}

	public static bool CreatReport(Engine tPhuse, string cFrom, string cDesc, string cGroup, string cOrgMessageId, string cOrgTitle, ref string zErr)
	{
		cDesc = cDesc.Trim();
		if (Strings.Len(cDesc) == 0)
		{
			zErr = Words.PleaseEnterDescription;
			return false;
		}
		if (Strings.Len(cDesc) < 3)
		{
			zErr = Words.DescriptionIsTooShort;
			return false;
		}
		if (Strings.Len(cDesc) > 900)
		{
			zErr = Words.DescriptionIsTooLong;
			return false;
		}
		cOrgMessageId = SpotHelper.MakeMsg(cOrgMessageId);
		cOrgTitle = cOrgTitle.Replace("\r\n", "");
		string sIn = FormatSpamReportDescription(cDesc);
		string text = $"REPORT {cOrgMessageId} - {cOrgTitle}";
		List<string> data = SpotHelper.SplitLines(sIn, allowBlankLines: true, 911);
		string text2 = "References: " + cOrgMessageId + "\r\n";
		text2 += "X-No-Archive: yes\r\n";
		RSACryptoServiceProvider key = UserKeyHelper.GetKey();
		string arg = SpotHelper.CreateUserSignature(SpotHelper.MakeMsg(AppHelper.CreateMsgId(cOrgMessageId.Split('@')[0].Replace(".", "").Replace("<", ""))), key);
		string arg2 = key.ToXmlString(includePrivateParameters: false).Replace("\t", "").Replace("\r\n", "");
		text2 += $"X-User-Signature: {arg}\r\nX-User-Key: {arg2}\r\n";
		string xOutId = "";
		string msgId = SpotHelper.CreateMsgId();
		string zFrom = GenerateSpamReportSenderField(cFrom, text, msgId, ref zErr);
		if (!zErr.IsNullOrEmpty())
		{
			return false;
		}
		return SpotHelper.PostData(tPhuse, data, text, zFrom, cGroup, text2, ref xOutId, msgId, ref zErr);
	}

	private static string FormatSpamReportDescription(string cDesc)
	{
		string text = Strings.Left(cDesc, 999).Trim();
		while (text.Contains("\r\n\r\n\r\n"))
		{
			text = text.Replace("\r\n\r\n\r\n", "\r\n\r\n");
		}
		while (text.StartsWith("\r\n"))
		{
			text = text.Substring(2);
		}
		while (text.EndsWith("\r\n"))
		{
			text = text.Substring(0, text.Length - 2);
		}
		return text + "\r\n";
	}

	private static string GenerateSpamReportSenderField(string cFrom, string zSub, string msgId, ref string zErr)
	{
		cFrom = cFrom.Trim();
		if (cFrom.Length < 3)
		{
			cFrom = Words.Sender;
		}
		if (cFrom.Length > 22)
		{
			zErr = Words.SenderIsTooLong;
			return null;
		}
		if (!SpotHelper.CheckFrom(cFrom))
		{
			zErr = Words.SenderInvalid;
			return null;
		}
		string text = SpotHelper.StripNonAlphaNumericCharacters(cFrom).Trim().Replace(" ", "");
		string arg = GenerateSecureStringForSender(text, zSub, msgId);
		return $"{text} <{arg}@spot.net>";
	}

	private static string GenerateSecureStringForSender(string from, string title, string msgId)
	{
		string text = AppHelper.MakeMd5(from + title + msgId).Substring(0, 10);
		RSACryptoServiceProvider key = UserKeyHelper.GetKey();
		string arg = SpotHelper.SpecialString(UserKeyHelper.GetModulus());
		string arg2 = SpotHelper.CreateUserSignature(text, key);
		return $"{text}.{arg}.{arg2}";
	}

	public static bool DeleteSpot(Engine tPhuse, string cFrom, string cTitle, string comment, string newsgroup, string spotMsgId, out string zErr)
	{
		string text = "DISPOSE " + SpotHelper.MakeMsg(spotMsgId, tag: false) + " - " + cTitle;
		zErr = null;
		string xOutId = null;
		string msgId = SpotHelper.CreateMsgId(SpotHelper.MakeMsg(spotMsgId, tag: false).Split('@')[0]);
		string zFrom = GenerateDisposeRequestSenderField(cFrom, text, msgId);
		if (!SpotHelper.PostData(tPhuse, new string[1] { "From the author of the spot: " + comment }.ToList(), text, zFrom, newsgroup, "X-No-Archive: Yes\r\n", ref xOutId, msgId, ref zErr))
		{
			zErr = Words.ModeratorCommandCannotPost + "\r\n\r\nDetails: " + zErr;
			return false;
		}
		int num;
		if (!xOutId.IsNullOrEmpty())
		{
			num = ((!xOutId.Contains(" ")) ? 1 : 0);
			if (num != 0)
			{
				goto IL_00b2;
			}
		}
		else
		{
			num = 0;
		}
		zErr = Words.MsgIDInvalid;
		goto IL_00b2;
		IL_00b2:
		return (byte)num != 0;
	}

	private static string GenerateDisposeRequestSenderField(string from, string title, string msgId)
	{
		string modulus = UserKeyHelper.GetModulus();
		RSACryptoServiceProvider key = UserKeyHelper.GetKey();
		string text = Strings.Left(SpotHelper.StripNonAlphaNumericCharacters(from), 50).Trim().Replace(" ", "");
		int num = (int)Math.Round((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds);
		string text2 = $"12a01.999.10.{Conversions.ToString(num)}.1.NL";
		string text3 = SpotHelper.CreateUserSignature(title + text2 + text, key);
		string text4 = SpotHelper.CreateUserSignature(SpotHelper.MakeMsg(msgId), key);
		return $"{text} <{modulus}.{text4}@{text2}.{text3}>";
	}

	public static string FindNzb(string sName, int waitTime, string sNewsGroup, int maxDays, bool allowIncomplete, bool extraChecks)
	{
		string text = "";
		bool stopSearch = false;
		bool noResults = false;
		text = SpotHelper.MakeNzbs(sName, sNewsGroup, secondServer: false, looser: false, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
		if (stopSearch)
		{
			return null;
		}
		if (Strings.Len(text) == 0)
		{
			SpotHelper.Wait(waitTime);
			text = SpotHelper.MakeNzbs(sName, sNewsGroup, secondServer: true, looser: false, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
			if (stopSearch)
			{
				return null;
			}
			if (Strings.Len(text) != 0)
			{
				return text;
			}
			SpotHelper.Wait(waitTime);
			text = SpotHelper.MakeNzbs(sName, sNewsGroup, secondServer: false, looser: true, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
			if (stopSearch)
			{
				return null;
			}
			if (Strings.Len(text) != 0)
			{
				return text;
			}
			SpotHelper.Wait(waitTime);
			text = SpotHelper.MakeNzbs(sName, sNewsGroup, secondServer: true, looser: true, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
			if (stopSearch)
			{
				return null;
			}
			if (Strings.Len(text) != 0)
			{
				return text;
			}
			if (Strings.Len(sNewsGroup) > 0)
			{
				SpotHelper.Wait(waitTime);
				text = SpotHelper.MakeNzbs(sName, "", secondServer: false, looser: false, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
				if (stopSearch)
				{
					return null;
				}
				if (Strings.Len(text) == 0)
				{
					SpotHelper.Wait(waitTime);
					text = SpotHelper.MakeNzbs(sName, "", secondServer: true, looser: false, onlyCol: true, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
					if (stopSearch)
					{
						return null;
					}
				}
			}
			if (Strings.Len(text) != 0)
			{
				return text;
			}
			SpotHelper.Wait(waitTime);
			text = SpotHelper.MakeNzbs(sName, "", secondServer: false, looser: true, onlyCol: false, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
			if (stopSearch)
			{
				return null;
			}
			if (Strings.Len(text) == 0)
			{
				SpotHelper.Wait(waitTime);
				text = SpotHelper.MakeNzbs(sName, "", secondServer: true, looser: true, onlyCol: false, maxDays, ref stopSearch, allowIncomplete, ref noResults, exCheck: false);
				if (stopSearch)
				{
					return null;
				}
			}
		}
		return text;
	}

	public static bool GetNzb(Engine tPhuse, string newsgroup, List<string> xMsgId, out string sxOut, out string sError)
	{
		sxOut = null;
		if (!SpotHelper.GetBinary(tPhuse, newsgroup, xMsgId, out var sxOut2, out sError))
		{
			return false;
		}
		sxOut = SpotHelper.UnzipStr(ref sxOut2);
		if (sxOut != null)
		{
			return true;
		}
		sError = Words.NZBCannotUnpack;
		return false;
	}

	public static bool GetSpot(Engine phuse, string newsgroup, long articleId, string messageId, ref SpotEx spotOut, NntpSettings param, ref string errorMsg, string postString = null)
	{
		if (NewznabHelper.IsNewznabMessageId(messageId))
		{
			return NewznabHelper.GetSpot(messageId, postString, ref spotOut, ref errorMsg);
		}
		int result = -1;
		if (postString == null)
		{
			postString = "";
			bool flag = false;
			NNTP nNTP = new NNTP(phuse);
			if (articleId > 0)
			{
				flag = nNTP.GetHeader(newsgroup, Conversions.ToString(articleId), out postString, out result, out errorMsg);
				if (!flag && result != 423)
				{
					return false;
				}
			}
			if (!flag && !messageId.IsNullOrEmpty())
			{
				messageId = SpotHelper.MakeMsg(messageId);
				flag = nNTP.GetHeader(newsgroup, messageId, out postString, out result, out errorMsg);
			}
			if (!flag)
			{
				if (result == 430 || result == 423)
				{
					errorMsg = Words.SpotNotFoundProbTooOld;
					Log.Error(errorMsg);
				}
				return false;
			}
		}
		string text = "";
		string sMessageId = "";
		string newsreader = "";
		string text2 = "";
		string text3 = "";
		string xmlSignature = "";
		UserInfo userInfo = new UserInfo();
		string[] array = Strings.Split(postString, "\r\n");
		int num = array.Length - 2;
		for (int i = 1; i <= num; i++)
		{
			if (Strings.UCase(array[i]).StartsWith("SUBJECT: "))
			{
				text3 = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
				int num2 = array.Length - 2;
				for (int j = i + 1; j <= num2 && (array[j].IndexOf(":", StringComparison.Ordinal) == -1 || array[j].StartsWith(" ") || array[j].StartsWith("\t")); j++)
				{
					text3 += array[j];
				}
			}
			if (Strings.UCase(array[i]).StartsWith("FROM: "))
			{
				text2 = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
				for (int j = i + 1; j < array.Count() - 1 && (!array[j].Contains(":") || array[j].StartsWith(" ") || array[j].StartsWith("\t")); j++)
				{
					text2 += array[j];
				}
			}
			if (Strings.UCase(array[i]).StartsWith("MESSAGE-ID: "))
			{
				sMessageId = SpotHelper.MakeMsg(Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3));
			}
			if (Strings.UCase(array[i]).StartsWith("X-NEWSREADER: "))
			{
				newsreader = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
			if (Strings.UCase(array[i]).StartsWith("X-USER-AVATAR: "))
			{
				userInfo.Avatar += Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
			if (Strings.UCase(array[i]).StartsWith("X-XML: "))
			{
				text += Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
			if (Strings.UCase(array[i]).StartsWith("X-USER-KEY: "))
			{
				UserInfo userInfo2 = userInfo;
				userInfo2.Modulus = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
				if (userInfo2.Modulus.ToLower().Contains("<modulus>"))
				{
					userInfo2.Modulus = userInfo2.Modulus.Substring(userInfo2.Modulus.ToLower().IndexOf("<modulus>", StringComparison.Ordinal) + 9);
					if (userInfo2.Modulus.Contains("<"))
					{
						userInfo2.Modulus = userInfo2.Modulus.Substring(0, userInfo2.Modulus.IndexOf("<", StringComparison.Ordinal));
					}
				}
				else
				{
					userInfo2.Modulus = SpotHelper.FixPadding(SpotHelper.UnSpecialString(userInfo2.Modulus));
				}
			}
			if (Strings.UCase(array[i]).StartsWith("ORGANIZATION: "))
			{
				userInfo.Organisation = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
				userInfo.Organisation = userInfo.Organisation.Substring(0, 1).ToUpper() + userInfo.Organisation.Substring(1);
			}
			if (Strings.UCase(array[i]).StartsWith("X-TRACE: "))
			{
				userInfo.Trace = userInfo.Trace + "\r\n" + Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
			if (Strings.UCase(array[i]).StartsWith("NNTP-POSTING-HOST: "))
			{
				string text4 = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
				if (text4.IndexOf(" (", StringComparison.Ordinal) > 0)
				{
					text4 = text4.Replace(")", "");
					text4 = text4.Substring(0, text4.IndexOf(" (", StringComparison.Ordinal));
				}
				userInfo.Trace = userInfo.Trace + "\r\n" + text4;
			}
			if (Strings.UCase(array[i]).StartsWith("X-ORIGINATING-IP: "))
			{
				userInfo.Trace = userInfo.Trace + "\r\n" + Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
			if (Strings.UCase(array[i]).StartsWith("X-USER-SIGNATURE: "))
			{
				userInfo.Signature = SpotHelper.UnSpecialString(Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3));
			}
			if (Strings.UCase(array[i]).StartsWith("X-XML-SIGNATURE: "))
			{
				xmlSignature = Strings.Mid(array[i], array[i].IndexOf(":", StringComparison.Ordinal) + 3);
			}
		}
		userInfo.Trace = userInfo.Trace.Replace("\r\n" + userInfo.Organisation, "");
		if (userInfo.Trace == "\r\n")
		{
			userInfo.Trace = "";
		}
		if (Strings.Len(userInfo.Avatar) > 0)
		{
			userInfo.Avatar = SpotHelper.FixPadding(SpotHelper.UnSpecialString(userInfo.Avatar));
		}
		Worker worker = new Worker();
		SpotEx lz = worker.ParseSpot(text3, text2, sMessageId, param);
		if (lz == null)
		{
			errorMsg = Words.SpotCannotBeParsed;
			Log.Error(errorMsg);
			return false;
		}
		lz.Newsreader = newsreader;
		lz.User = userInfo;
		if (Strings.Len(lz.Modulus) > 0 && lz.User.Modulus != lz.Modulus)
		{
			errorMsg = Words.SignatureIsNotCorrect;
			Log.Error(errorMsg);
			return false;
		}
		if (lz.KeyID == 1)
		{
			text = text.Replace("<Signature><", "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><").Replace("<SignedInfo><", "<SignedInfo><CanonicalizationMethod Algorithm=\"http://www.w3.org/TR/2001/REC-xml-c14n-20010315\" /><SignatureMethod Algorithm=\"http://www.w3.org/2000/09/xmldsig#dsa-sha1\" /><").Replace("<Reference URI=\"\">", "<Reference URI=\"\"><Transforms><Transform Algorithm=\"http://www.w3.org/2000/09/xmldsig#enveloped-signature\" /></Transforms><DigestMethod Algorithm=\"http://www.w3.org/2000/09/xmldsig#sha1\" />");
		}
		XmlDocument xmlDocument = new XmlDocument();
		try
		{
			xmlDocument.XmlResolver = null;
			xmlDocument.LoadXml(SpotHelper.MakeAscii(text.Replace("&", "&amp;")));
		}
		catch (Exception ex)
		{
			errorMsg = Words.SpotCannotRetrieveXMLInvalid + ": " + ex.Message;
			Log.Error(errorMsg);
			return false;
		}
		lz = worker.ParseSpotXML(ref lz, xmlDocument, xmlSignature, param.CheckSignatures);
		if (lz == null)
		{
			errorMsg = Words.SpotCannotOpen;
			Log.Error(errorMsg);
			return false;
		}
		spotOut = lz;
		return true;
	}

	public static bool TestConnection(Engine tPhuse, string newsgroup, out string errorMsg)
	{
		NNTP nNTP = new NNTP(tPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		int result;
		return nNTP.SelectGroup(newsgroup, ref first, ref last, ref count, out result, out errorMsg, testConnection: true);
	}
}
