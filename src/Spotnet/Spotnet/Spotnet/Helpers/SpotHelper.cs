using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Xml;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Phuse;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Helpers;

internal sealed class SpotHelper
{
	internal const string CancelMsg = "Canceled";

	internal const string MsgDomain = "spot.net";

	private static readonly Logger Log;

	internal static readonly DateTime Epoch;

	/// <summary>Public exponent 65537, shared by every spot key.</summary>
	private static readonly byte[] RsaExponent = new byte[3] { 1, 0, 1 };

	private const int RsaCacheLimit = 2048;

	private static readonly Dictionary<string, RSACryptoServiceProvider> RsaCache = new Dictionary<string, RSACryptoServiceProvider>(StringComparer.Ordinal);

	static SpotHelper()
	{
		Log = LogManager.GetCurrentClassLogger();
		Epoch = new DateTime(1970, 1, 1, 0, 0, 0);
	}

	public static string AddHttp(string text)
	{
		if (HasHttp(text) || text.Length <= 0)
		{
			return text;
		}
		return "http://" + text;
	}

	internal static bool CheckFrom(string sFrom)
	{
		return !new string[15]
		{
			"god", "mod", "modje", "spot", "spotje", "spotmod", "admin", "drazix", "moderator", "superuser",
			"supervisor", "spotnet", "spotned", "spotnetmod", "administrator"
		}.Contains(sFrom.Trim().ToLower());
	}

	internal static bool CheckHash(string sMsg)
	{
		byte[] array = SHA1.HashData(MakeLatin(sMsg));
		if (array[0] == 0)
		{
			return array[1] == 0;
		}
		return false;
	}

	public static bool CheckUserSignature(string sOrg, string sSignature, string sUserKey)
	{
		try
		{
			return MakeRsa(sUserKey)?.VerifyHash(SHA1.HashData(MakeLatin(sOrg)), null, Convert.FromBase64String(FixPadding(sSignature))) ?? false;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return false;
	}

	internal static DateTime ConvertDate(string sDate)
	{
		sDate = sDate.Replace("(UTC)", "").Trim();
		if (!DateTime.TryParse(sDate, out var result))
		{
			Log.Warn("Cannot parse date: " + sDate);
		}
		return result;
	}

	public static bool IsStampOutOfEuroRetention(long stamp)
	{
		if (Sys.EuroUsenetRetention > 0)
		{
			return stamp.FromUnixTime() <= DateTime.Now - TimeSpan.FromHours(Sys.EuroUsenetRetention + 1);
		}
		return false;
	}

	private static string CreateHash(string sLeft, string sRight)
	{
		// SHA1.Create() picks the platform implementation instead of the CAPI provider,
		// which a FIPS-policy machine will not create. Same digest, same wire format.
		using SHA1 sHA1CryptoServiceProvider = SHA1.Create();
		byte[] array = MakeLatin(sLeft);
		byte[] array2 = MakeLatin(sRight);
		int num = Information.UBound(array) + 1;
		int num2 = Information.UBound(array2) + 1;
		byte[] array3 = new byte[5 + num + num2 - 1];
		int num3 = Information.UBound(array);
		for (int i = 0; i <= num3; i++)
		{
			array3[i] = array[i];
		}
		int num4 = Information.UBound(array3);
		for (int j = Information.UBound(array3) - (num2 - 1); j <= num4; j++)
		{
			array3[j] = array2[j - (Information.UBound(array3) - (num2 - 1))];
		}
		int num5 = Information.UBound(array3) - 3 - num2;
		int num6 = Information.UBound(array3) - 2 - num2;
		int num7 = Information.UBound(array3) - 1 - num2;
		int num8 = Information.UBound(array3) - num2;
		Random random = new Random();
		byte[] array4 = new byte[62];
		byte[] array5 = new byte[62];
		byte[] array6 = new byte[62];
		byte[] array7 = new byte[62];
		for (int k = 0; k < 62; k++)
		{
			array4[k] = (byte)k;
			array5[k] = (byte)k;
			array6[k] = (byte)k;
			array7[k] = (byte)k;
		}
		for (int l = 0; l < 62; l++)
		{
			int num9 = random.Next(0, 62);
			byte b = array4[l];
			array4[l] = array4[num9];
			array4[num9] = b;
			num9 = random.Next(0, 62);
			b = array5[l];
			array5[l] = array5[num9];
			array5[num9] = b;
			num9 = random.Next(0, 62);
			b = array6[l];
			array6[l] = array6[num9];
			array6[num9] = b;
			num9 = random.Next(0, 62);
			b = array7[l];
			array7[l] = array7[num9];
			array7[num9] = b;
		}
		for (int m = 0; m < 62; m++)
		{
			array4[m] = GetBaseChar(array4[m]);
			array5[m] = GetBaseChar(array5[m]);
			array6[m] = GetBaseChar(array6[m]);
			array7[m] = GetBaseChar(array7[m]);
		}
		for (int n = 0; n < 62; n++)
		{
			for (int num10 = 0; num10 <= 61; num10++)
			{
				for (int num11 = 0; num11 <= 61; num11++)
				{
					for (int num12 = 0; num12 <= 61; num12++)
					{
						array3[num5] = array4[n];
						array3[num6] = array5[num10];
						array3[num7] = array6[num11];
						array3[num8] = array7[num12];
						byte[] array8 = sHA1CryptoServiceProvider.ComputeHash(array3);
						if (array8[0] == 0 && array8[1] == 0)
						{
							return GetLatin(array3);
						}
					}
				}
			}
		}
		throw new Exception("Error 422");
	}

	internal static string CreateMsgId(string sPrefix = "")
	{
		byte[] array = new byte[8];
		new Random().NextBytes(array);
		int value = (int)Math.Round((DateTime.UtcNow - Epoch).TotalSeconds);
		string text = (Convert.ToBase64String(array) + Convert.ToBase64String(BitConverter.GetBytes(value))).Replace("/", "s").Replace("+", "p").Replace("=", "");
		if (sPrefix.Length != 0)
		{
			return CreateHash("<" + sPrefix.Replace(".", "") + ".0." + text + ".", "@spot.net>");
		}
		return CreateHash("<" + text, "@spot.net>");
	}

	internal static bool CreateSpotLocal(string sPoster, string sTitle, string nzbMsgId, long nzbFileSize, string encNzbMsgId, string sLink, string sImgId, string sDesc, byte HCat, string sCat, string sTag, ref string[] xOut, RSACryptoServiceProvider cRsa, long lImgX, long lImgY, ref string zErr)
	{
		bool result;
		try
		{
			int num = (int)Math.Round((DateTime.UtcNow - Epoch).TotalSeconds);
			string text = Conversions.ToString(HCat) + Conversions.ToString((byte)7) + sCat.ToLower().Replace(".", "") + "." + Conversions.ToString(Math.Abs(nzbFileSize)) + ".10." + Conversions.ToString(num) + ".1.NL";
			string text2 = CreateUserSignature(sTitle + text + sPoster, cRsa);
			text = text + "." + text2;
			string text3 = "<Spotnet><Posting>";
			text3 = text3 + "<Key>" + Conversions.ToString((byte)7) + "</Key><Created>" + Conversions.ToString(num) + "</Created><Poster>" + HtmlEncode(sPoster) + "</Poster>";
			if (Strings.Len(sTag) > 0)
			{
				text3 = text3 + "<Tag>" + HtmlEncode(sTag) + "</Tag>";
			}
			text3 = text3 + "<Title>" + HtmlEncode(sTitle) + "</Title><Description>" + HtmlEncode(sDesc) + "</Description>";
			if (!sLink.IsNullOrEmpty())
			{
				text3 = text3 + "<Website>" + HtmlEncode(sLink) + "</Website>";
			}
			if (Strings.Len(sImgId) > 0)
			{
				text3 = Enumerable.Aggregate(seed: (!(lImgX > 0 && lImgY > 0)) ? (text3 + "<Image>") : (text3 + "<Image Width='" + Conversions.ToString(lImgX) + "' Height='" + Conversions.ToString(lImgY) + "'>"), source: from str4 in sImgId.Split(' ')
					where str4.Length > 0
					select str4, func: (string current, string str4) => current + "<Segment>" + MakeMsg(str4, tag: false) + "</Segment>");
				text3 += "</Image>";
			}
			text3 = text3 + "<Size>" + Conversions.ToString(nzbFileSize) + "</Size><Category>0" + Conversions.ToString(HCat);
			text3 = (from str5 in SplitBySizEx(sCat, 3)
				where str5.Length > 0
				select str5).Aggregate(text3, (string current, string str5) => current + "<Sub>0" + Conversions.ToString(HCat) + HtmlEncode(str5) + "</Sub>");
			text3 += "</Category><NZB>";
			text3 = (from msgid in nzbMsgId.Split(' ')
				where msgid.Length > 0
				select msgid).Aggregate(text3, (string current, string id) => current + "<Segment>" + MakeMsg(id, tag: false) + "</Segment>");
			string arg = "";
			if (!encNzbMsgId.IsNullOrEmpty())
			{
				arg = (from msgid in encNzbMsgId.Split(' ')
					where msgid.Length > 0
					select msgid).Aggregate("", (string current, string id) => current + "<Segment>" + MakeMsg(id, tag: false) + "</Segment>");
				arg = NzrDecoder.Encode(arg, 1);
				arg = ((arg != null) ? $"<NZR K=\"1\">{arg}</NZR>" : "");
			}
			text3 += $"</NZB>{arg}</Posting></Spotnet>";
			xOut = new string[3]
			{
				text,
				text3,
				CreateUserSignature(text3, cRsa)
			};
			result = true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			result = false;
			zErr = "CreateSpotLocal: " + ex.Message;
		}
		return result;
	}

	internal static string CreateUserSignature(string sDataIn, RSACryptoServiceProvider cRsa)
	{
		return SpecialString(Convert.ToBase64String(cRsa.SignHash(SHA1.HashData(MakeLatin(sDataIn)), null)));
	}

	internal static List<NNTPWork> CreateWork(long sFirst, long sLast, int maxDesiredWorkLen)
	{
		List<NNTPWork> list = new List<NNTPWork>();
		long num = 0L;
		int num2 = maxDesiredWorkLen / 5;
		int num3 = num2;
		long num4 = sLast - sFirst + 1;
		if (num4 <= 0 || sFirst < 0 || sLast < 0)
		{
			return list;
		}
		if (num4 > num3)
		{
			num = sFirst + (num4 - num3);
		}
		else
		{
			num3 = 0;
		}
		long num5 = (long)Math.Round(Math.Ceiling((double)(num4 - num3) / 100.0));
		if (num5 > maxDesiredWorkLen)
		{
			num5 = maxDesiredWorkLen;
		}
		if (num5 < num2)
		{
			num5 = num2;
		}
		if (num5 > num4 - num3)
		{
			num5 = num4 - num3;
		}
		long num6 = sFirst;
		long num7 = num5;
		long num8 = (long)Math.Round(Math.Ceiling((double)(num4 - num3) / (double)num5)) + 1;
		for (long num9 = 1L; num9 <= num8; num9++)
		{
			if (num6 > sLast)
			{
				continue;
			}
			if (num6 + num7 > sLast)
			{
				num7 = sLast - num6 + 1;
			}
			if ((long)num3 > 0L)
			{
				if (num6 >= num)
				{
					continue;
				}
				if (num6 + num7 >= num)
				{
					num7 = num - num6;
				}
			}
			if (num7 > 0)
			{
				NNTPWork item = new NNTPWork
				{
					xDone = false,
					xStart = num6,
					xEnd = num6 + (num7 - 1)
				};
				list.Add(item);
				num6 += num7;
				num7 = num5;
			}
		}
		if ((long)num3 > 0L)
		{
			NNTPWork item2 = new NNTPWork
			{
				xDone = false,
				xStart = num,
				xEnd = num + ((long)num3 - 1L)
			};
			list.Add(item2);
		}
		list.Reverse();
		long num10 = list.Sum((NNTPWork work2) => work2.xEnd - work2.xStart + 1);
		if (num4 != num10)
		{
			throw new Exception("TotalMSG (" + num4 + ") != cTotal (" + num10 + ")");
		}
		return list;
	}

	internal static string FixPadding(string sIn)
	{
		return (sIn.Length % 4) switch
		{
			0 => sIn, 
			1 => sIn + "===", 
			2 => sIn + "==", 
			3 => sIn + "=", 
			_ => null, 
		};
	}

	internal static string FormatLong(long zLong)
	{
		string text = FormatLong2(zLong);
		if (text == "0")
		{
			return Words.NoneWord;
		}
		return text;
	}

	internal static string FormatLong2(long zLong)
	{
		if (zLong == 0L)
		{
			return "0";
		}
		return zLong.ToString("#,#", CultureInfo.InvariantCulture).Replace(",", ".");
	}

	private static byte GetBaseChar(byte lIndex)
	{
		byte b = lIndex;
		if (b <= 25)
		{
			return (byte)(65 + lIndex);
		}
		if (b >= 26 && b <= 51)
		{
			return (byte)(97 + (lIndex - 26));
		}
		if (b >= 52 && b <= 62)
		{
			return (byte)(48 + (lIndex - 52));
		}
		return 65;
	}

	internal static bool GetBinary(Engine tPhuse, string newsgroup, List<string> xMsgId, out byte[] sxOut, out string sError, bool decodeGzip = true)
	{
		sxOut = null;
		sError = "";
		if (xMsgId == null)
		{
			return false;
		}
		if (xMsgId.Count < 1)
		{
			return false;
		}
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(tPhuse);
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in xMsgId)
		{
			if (!nNTP.GetBody(newsgroup, item, out string resp, out int resCode, out sError))
			{
				if (resCode == 430)
				{
					sError = Words.CannotFindNZBTryAgainLater;
				}
				return false;
			}
			if (resp.Substring(resp.Length - 3) != ".\r\n")
			{
				sError = Words.ProductCannotDownload + ": Code 4";
				return false;
			}
			int num = resp.IndexOf("\r\n", StringComparison.Ordinal) + 2;
			resp = ((!decodeGzip) ? resp.Substring(num, resp.Length - num - 3) : resp.Substring(num, resp.Length - num - 5).Replace("\r\n..", ".").Replace("\r\n", null));
			stringBuilder.Append(resp);
		}
		if (decodeGzip)
		{
			sxOut = MakeLatin(stringBuilder.ToString().Replace("=C", "\n").Replace("=B", "\r")
				.Replace("=A", "\0")
				.Replace("=D", "="));
		}
		else
		{
			sxOut = MakeLatin(stringBuilder.ToString());
		}
		return true;
	}

	internal static bool GetFullImageBinary(List<string> xMsgId, out byte[] imageBytes, out string sError)
	{
		Engine downloadPhuse = AppHelper.DownloadPhuse;
		string nZBGroup = Settings.Default.NZBGroup;
		return GetBinary(downloadPhuse, nZBGroup, xMsgId, out imageBytes, out sError);
	}

	internal static bool GetThumbImageBinary(List<string> xMsgId, out byte[] imageBytes, out string sError)
	{
		Engine headerPhuse = AppHelper.HeaderPhuse;
		string thumbsGroup = Settings.Default.ThumbsGroup;
		return GetBinary(headerPhuse, thumbsGroup, xMsgId, out imageBytes, out sError);
	}

	private static string GetBinsearch()
	{
		return "binsearch.net";
	}

	internal static int TryToExtractCodeFromResponse(string sResponse)
	{
		int result = 0;
		try
		{
			string input = Strings.Left(sResponse, 200);
			Match match = new Regex("^(\\d+) ").Match(input);
			if (match.Success)
			{
				return Convert.ToInt32(match.Groups[1].Value);
			}
			match = new Regex("\\((\\d+)\\)").Match(input);
			if (match.Success)
			{
				return Convert.ToInt32(match.Groups[1].Value);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return result;
	}

	internal static int TryToExtractCodeFromResponse(Stream stream, int length = 200)
	{
		int result = 0;
		try
		{
			string @string = Module.GetString(stream, 0L, length);
			Match match = new Regex("^(\\d+) ").Match(@string);
			if (match.Success)
			{
				return Convert.ToInt32(match.Groups[1].Value);
			}
			match = new Regex("\\((\\d+)\\)").Match(@string);
			if (match.Success)
			{
				return Convert.ToInt32(match.Groups[1].Value);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return result;
	}

	public static string GetFileContents(string fullPath, ref string errInfo)
	{
		try
		{
			StreamReader streamReader = new StreamReader(fullPath, LatinEnc());
			string result = streamReader.ReadToEnd();
			streamReader.Close();
			return result;
		}
		catch (Exception ex)
		{
			errInfo = ex.Message;
			return null;
		}
	}

	internal static string GetLatin(byte[] zText)
	{
		return LatinEnc().GetString(zText);
	}

	internal static string GetLocation(string url)
	{
		try
		{
			WebRequest webRequest = WebRequest.Create(url);
			webRequest.Proxy = null;
			return webRequest.GetResponse().ResponseUri.Host;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string GetNzb(string zPostData, string orgUrl, ref string zError)
	{
		byte[] array = MakeLatin(zPostData);
		try
		{
			HttpWebRequest obj = (HttpWebRequest)WebRequest.Create("http://" + GetBinsearch() + "/fcgi/nzb.fcgi?" + orgUrl);
			obj.Proxy = null;
			obj.ServicePoint.Expect100Continue = false;
			obj.Method = "POST";
			obj.ContentType = "application/x-www-form-urlencoded";
			obj.ContentLength = array.Length;
			Stream requestStream = obj.GetRequestStream();
			requestStream.Write(array, 0, array.Length);
			requestStream.Close();
			Stream responseStream = obj.GetResponse().GetResponseStream();
			if (responseStream == null)
			{
				zError = "GetNZB from binsearch response is empty";
				AppHelper.Error(zError);
				return null;
			}
			string text = UnGzip(MakeLatin(new StreamReader(responseStream, LatinEnc()).ReadToEnd()));
			long sSize = 0L;
			if (IsNzb(text, ref sSize))
			{
				return text;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			zError = ex.Message;
		}
		return null;
	}

	internal static RSACryptoServiceProvider[] GetRsa(string[] trustedKeys)
	{
		int num = Information.UBound(trustedKeys);
		RSACryptoServiceProvider[] array = new RSACryptoServiceProvider[num + 1];
		for (int i = 0; i <= num; i++)
		{
			array[i] = null;
			if (Strings.Len(trustedKeys[i]) > 0)
			{
				try
				{
					array[i] = MakeRsa(trustedKeys[i]);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}
		}
		return array;
	}

	public static bool HasHttp(string text)
	{
		if (text.IndexOf(":", StringComparison.Ordinal) > 1)
		{
			string text2 = Strings.Split(text, ":")[0].ToLower();
			if (text2 == "http" || text2 == "https")
			{
				return true;
			}
		}
		return false;
	}

	public static string HtmlDecode(string text)
	{
		if (Strings.Len(text) == 0)
		{
			return "";
		}
		return WebUtility.HtmlDecode(text.Replace("&amp;", "&")).Replace("\n", "").Replace("\r", "")
			.Replace("\t", "");
	}

	public static string HtmlEncode(string text)
	{
		if (Strings.Len(text) == 0)
		{
			return "";
		}
		char[] array = WebUtility.HtmlEncode(HtmlDecode(text)).ToCharArray();
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		char[] array2 = array;
		foreach (char value in array2)
		{
			int num = Convert.ToInt32(value);
			if (num > 31 && num < 127 && num != 96)
			{
				stringBuilder.Append(value);
				continue;
			}
			stringBuilder.Append("&#");
			stringBuilder.Append(num);
			stringBuilder.Append(";");
		}
		return stringBuilder.ToString();
	}

	internal static bool IsAscii(string text, ref int lPos)
	{
		int num = 0;
		if (Strings.Len(Strings.Trim(text)) != 0)
		{
			char[] array = text.ToCharArray();
			for (int i = 0; i < array.Length; i++)
			{
				if (Convert.ToInt32(array[i]) > 126)
				{
					lPos = num;
					return false;
				}
				num++;
			}
		}
		return true;
	}

	internal static bool IsEbook(string sVal)
	{
		if (!sVal.Contains("a5|"))
		{
			return sVal.Contains("z2|");
		}
		return true;
	}

	internal static bool IsEro(string sVal)
	{
		if (sVal.Contains("d2"))
		{
			if (sVal.Contains("d23|"))
			{
				return true;
			}
			if (sVal.Contains("d24|"))
			{
				return true;
			}
			if (sVal.Contains("d25|"))
			{
				return true;
			}
			if (sVal.Contains("d26|"))
			{
				return true;
			}
		}
		if (sVal.Contains("d7"))
		{
			if (sVal.Contains("d72|"))
			{
				return true;
			}
			if (sVal.Contains("d73|"))
			{
				return true;
			}
			if (sVal.Contains("d74|"))
			{
				return true;
			}
			if (sVal.Contains("d75|"))
			{
				return true;
			}
		}
		return sVal.Contains("z3|");
	}

	public static bool IsNzb(string fileLnk, ref long sSize)
	{
		try
		{
			if (fileLnk == null)
			{
				return false;
			}
			if (Strings.Len(fileLnk) == 0)
			{
				return false;
			}
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			xmlDocument.LoadXml(fileLnk);
			XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);
			xmlNamespaceManager.AddNamespace("pf", "http://www.newzbin.com/DTD/2003/nzb");
			if (xmlDocument.DocumentElement.Name == "nzb")
			{
				IEnumerator enumerator = null;
				try
				{
					enumerator = xmlDocument.SelectNodes("/pf:nzb/pf:file/pf:segments/pf:segment", xmlNamespaceManager).GetEnumerator();
					while (enumerator.MoveNext())
					{
						XmlNode xmlNode = (XmlNode)enumerator.Current;
						sSize += Conversions.ToLong(xmlNode.Attributes["bytes"].InnerXml);
					}
				}
				finally
				{
					if (enumerator is IDisposable)
					{
						(enumerator as IDisposable).Dispose();
					}
				}
				return sSize > 0;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return false;
	}

	internal static bool IsTv(string sVal)
	{
		if (!sVal.Contains("b4|") && !sVal.Contains("d11|"))
		{
			return sVal.Contains("z1|");
		}
		return true;
	}

	internal static Encoding LatinEnc()
	{
		return Encoding.GetEncoding(28591);
	}

	internal static string MakeAscii(string text)
	{
		if (Strings.Len(Strings.Trim(text)) == 0)
		{
			return "";
		}
		char[] array = text.ToCharArray();
		StringBuilder stringBuilder = new StringBuilder();
		char[] array2 = array;
		foreach (char value in array2)
		{
			int num = Convert.ToInt32(value);
			if (num < 127 && num > 31)
			{
				stringBuilder.Append(value);
			}
		}
		return stringBuilder.ToString();
	}

	internal static byte[] MakeLatin(string zText)
	{
		return LatinEnc().GetBytes(zText);
	}

	public static string MakeMsg(string sMes, bool tag = true)
	{
		if (sMes.StartsWith("<"))
		{
			if (tag)
			{
				return sMes;
			}
		}
		else if (!tag)
		{
			return sMes;
		}
		if (tag)
		{
			return "<" + sMes + ">";
		}
		return sMes.Substring(1, sMes.Length - 2);
	}

	public static string MakeNzbs(string sTitle, string sGroup, bool secondServer, bool looser, bool onlyCol, long maxDays, ref bool stopSearch, bool allowIncomplete, ref bool noResults, bool exCheck)
	{
		long num = 0L;
		bool flag = false;
		try
		{
			if (Strings.Len(sTitle) > 0)
			{
				string text = "q=" + UrlEncode((looser ? "" : "\"") + sTitle + (looser ? "" : "\"")) + "&m=&max=250" + ((sGroup.Length > 0) ? ("&adv_g=" + sGroup) : "") + "&adv_age=" + ((maxDays == 0L) ? "1400" : Conversions.ToString(maxDays)) + "&adv_sort=subject" + (onlyCol ? "&adv_col=on" : "") + (secondServer ? "&server=2" : "");
				string text2;
				while (true)
				{
					try
					{
						text2 = new WebClient().DownloadString("http://" + GetBinsearch() + "/index.php?" + text);
					}
					catch (Exception ex)
					{
						if (ex.Message.Contains("404"))
						{
							return null;
						}
						num++;
						if (num > 3)
						{
							stopSearch = true;
							return null;
						}
						Wait(1100);
						continue;
					}
					break;
				}
				text2 = text2.ToLower();
				if (text2.Length == 0)
				{
					stopSearch = true;
					return null;
				}
				if (text2.Contains("group <b></b> does not exist.."))
				{
					return null;
				}
				if (exCheck && !text2.Contains("2010 binsearch"))
				{
					Interaction.MsgBox(text2, MsgBoxStyle.Critical, "Binsearch Error (3) - " + Conversions.ToString(text2.Length));
					stopSearch = true;
					return null;
				}
				if (text2.Contains("<font color='red'>") && !allowIncomplete)
				{
					stopSearch = true;
					return null;
				}
				while (text2.Contains("<!--") & text2.Contains("-->"))
				{
					string text3 = text2.Substring(0, text2.IndexOf("<!--", StringComparison.Ordinal));
					string text4 = text2.Substring(text2.IndexOf("-->", text3.Length, StringComparison.Ordinal) + 3);
					text2 = text3 + text4;
				}
				if (text2.Contains("<td><input type=\"checkbox\" name="))
				{
					string[] array = Strings.Split(text2, "<td><input type=\"checkbox\" name=");
					if (Information.UBound(array) == 1)
					{
						if (text2.Contains("&amp;g=free.pt"))
						{
							noResults = true;
							return null;
						}
					}
					else
					{
						string text5 = sGroup.ToLower() + "&amp;a=";
						if (!text2.Contains(text5))
						{
							return null;
						}
						string[] array2 = Strings.Split(text2, text5);
						array2[1] = array2[1].Substring(array2[1].IndexOf(">", StringComparison.Ordinal) + 1);
						array2[1] = array2[1].Substring(0, array2[1].IndexOf("<", StringComparison.Ordinal));
						string[] array3 = Strings.Split(text2, ">" + array2[1] + "<");
						if (array3.Length != array.Length)
						{
							if (!text2.Contains("&amp;g=free.pt"))
							{
								return null;
							}
							flag = true;
							if (array3.Length != array.Length - 1)
							{
								return null;
							}
						}
					}
					string text6 = "";
					for (int i = 1; i < array.Length; i++)
					{
						if (!array[i].StartsWith("\""))
						{
							return null;
						}
						if (!flag || !array[i].Contains("&amp;g=free.pt"))
						{
							text6 = text6 + array[i].Substring(1, array[i].IndexOf('"', 2) - 1) + "=on&";
						}
					}
					if (text6.IsNullOrEmpty())
					{
						return null;
					}
					string zError = "";
					return GetNzb(text6 + "action=nzb", text, ref zError);
				}
				noResults = true;
				return null;
			}
			return null;
		}
		catch (Exception ex2)
		{
			Interaction.MsgBox(ex2.Message, MsgBoxStyle.Critical, "Binsearch Error");
			stopSearch = true;
			return null;
		}
	}

	internal static string MakeP(string sIn)
	{
		return Convert.ToBase64String(MakeLatin(sIn)).Replace("=", "%3d").Replace("+", "%2b")
			.Replace("&", "%26")
			.Replace("/", "%2f")
			.Trim();
	}

	/// <summary>
	/// Builds a public-key-only verifier for a spot modulus.
	/// </summary>
	/// <remarks>
	/// Constructing an <see cref="RSACryptoServiceProvider"/> allocates a Windows CryptoAPI
	/// key container. Header import calls this once per spot and a small number of posters
	/// account for most spots, so verifiers are cached by modulus and reused for the life
	/// of the process. That also stops the handle leak: the old code never disposed them.
	///
	/// Measured (tools/DbDiagnostic bench), construction is roughly 6us against 24us for
	/// the VerifyHash it enables - about a fifth of the cost, not the bulk of it. The cache
	/// is worth having, but the remaining per-spot cost is the verification itself and only
	/// comes down by verifying on more than one thread.
	///
	/// Entries are never evicted, so callers such as <see cref="GetRsa"/> may hold on to
	/// what they get back. The cache stops growing at <see cref="RsaCacheLimit"/> and
	/// falls back to constructing per call, which is only the old behaviour.
	///
	/// Instance members of RSACryptoServiceProvider are not documented as thread-safe.
	/// Verification runs on a single thread today; parallelizing it means giving each
	/// worker its own cache rather than sharing this one.
	/// </remarks>
	internal static RSACryptoServiceProvider MakeRsa(string sModulus)
	{
		if (sModulus.IsNullOrEmpty() || sModulus.Length % 4 != 0)
		{
			return null;
		}
		if (RsaCache.TryGetValue(sModulus, out RSACryptoServiceProvider cached))
		{
			return cached;
		}
		RSACryptoServiceProvider created = CreateRsa(sModulus);
		if (RsaCache.Count < RsaCacheLimit)
		{
			RsaCache[sModulus] = created;
		}
		return created;
	}

	private static RSACryptoServiceProvider CreateRsa(string sModulus)
	{
		try
		{
			// RSAParameters is built per call: it used to be a static field mutated in
			// place here, which made this method unsafe to call from more than one thread.
			RSAParameters parameters = default(RSAParameters);
			parameters.Exponent = RsaExponent;
			parameters.Modulus = Convert.FromBase64String(sModulus);
			RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
			rSACryptoServiceProvider.ImportParameters(parameters);
			return rSACryptoServiceProvider;
		}
		catch (Exception)
		{
			return null;
		}
	}

	internal static string Parse(string input)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		bool flag = false;
		int num = 0;
		while (num < input.Length)
		{
			char c = input[num];
			switch (c)
			{
			case '=':
			{
				char c2 = ((num == input.Length - 1) ? ' ' : input[num + 1]);
				if (Conversions.ToString(c2) == "?")
				{
					flag = true;
				}
				break;
			}
			case '?':
			{
				char c2 = ((num == input.Length - 1) ? ' ' : input[num + 1]);
				if (!(Conversions.ToString(c2) != "="))
				{
					flag = false;
					stringBuilder2.Append(c);
					stringBuilder2.Append(c2);
					stringBuilder.Append(ParseEncodedWord(stringBuilder2.ToString()));
					stringBuilder2 = new StringBuilder();
					num += 2;
					continue;
				}
				break;
			}
			}
			if (flag)
			{
				stringBuilder2.Append(c);
				num++;
			}
			else
			{
				stringBuilder.Append(c);
				num++;
			}
		}
		return stringBuilder.ToString();
	}

	private static string ParseEncodedWord(string input)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string result;
		try
		{
			if (!input.StartsWith("="))
			{
				return input;
			}
			if (!input.EndsWith("?="))
			{
				return input;
			}
			string text = input.Substring(2, input.IndexOf("?", 2, StringComparison.Ordinal) - 2);
			if (text.ToUpper() == "UTF8")
			{
				text = "UTF-8";
			}
			Encoding encoding = Encoding.GetEncoding(text);
			char c = input[text.Length + 3];
			int num = text.Length + 5;
			char c2 = char.ToLowerInvariant(c);
			if (c2 != 'b')
			{
				if (c2 == 'q')
				{
					while (num < input.Length)
					{
						char c3 = input[num];
						switch (c3)
						{
						case '=':
						{
							char[] array = ((num >= input.Length - 2) ? null : new char[2]
							{
								input[num + 1],
								input[num + 2]
							});
							if (array != null)
							{
								string @string = encoding.GetString(new byte[1] { Convert.ToByte(new string(array, 0, 2), 16) });
								stringBuilder.Append(@string);
								num += 3;
							}
							break;
						}
						case '?':
							if (Conversions.ToString(input[num + 1]) == "=")
							{
								num += 2;
							}
							break;
						default:
							stringBuilder.Append(c3);
							num++;
							break;
						}
					}
				}
			}
			else
			{
				byte[] bytes = Convert.FromBase64String(input.Substring(num, input.Length - num - 2));
				stringBuilder.Append(encoding.GetString(bytes));
			}
			result = stringBuilder.ToString();
		}
		catch (Exception ex)
		{
			result = null;
			Log.Warn(ex.Message);
		}
		return result;
	}

	internal static bool PostData(Engine tPhuse, List<string> data, string subject, string zFrom, string zGroup, string zExtra, ref string xOutId, string msgId, ref string sError)
	{
		long num = 0L;
		string text = "";
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(tPhuse);
		foreach (string datum in data)
		{
			num++;
			sError = null;
			string text2 = ((msgId.Length == 0) ? CreateMsgId() : MakeMsg(msgId));
			string text3 = string.Concat("From: ", zFrom, "\r\nSubject: ", subject, (data.Count > 1) ? (" [" + Conversions.ToString(num) + "/" + Conversions.ToString(data.Count) + "]") : "", "\r\nNewsgroups: ", zGroup, "\r\nMessage-ID: ", text2, "\r\nX-Newsreader: Spotnet ", AppHelper.AppVersion, "\r\n", zExtra, "Content-Type: text/plain; charset=ISO-8859-1\r\nContent-Transfer-Encoding: 8bit");
			string resp = "";
			string text4 = datum;
			if (!text4.EndsWith("\r\n"))
			{
				text4 += "\r\n";
			}
			if (nNTP.PostData(zGroup, text3 + "\r\n\r\n" + text4 + ".", ref resp, out var _, ref sError))
			{
				text = text + MakeMsg(text2, tag: false) + " ";
				continue;
			}
			return false;
		}
		xOutId = text.Trim();
		return true;
	}

	internal static string SpecialString(string sDataIn)
	{
		return sDataIn.Replace("/", "-s").Replace("+", "-p").Replace("=", "");
	}

	internal static string[] SplitBySizEx(string strInput, int iSize)
	{
		int length = strInput.Length;
		int num = (int)Math.Round((double)length / (double)iSize);
		if (length % iSize != 0)
		{
			num++;
		}
		int num2 = 0;
		string[] array = new string[num + 1];
		int num3 = length;
		for (int i = 0; ((iSize >> 31) ^ i) <= ((iSize >> 31) ^ num3); i += iSize)
		{
			array[num2] = Strings.Mid(strInput, i + 1, iSize);
			num2++;
		}
		return array;
	}

	internal static List<string> SplitLines(string sIn, bool allowBlankLines, int lMax)
	{
		long num = 0L;
		List<string> list = new List<string>();
		string[] array = Strings.Split(sIn, "\r\n");
		StringBuilder stringBuilder = new StringBuilder();
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			string[] array3 = SplitBySizEx(array2[i], lMax);
			foreach (string text in array3)
			{
				if (text != null && (text.Length > 0 || allowBlankLines))
				{
					if (text.StartsWith("."))
					{
						stringBuilder.AppendLine("." + text);
					}
					else
					{
						stringBuilder.AppendLine(text);
					}
					num++;
				}
			}
		}
		if (num > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	internal static List<string> SplitLinesGzip(string sIn)
	{
		long num = 0L;
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		if (sIn == null)
		{
			return null;
		}
		string[] array = SplitBySizEx(sIn, 900);
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] != null && Strings.Len(array[i]) > 0)
			{
				num++;
				if (array[i].StartsWith("."))
				{
					array[i] = "." + array[i];
				}
				stringBuilder.AppendLine(array[i].Replace("=", "=D").Replace("\n", "=C").Replace("\r", "=B")
					.Replace("\0", "=A"));
				if (num == 900)
				{
					list.Add(stringBuilder.ToString());
					num = 0L;
					stringBuilder = new StringBuilder();
				}
			}
		}
		if (num > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	internal static string SplitLinesXml(string sIn, string sPrefix, int lMax)
	{
		string[] array = Strings.Split(sIn, "\r\n");
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = SplitBySizEx(array[i], lMax);
			for (int j = 0; j < array2.Length; j++)
			{
				if (array2[j] != null && Strings.Len(array2[j]) > 0)
				{
					stringBuilder.AppendLine(sPrefix + " " + array2[j]);
				}
			}
		}
		return stringBuilder.ToString();
	}

	public static string StripNonAlphaNumericCharacters(string sText)
	{
		return Regex.Replace(sText, "[^A-Za-z0-9]", "").Trim();
	}

	private static string UnGzip(byte[] inz)
	{
		try
		{
			return new StreamReader(new GZipStream(new MemoryStream(inz), CompressionMode.Decompress, leaveOpen: true), LatinEnc()).ReadToEnd();
		}
		catch (Exception)
		{
			return null;
		}
	}

	internal static string UnSpecialString(string sDataIn)
	{
		return sDataIn.Replace("-s", "/").Replace("-p", "+");
	}

	internal static string UnzipStr(ref byte[] inz)
	{
		try
		{
			return new StreamReader(new DeflateStream(new MemoryStream(inz), CompressionMode.Decompress, leaveOpen: true), LatinEnc()).ReadToEnd();
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			return null;
		}
	}

	private static string UrlEncode(string text)
	{
		string sDest = text;
		for (int i = sDest.Length; i >= 1; i += -1)
		{
			int num = Strings.Asc(Strings.Mid(sDest, i, 1));
			int num2 = num;
			if ((num2 < 48 || num2 > 57) && (num2 < 65 || num2 > 90) && (num2 < 97 || num2 > 122))
			{
				switch (num2)
				{
				case 32:
					StringType.MidStmtStr(ref sDest, i, 1, "+");
					break;
				default:
					sDest = Strings.Left(sDest, i - 1) + "%" + Conversion.Hex(num) + Strings.Mid(sDest, i + 1);
					break;
				case 42:
				case 46:
				case 47:
				case 58:
				case 95:
					break;
				}
			}
		}
		return sDest;
	}

	internal static string UtfEncode(string sInput)
	{
		int num = 0;
		byte[] array = new byte[1];
		StringBuilder stringBuilder = new StringBuilder();
		char[] array2 = sInput.ToCharArray();
		for (int i = 0; i < array2.Length; i++)
		{
			int num2 = Strings.Asc(array2[i]);
			if (num2 < 32 || num2 == 63 || num2 > 126)
			{
				if ((uint)(num2 - 9) <= 1u || num2 == 13)
				{
					if (num > 0)
					{
						stringBuilder.Append("=?UTF-8?B?" + Convert.ToBase64String(array) + "?=");
					}
					num = 0;
					stringBuilder.Append(array2[i]);
				}
				else
				{
					array = (byte[])Utils.CopyArray(array, new byte[num + 1]);
					array[num] = (byte)num2;
					num++;
				}
			}
			else
			{
				if (num > 0)
				{
					stringBuilder.Append("=?UTF-8?B?" + Convert.ToBase64String(array) + "?=");
				}
				num = 0;
				stringBuilder.Append(array2[i]);
			}
		}
		if (num > 0)
		{
			stringBuilder.Append("=?UTF-8?B?" + Convert.ToBase64String(array) + "?=");
		}
		return stringBuilder.ToString();
	}

	public static void Wait(int ms)
	{
		using ManualResetEvent manualResetEvent = new ManualResetEvent(initialState: false);
		manualResetEvent.WaitOne(ms);
	}

	internal static string ZipStr(byte[] inz)
	{
		try
		{
			using MemoryStream memoryStream = new MemoryStream();
			using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
			{
				deflateStream.Write(inz, 0, inz.Length);
			}
			memoryStream.Seek(0L, SeekOrigin.Begin);
			return new StreamReader(memoryStream, LatinEnc()).ReadToEnd();
		}
		catch (Exception)
		{
			return null;
		}
	}

	internal static bool DoDownload(string pathToNzb, DownloaderItemViewModel item)
	{
		if (pathToNzb.IsNullOrEmpty())
		{
			throw new ArgumentNullException("pathToNzb");
		}
		if (!System.IO.File.Exists(pathToNzb))
		{
			throw new Exception("NZB file not found: " + pathToNzb);
		}
		switch (Settings.Default.DownloadAction)
		{
		case 0:
		case 1:
			return Sys.MainWindow.ScheduleNzbDownload(pathToNzb, item);
		case 2:
			Task.Run(delegate
			{
				try
				{
					Process process = new Process();
					ProcessStartInfo startInfo = new ProcessStartInfo(pathToNzb)
					{
						UseShellExecute = true,
						WindowStyle = ProcessWindowStyle.Normal
					};
					process.StartInfo = startInfo;
					process.Start();
				}
				catch (Exception ex2)
				{
					Log.Debug("Path to nzb: " + pathToNzb);
					Log.Exception(ex2, showToClient: true);
				}
			});
			break;
		case 3:
		{
			string text = "";
			try
			{
				text = AskFile(item.Titel);
				if (!text.IsNullOrEmpty())
				{
					System.IO.File.Copy(pathToNzb, text, overwrite: true);
				}
			}
			catch (Exception ex)
			{
				Log.Debug("Path to nzb: " + pathToNzb + ". New path: " + text);
				Log.Exception(ex, showToClient: true);
			}
			break;
		}
		}
		return true;
	}

	public static void DownloadNzbAndStartDownloadItem(SpotEx spot)
	{
		DownloaderItemViewModel item = null;
		try
		{
			if (Settings.Default.DownloadAction <= 1 && !spot.Title.IsNullOrEmpty())
			{
				if (Sys.Downloader.IsDownloadInQueueAlready(spot.MessageId, out item))
				{
					item.Blink();
					return;
				}
				item = Sys.Downloader.AddFakeItemBeforeNzbDownloaded(spot.Title, spot.MessageId, spot.Category);
			}
			string text = ((spot.OldInfo != null) ? AppHelper.FindNzb(AppHelper.HtmlDecode(spot.OldInfo.FileName), spot.OldInfo.Groups.Split('|')[0], spot.Title) : (spot.NZR.IsNullOrEmpty() ? OpenNzb(spot.NZB, spot.Title) : OpenNzb(spot.NZR, spot.Title, spot.NZRKey)));
			if (!text.IsNullOrEmpty())
			{
				if (item == null)
				{
					item = DownloaderItemFactory.New(spot.Title);
				}
				// The spot body only exists here. Unpack re-reads the NZB later for the
				// same purpose, but by then the description is gone, so whatever the
				// poster wrote there has to be picked up now.
				ApplyDetectedUnpackPassword(item, spot);
				DoDownload(text, item);
			}
			else if (item != null)
			{
				Sys.Downloader.RemoveItemsAsync(new DownloaderItemViewModel[1] { item });
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	/// <summary>
	/// Carries the archive password out of the spot text onto the queued download.
	/// </summary>
	/// <remarks>
	/// Only fills an empty value, so a password the user set on a re-queued item survives.
	/// The body is checked before the title: a title mentioning a password is usually
	/// repeating what the body states in full.
	/// </remarks>
	private static void ApplyDetectedUnpackPassword(DownloaderItemViewModel item, SpotEx spot)
	{
		if (item == null || spot == null || !item.UnpackPassword.IsNullOrEmpty())
		{
			return;
		}
		try
		{
			string detected = UnpackPasswordDetector.FromDescription(spot.Body)
				?? UnpackPasswordDetector.FromDescription(spot.Title);
			if (!detected.IsNullOrEmpty())
			{
				item.UnpackPassword = detected;
				Log.Debug("Unpack password taken from the spot text for " + spot.MessageId);
			}
		}
		catch (Exception ex)
		{
			Log.Debug("Failed to look for an unpack password in the spot text: " + ex.Message);
		}
	}

	private static string OpenNzb(string sLoc, string title, int nzrKey = -1)
	{
		try
		{
			if (sLoc.IsNullOrWhiteSpace())
			{
				AppHelper.Error(Words.NoSegments);
				return null;
			}
			string sxOut;
			if (sLoc.ToLower().Trim().StartsWith("http:") || sLoc.ToLower().StartsWith("https:"))
			{
				try
				{
					using WebClient webClient = new WebClient();
					sxOut = webClient.DownloadString(sLoc.ToLower().Trim());
				}
				catch (WebException ex)
				{
					AppHelper.Error("Newznab server error on details: " + ex.Message);
					return null;
				}
			}
			else
			{
				List<string> list = (from s in sLoc.Split(' ')
					select MakeMsg(s)).ToList();
				if (list.Count == 0)
				{
					AppHelper.Error(Words.NoSegments);
					return null;
				}
				if (!Spots.GetNzb(AppHelper.DownloadPhuse, Settings.Default.NZBGroup, list, out sxOut, out var sError))
				{
					AppHelper.Error(sError);
					return null;
				}
			}
			if (nzrKey >= 0)
			{
				sxOut = NzrDecoder.Decode(sxOut, nzrKey);
				if (sxOut.IsNullOrEmpty())
				{
					AppHelper.Error(Words.ErrorWhileParsing + " NZB context cannot be decoded.");
					return null;
				}
			}
			try
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.XmlResolver = null;
				xmlDocument.LoadXml(sxOut);
			}
			catch (Exception ex2)
			{
				Log.Exception(ex2);
				AppHelper.Error(Words.ErrorWhileParsing + " " + ex2.Message);
				return null;
			}
			string text = AppHelper.GenerateNzbFilePath(AppHelper.MakeFilename(title).Trim() + ".nzb");
			try
			{
				System.IO.File.WriteAllText(text, sxOut, AppHelper.LatinEnc());
			}
			catch (Exception ex3)
			{
				Log.Exception(ex3);
				AppHelper.Error(Words.ErrorOnWriting + " " + ex3.Message);
				return null;
			}
			return text;
		}
		catch (Exception ex4)
		{
			Log.Exception(ex4, showToClient: true);
			return null;
		}
	}

	private static string AskFile(string sFile)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			AddExtension = true,
			AutoUpgradeEnabled = true,
			CheckFileExists = false,
			CheckPathExists = true,
			CreatePrompt = false,
			DefaultExt = "nzb",
			Filter = Words.NZBFiles,
			FilterIndex = 1,
			InitialDirectory = ((!Settings.Default.LastFolder.Trim().IsNullOrEmpty()) ? Settings.Default.LastFolder : AppHelper.DesktopDirectory),
			OverwritePrompt = true,
			RestoreDirectory = true,
			Title = Words.NZBSave,
			FileName = AppHelper.MakeFilename(sFile)
		};
		if (System.Windows.Application.Current.Dispatcher.Invoke((Func<DialogResult>)saveFileDialog.ShowDialog) != DialogResult.OK || saveFileDialog.FileName.IsNullOrWhiteSpace())
		{
			return null;
		}
		Settings.Default.LastFolder = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
		Settings.Default.Save();
		return saveFileDialog.FileName;
	}
}
