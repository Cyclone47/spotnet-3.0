using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Utilities;

internal class SpotParser
{
	private static readonly Logger Log;

	private static string _commentHtml;

	private static readonly Regex LinkRegEx;

	private static readonly string LinkPattern;

	private static readonly string EmailPattern;

	private static string _spotHtm;

	private const string InfoRowStyle = "padding-right:15px;word-wrap:normal;";

	/// <summary>
	/// Scheme prefix for theme images, scripts and stylesheets inside a rendered page.
	/// </summary>
	/// <remarks>
	/// Both engines open the document from a file on disk, so both resolve these the same
	/// way. This used to return an "asset://" prefix under WebView2, for a virtual host
	/// mapping that was never wired up and that a file:// document does not need.
	/// </remarks>
	internal const string LocalFilePrefix = "file://";

	static SpotParser()
	{
		Log = LogManager.GetCurrentClassLogger();
		EmailPattern = "[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*";
		LinkPattern = "\\(?\\b(https|http)://[-A-Za-z0-9+&@#/%?=~_()|!:,.;]*[-A-Za-z0-9+&@#/%=~_()|]";
		LinkRegEx = new Regex(LinkPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
	}

	private static string CreateClassLink(string sTarget, string sText, string sClass, string sTooltip = "", string id = "")
	{
		return "<A id='" + id + "' CLASS='" + sClass + "' " + ((!sTooltip.IsNullOrEmpty()) ? (" TITLE='" + sTooltip.Replace("'", "''") + "'") : " ") + " onfocus='this.blur()' HREF='" + sTarget.Replace("'", "''") + "'>" + AppHelper.HtmlEncode(sText) + "</A>";
	}

	private static string DefaultAvatar(string sModulus)
	{
		string text = AppHelper.MakeMd5(sModulus) ?? Words.Unknown;
		return "http://www.gravatar.com/avatar/" + text + "?s=32&d=identicon";
	}

	internal static string GetAvatar(string sAvatar, string sModulus)
	{
		if (sAvatar.IsNullOrEmpty())
		{
			return DefaultAvatar(sModulus);
		}
		try
		{
			string text = "";
			Bitmap bitmap = new Bitmap(new MemoryStream(Convert.FromBase64String(sAvatar)));
			ImageFormat rawFormat = bitmap.RawFormat;
			Bitmap bitmap2 = null;
			if (bitmap.Width > 32 || bitmap.Height > 32)
			{
				bitmap2 = bitmap.Resize(32, 32);
			}
			if (rawFormat.Equals(ImageFormat.Jpeg))
			{
				text = "jpeg";
			}
			if (rawFormat.Equals(ImageFormat.Bmp))
			{
				text = "bmp";
			}
			if (rawFormat.Equals(ImageFormat.Gif))
			{
				text = "gif";
			}
			if (rawFormat.Equals(ImageFormat.Png))
			{
				text = "png";
			}
			if (text.IsNullOrEmpty())
			{
				return DefaultAvatar(sModulus);
			}
			if (bitmap2 != null)
			{
				sAvatar = Convert.ToBase64String(bitmap2.ToByteArray());
			}
			return "data:image/" + text + ";base64," + sAvatar;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return DefaultAvatar(sModulus);
	}

	internal static string GeneratePosterLinksHtmlCode(string sPoster, UserInfo zUser, PosterIdentType posterIdent)
	{
		string sTooltip = Words.Unknown;
		if (!zUser.Organisation.IsNullOrEmpty())
		{
			sTooltip = AppHelper.HtmlEncode(zUser.Organisation);
		}
		string sClass = "from";
		switch (posterIdent)
		{
		case PosterIdentType.Black:
		case PosterIdentType.Fake:
			sClass = "untrusted";
			break;
		case PosterIdentType.White:
			sClass = "trusted";
			break;
		}
		string text = (zUser.ValidSignature ? AppHelper.MakeUnique(zUser.Modulus) : "INVALID_SIGNATURE");
		return CreateClassLink("menu:" + (zUser.ValidSignature ? zUser.Modulus : "") + "_" + AppHelper.StripNonAlphaNumericCharacters(sPoster), AppHelper.StripNonAlphaNumericCharacters(sPoster) + " (" + text + ")", sClass, sTooltip, "PosterIdentNames");
	}

	internal static string GeneratePosterIdentLabelHtmlCode(PosterIdentType posterIdent)
	{
		string arg = "none";
		string text = "";
		switch (posterIdent)
		{
		case PosterIdentType.Black:
			arg = "posteridentblack";
			text = Words.PosterIdentBlackLetter;
			break;
		case PosterIdentType.Fake:
			arg = "posteridentfake";
			text = Words.PosterIdentUntrustedLetter;
			break;
		case PosterIdentType.White:
			arg = "posteridentwhite";
			text = Words.PosterIdentWhiteLetter;
			break;
		case PosterIdentType.Verified:
			arg = "posteridenttrusted";
			text = Words.PosterIdentTrustedLetter;
			break;
		}
		if (!text.IsNullOrEmpty())
		{
			return $"<label class='{arg}' style='display: inline;'>[{text}]</label>";
		}
		return "";
	}

	internal static string GetSpotDescriptionAsText(SpotEx spotEx)
	{
		return HtmlToPlainText(GetSpotDescriptionLines(spotEx, 20));
	}

	private static string HtmlToPlainText(string html)
	{
		Regex regex = new Regex("<(br|BR)\\s{0,1}\\/{0,1}>", RegexOptions.Multiline);
		Regex regex2 = new Regex("<[^>]*(>|$)", RegexOptions.Multiline);
		Regex regex3 = new Regex("(>|$)(\\W|\\n|\\r)+<", RegexOptions.Multiline);
		string value = html;
		value = WebUtility.HtmlDecode(value);
		value = regex3.Replace(value, "><");
		value = regex.Replace(value, Environment.NewLine);
		return regex2.Replace(value, string.Empty);
	}

	private static string MakeCommentBody(string body, string bodyId)
	{
		if (body.IsNullOrEmpty())
		{
			return $"<span id='{bodyId}'/>";
		}
		string text = Strings.Replace(body.Trim(), "[br]", "\r\n", 1, -1, CompareMethod.Text);
		while (text.StartsWith("\r\n"))
		{
			text = text.Substring(2);
		}
		while (text.StartsWith("\n"))
		{
			text = text.Substring(1);
		}
		while (text.EndsWith("\r\n"))
		{
			text = text.Substring(0, text.Length - 2);
		}
		while (text.EndsWith("\n"))
		{
			text = text.Substring(0, text.Length - 1);
		}
		string text2 = text.Replace("\t", " ").Replace("\r\n", "\t").Replace("\n", "\t");
		if (text2.Split("\t".ToCharArray()).GetUpperBound(0) > 15)
		{
			text = text2.Replace("\t", " ");
		}
		text = Linkify(AppHelper.HtmlEncode(text.Replace("\r\n", "[br]").Replace("\n", "[br]").Replace("&#", "[aa]")));
		if (Settings.Default.IsEnabledSmiles)
		{
			text = InsertSmileys(text);
		}
		text = text.ReplaceIgnoreCase("[br]", "<br>");
		text = text.ReplaceIgnoreCase("[aa]", "&#");
		if (Settings.Default.IsEnabledUbbForComment)
		{
			text = text.ReplaceIgnoreCase("[b]", "<b>");
			text = text.ReplaceIgnoreCase("[/b]", "</b>");
			text = text.ReplaceIgnoreCase("[u]", "<u>");
			text = text.ReplaceIgnoreCase("[/u]", "</u>");
			text = text.ReplaceIgnoreCase("[i]", "<i>");
			text = text.ReplaceIgnoreCase("[/i]", "</i>");
			text = new Regex("\\[color=(\"|&quot;)?(#?[a-zA-Z0-9]+)(\"|&quot;)?\\]", RegexOptions.IgnoreCase).Replace(text, "<font color=\"$2\">");
			text = text.ReplaceIgnoreCase("[/color]", "</font>");
			text = UbbParseQuoteForComments(text);
			text = UbbParseLinkToAnotherSpot(text);
		}
		if (Settings.Default.IsEnabledBadWordsFilterForComment)
		{
			IOrderedEnumerable<string> orderedEnumerable = AppHelper.BadWordsSet();
			if (orderedEnumerable != null)
			{
				foreach (string item in orderedEnumerable)
				{
					text = Regex.Replace(text, "(" + item.Trim() + ")", "<span onmouseover='this.innerHTML=\"$1\"' onmouseout='this.innerHTML=\"***\"'>***</span>", RegexOptions.IgnoreCase);
				}
			}
		}
		return $"<span id='{bodyId}'>{text}</span>";
	}

	private static string UbbParseLinkToAnotherSpot(string str)
	{
		str = new Regex("\\[url=(&quot;)?spotnet://(" + EmailPattern + ")(&quot;)?\\]", RegexOptions.IgnoreCase).Replace(str, LinkToTheSpotUbbEvaluator);
		str = Regex.Replace(str, "\\[/url\\]", "</a>", RegexOptions.IgnoreCase);
		return str;
	}

	private static string UbbParseQuoteForComments(string str)
	{
		str = Regex.Replace(str, "\\[quote=(&quot;)?([A-Za-z0-9\\-_# ()]+)(&quot;)?\\]", "<blockquote><cite style='display:block;'>$2 " + Words.Wrote + ":</cite>", RegexOptions.IgnoreCase);
		str = Regex.Replace(str, "\\[/quote\\](<br>|[ \\r\\n])*", "</blockquote>", RegexOptions.IgnoreCase);
		return str;
	}

	private static string GetSpotDescriptionLines(SpotEx spotEx, int numberOfLines)
	{
		string text = MakeDesc(spotEx.Body.Trim(), spotEx.OldInfo != null);
		int num = -4;
		for (int i = 0; i < numberOfLines; i++)
		{
			if (num == -1)
			{
				break;
			}
			num = text.IndexOf("<br>", num + 4, StringComparison.Ordinal);
		}
		if (num != -1)
		{
			return text.Substring(0, num);
		}
		return text;
	}

	private static string MakeCats(int hCat, char sCat, Dictionary<string, string> theCats, string sColor, string sColor2)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (theCats.Count > 0)
		{
			string text = null;
			stringBuilder.Append("<TR><TD align=\"right\" style=\"padding-right:15px\"><b>" + AppHelper.TranslateCatDesc((AppHelper.SpotCategory)hCat, sCat.ToString(CultureInfo.InvariantCulture)) + "</b></TD><TD>");
			foreach (string key in theCats.Keys)
			{
				if (hCat == 1 || hCat == 6)
				{
					stringBuilder.Append(text + CreateClassLink("query:" + AppHelper.UrlEncode(theCats[key] + "_cats MATCH '" + AppHelper.StripNonAlphaNumericCharacters("1" + key) + " OR " + AppHelper.StripNonAlphaNumericCharacters("6" + key) + "'"), theCats[key], "category", Words.Search) + "</TD></TR>");
				}
				else
				{
					stringBuilder.Append(text + CreateClassLink("query:" + AppHelper.UrlEncode(theCats[key] + "_cats MATCH '" + AppHelper.StripNonAlphaNumericCharacters(hCat + key) + "'"), theCats[key], "category", Words.Search) + "</TD></TR>");
				}
				text = "<TR><TD>&nbsp;</TD><TD>";
			}
		}
		return stringBuilder.ToStringSafely();
	}

	private static string MakeDesc(string text, bool oldInfo, bool ubbSimple = true, bool ubbAdvanced = true)
	{
		if (text.IsNullOrEmpty())
		{
			return "";
		}
		text = text.ReplaceIgnoreCase("[br]", "\r\n");
		StringBuilder stringBuilder = new StringBuilder(text);
		while (stringBuilder.ToString(0, 2).Equals("\r\n"))
		{
			stringBuilder.Remove(0, 2);
		}
		while (stringBuilder.ToString(stringBuilder.Length - 2, 2).Equals("\r\n"))
		{
			stringBuilder.Remove(stringBuilder.Length - 2, 2);
		}
		stringBuilder.Replace("\r\n", "[br]").Replace("&#", "[aa]");
		text = stringBuilder.ToString();
		if (oldInfo)
		{
			text = text.ReplaceIgnoreCase("&amp;lt;br />", "[br]");
			text = text.ReplaceIgnoreCase("&lt;br /&gt;", "[br]");
			text = text.ReplaceIgnoreCase("&quot;", "\"");
			text = text.ReplaceIgnoreCase("&amp;", "&");
			text = text.ReplaceIgnoreCase("<br>", "[br]");
			text = text.ReplaceIgnoreCase("<br/>", "[br]");
			text = text.ReplaceIgnoreCase("<br />", "[br]");
			text = text.ReplaceIgnoreCase("</br>", "");
		}
		text = AppHelper.HtmlEncode(text);
		if (ubbSimple && Settings.Default.IsEnabledUbbForSpot)
		{
			text = text.ReplaceIgnoreCase("[b]", "<b>");
			text = text.ReplaceIgnoreCase("[/b]", "</b>");
			text = text.ReplaceIgnoreCase("[u]", "<u>");
			text = text.ReplaceIgnoreCase("[/u]", "</u>");
			text = text.ReplaceIgnoreCase("[i]", "<i>");
			text = text.ReplaceIgnoreCase("[/i]", "</i>");
		}
		text = text.ReplaceIgnoreCase("[br]", "<br>");
		text = text.ReplaceIgnoreCase("[aa]", "&#");
		if (ubbAdvanced && Settings.Default.IsEnabledUbbForSpot)
		{
			text = new Regex("\\[color=\"?(#?[a-zA-Z0-9]+)\"?\\]", RegexOptions.IgnoreCase).Replace(text, "<font color=\"$1\">");
			text = new Regex("\\[url=(\")?spotnet://(" + EmailPattern + ")\"?\\]", RegexOptions.IgnoreCase).Replace(text, LinkToTheSpotUbbEvaluator);
			text = new Regex("\\[url=\"?(" + LinkPattern + ")\"?\\]", RegexOptions.IgnoreCase).Replace(text, UrlUbbEvaluator);
			text = new Regex("\\[img\\](" + LinkPattern + ")\\[/img\\]", RegexOptions.IgnoreCase).Replace(text, ImgUbbEvaluator);
			text = text.ReplaceIgnoreCase("[/color]", "</font>");
			text = text.ReplaceIgnoreCase("[/url]", "</a>");
		}
		if (text.IndexOf("<br><br><br>", StringComparison.Ordinal) > 250)
		{
			text = text.ReplaceIgnoreCase("<br><br><br>", "<br clear='all'>&nbsp;<br>");
		}
		text = Linkify(text) + "</b></b></b></i></i></i></u></u></u></p></p></p>";
		if (!Settings.Default.IsEnabledSmiles)
		{
			return text;
		}
		return InsertSmileys(text);
	}

	internal static string GenerateSpotUrl(string messageId, string text)
	{
		return $"[url=\"spotnet://{SpotHelper.MakeMsg(messageId, tag: false)}\"]{text}[/url]";
	}

	private static string LinkToTheSpotUbbEvaluator(Match match)
	{
		string value = match.Groups[2].Value;
		return "<A onfocus='this.blur()' TITLE='" + Words.LinkOpen + " " + value + "' HREF=\"spotnet://" + AppHelper.HtmlDecode(value).Replace("\"", "%22").Replace("`", "%60")
			.Replace("'", "%27") + "\">";
	}

	private static string UrlUbbEvaluator(Match match)
	{
		string value = match.Groups[1].Value;
		return "<A onfocus='this.blur()' TITLE='" + Words.LinkOpen + " " + value + "' HREF=\"link:" + AppHelper.SafeHref(AppHelper.HtmlDecode(value)) + "\">";
	}

	private static string ImgUbbEvaluator(Match match)
	{
		string value = match.Groups[1].Value;
		return "<img src=\"" + AppHelper.SafeHref(AppHelper.HtmlDecode(value)) + "\" />";
	}

	internal static string MakeGoogleSearch(string sTitle)
	{
		return "http://www.google.nl/search?q=" + AppHelper.UrlEncode(sTitle);
	}

	private static string MakeNzbSearch(string sTitle)
	{
		return "http://binsearch.info/index.php?q=" + AppHelper.UrlEncode(sTitle) + AppHelper.HtmlEncode("&max=250&adv_age=&server=");
	}

	internal static string InsertSmileys(string sHtml)
	{
		foreach (Match item in Regex.Matches(sHtml, "\\[img=(&quot;)?([a-z]+)(&quot;)?\\]"))
		{
			string value = item.Groups[2].Value;
			string text = LocalFilePrefix + AppHelper.SmileysPath + value + ".gif";
			if (System.IO.File.Exists(text.Replace("file://", "")))
			{
				sHtml = sHtml.Replace(item.Groups[0].Value, "<IMG onfocus='this.blur()' title=\"" + value + "\" SRC=\"" + text + "\">");
			}
		}
		return sHtml;
	}

	internal static string Linkify(string sIn)
	{
		MatchCollection source = LinkRegEx.Matches(sIn);
		List<string> list = new List<string>
		{
			"link:",
			Words.LinkOpen + " ",
			"src=\""
		};
		StringBuilder stringBuilder = new StringBuilder(sIn);
		foreach (Match item in source.Cast<Match>().Reverse())
		{
			bool flag = false;
			int index = item.Index;
			foreach (string item2 in list)
			{
				int length = item2.Length;
				if (index - length >= 0)
				{
					string value = sIn.Substring(index - length, length);
					if (item2.Equals(value))
					{
						flag = true;
					}
				}
			}
			if (!flag)
			{
				stringBuilder.Remove(index, item.Value.Length);
				string value2 = "<A onfocus='this.blur()' TITLE='" + Words.LinkOpen + "' HREF=\"link:" + AppHelper.SafeHref(AppHelper.HtmlDecode(item.Value)) + "\">" + item.Value + "</A>";
				stringBuilder.Insert(index, value2);
			}
		}
		return stringBuilder.ToString();
	}

	internal static string GenerateSpotInfoTableHtml(SpotEx xSpot)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("<TABLE BORDER=0>");
		if (xSpot.OldInfo != null)
		{
			string value = "<A onfocus='this.blur()' TITLE='" + Words.LookingFor + " NZB' HREF='link:" + MakeNzbSearch(AppHelper.HtmlDecode(xSpot.OldInfo.FileName)) + "'>" + AppHelper.HtmlEncode(xSpot.OldInfo.FileName) + "</A>";
			stringBuilder.Append(FormatInfoLine(Words.Filename + "&nbsp;&nbsp;&nbsp;", value));
		}
		int category = xSpot.Category;
		string subCats = xSpot.SubCats;
		string tag = xSpot.Tag;
		string poster = xSpot.Poster;
		UserInfo user = xSpot.User;
		PosterIdentType posterIdent = xSpot.PosterIdent;
		byte b = 0;
		string sColor = "black";
		string sColor2 = "blue";
		string text = null;
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
		Dictionary<string, string> dictionary3 = new Dictionary<string, string>();
		Dictionary<string, string> dictionary4 = new Dictionary<string, string>();
		Dictionary<string, string> dictionary5 = new Dictionary<string, string>();
		Collection collection = new Collection();
		Collection collection2 = new Collection();
		string[] array = Strings.Split(subCats, "|");
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].IsNullOrEmpty())
			{
				continue;
			}
			string text2 = AppHelper.TranslateCat((AppHelper.SpotCategory)category, array[i]);
			if (text2.Length == 0)
			{
				continue;
			}
			string text3 = Strings.Left(array[i], 1).ToLower();
			if (text3.Equals("a"))
			{
				if (!dictionary.ContainsKey(array[i]))
				{
					dictionary.Add(array[i], text2);
				}
			}
			else if (text3.Equals("b"))
			{
				if (!dictionary2.ContainsKey(array[i]))
				{
					dictionary2.Add(array[i], text2);
				}
			}
			else if (text3.Equals("c"))
			{
				if (!dictionary3.ContainsKey(array[i]))
				{
					dictionary3.Add(array[i], text2);
				}
			}
			else if (text3.Equals("d"))
			{
				if (category == 9)
				{
					string a = array[i].ToLower();
					if ((string.Equals(a, "d75", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "d74", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "d73", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "d72", StringComparison.OrdinalIgnoreCase)) && subCats.Contains("d2"))
					{
						continue;
					}
				}
				if (!dictionary4.ContainsKey(array[i]))
				{
					dictionary4.Add(array[i], text2);
				}
			}
			else if (text3.Equals("z") && !dictionary5.ContainsKey(array[i]))
			{
				b = (byte)Math.Round(Conversion.Val(array[i].ToLower().Replace("z", "")) + 1.0);
				dictionary5.Add(array[i], text2);
			}
		}
		string text4 = AppHelper.CatDesc(category, b);
		string value2 = CreateClassLink("query:" + AppHelper.UrlEncode(text4 + "_cat = " + category.ToStringSafely()), text4, "category", Words.Search);
		string value3 = FormatInfoLine(Categories.Category, value2);
		string text5 = MakeCats(category, 'z', dictionary5, sColor, sColor2);
		string value4 = MakeCats(category, 'a', dictionary, sColor, sColor2);
		string value5 = MakeCats(category, 'b', dictionary2, sColor, sColor2);
		string value6 = MakeCats(category, 'c', dictionary3, sColor, sColor2);
		string value7 = MakeCats(category, 'd', dictionary4, sColor, sColor2);
		switch (category)
		{
		case 2:
			if (b > 1)
			{
				value3 = text5;
			}
			stringBuilder.Append(value3);
			stringBuilder.Append(value4);
			stringBuilder.Append(value5);
			stringBuilder.Append(value6);
			stringBuilder.Append(value7);
			break;
		case 3:
			stringBuilder.Append(value3);
			stringBuilder.Append(value5);
			stringBuilder.Append(value4);
			stringBuilder.Append(value6);
			break;
		case 4:
			stringBuilder.Append(value3);
			stringBuilder.Append(value4);
			stringBuilder.Append(value5);
			break;
		default:
			stringBuilder.Append(value3);
			stringBuilder.Append(value4);
			stringBuilder.Append(value5);
			stringBuilder.Append(value6);
			stringBuilder.Append(value7);
			break;
		}
		string[] array2 = Strings.Split(tag);
		for (int j = 0; j <= array2.GetUpperBound(0); j++)
		{
			if (!array2[j].Trim().IsNullOrEmpty())
			{
				collection.Add(array2[j]);
			}
		}
		string expression = "";
		if (xSpot.OldInfo != null)
		{
			expression = xSpot.OldInfo.Groups;
		}
		string[] array3 = Strings.Split(expression, "|");
		int upperBound = array3.GetUpperBound(0);
		for (int k = 0; k <= upperBound; k++)
		{
			if (!array3[k].Trim().IsNullOrEmpty())
			{
				collection2.Add(array3[k]);
			}
		}
		if (collection.Count > 0)
		{
			string text6 = null;
			text = "<TR><TD align=\"right\" style=\"padding-right:15px;word-wrap:normal;\"><b>Tag" + ((collection.Count > 1) ? "s" : "") + "</b></TD><TD>";
			foreach (object item in collection)
			{
				string text7 = item.ToStringSafely();
				if (!text7.EqualsIgnoreCase(poster))
				{
					text = text + text6 + CreateClassLink("query:" + AppHelper.UrlEncode(AppHelper.StripNonAlphaNumericCharacters(text7) + "_tag MATCH '" + AppHelper.StripNonAlphaNumericCharacters(text7).ToLower() + "'"), AppHelper.StripNonAlphaNumericCharacters(text7), "category", Words.Search) + "</TD></TR>";
					text6 = "<TR><TD>&nbsp;</TD><TD>";
				}
			}
			if (text6.IsNullOrEmpty())
			{
				text = null;
			}
		}
		if (collection2.Count > 0)
		{
			string text8 = null;
			expression = "<TR><TD align=\"right\" style=\"padding-right:15px;word-wrap:normal;\"><b>" + ((collection2.Count > 1) ? Words.Newsgroups : Words.Newsgroup) + "</b></TD><TD>";
			foreach (object item2 in collection2)
			{
				string text9 = item2.ToStringSafely();
				if (!text9.EqualsIgnoreCase(Words.Other))
				{
					expression = expression + text8 + AppHelper.HtmlEncode(text9) + "</TD></TR>";
					text8 = "<TR><TD align=\"right\" style=\"padding-right:15px;word-wrap:normal;\">&nbsp;</TD><TD>";
				}
			}
			if (text8.IsNullOrEmpty())
			{
				expression = null;
			}
		}
		else
		{
			expression = null;
		}
		if (xSpot.OldInfo == null)
		{
			expression += FormatInfoLine(Words.ColumnSize, AppHelper.ConvertSize(xSpot.Filesize));
		}
		string text10 = xSpot.Web;
		if (text10.IsNullOrWhiteSpace())
		{
			text10 = MakeGoogleSearch(xSpot.Title);
		}
		string text11 = AppHelper.SafeHref(text10);
		string text12 = AppHelper.HtmlEncode(HttpUtility.UrlDecode(text10));
		string value8 = "<A onfocus='this.blur()' TITLE='" + Words.LinkOpen + "' HREF=\"link:" + text11 + "\">" + text12 + "</A>";
		expression += FormatInfoLine(Words.Website, value8);
		stringBuilder.Append(expression);
		string arg = $"<span id='PosterIdentLinks'>{GeneratePosterLinksHtmlCode(poster, user, posterIdent)}</span>";
		string arg2 = $"<span id='PosterIdentLabel'>{GeneratePosterIdentLabelHtmlCode(posterIdent)}</span>";
		string value9 = string.Format("{1} {0}", arg, arg2);
		string value10 = FormatInfoLine(Words.Sender, value9);
		stringBuilder.Append(value10);
		stringBuilder.Append(text);
		if (Settings.Default.SpotDetailsShowNewsreader && !xSpot.Newsreader.IsNullOrEmpty())
		{
			stringBuilder.Append(FormatInfoLine(Words.PostedWith, AppHelper.HtmlEncode(xSpot.Newsreader)));
		}
		stringBuilder.Append(GenerateNumberOfSpamReportsHtmlTableLine(xSpot));
		stringBuilder.Append("</TABLE>");
		return stringBuilder.ToStringSafely();
	}

	private static string GenerateNumberOfSpamReportsHtmlTableLine(Spot xSpot)
	{
		string text = "";
		if (xSpot.NumberOfSpamReports == 0)
		{
			text += xSpot.NumberOfSpamReports;
		}
		else
		{
			if (xSpot.NumberOfSpamReports > 1)
			{
				text = $"<b style=\"color:red\">{xSpot.NumberOfSpamReports}</b>";
			}
			else if (xSpot.NumberOfSpamReports > 0)
			{
				text = $"<b style=\"color:orange\">{xSpot.NumberOfSpamReports}</b>";
			}
			text = $"<a href='spamreports:{xSpot.MessageId}'>{text}</a>";
		}
		return FormatInfoLine(Words.NumberOfSpamReports, text);
	}

	private static string FormatInfoLine(string name, string value)
	{
		return string.Format("<TR><TD align=\"right\" style=\"{0}\"><b>{1}</b></TD><TD>{2}</TD></TR>", "padding-right:15px;word-wrap:normal;", name, value);
	}

	internal static string ParseComment(Comment xComment, string sClass, string sTooltip, bool isPreview = false)
	{
		if (_commentHtml == null)
		{
			_commentHtml = new StreamReader(AppHelper.GetCommentThemeFile()).ReadToEnd().Trim();
		}
		string text = _commentHtml;
		if (isPreview && text.Trim().EndsWith("<hr>"))
		{
			text = text.Substring(0, text.Length - 4);
		}
		string newValue = ((xComment.User.Modulus.Equals(UserKeyHelper.GetModulus()) || isPreview || sClass.Equals("trusted")) ? "none" : "true");
		string text2 = LocalFilePrefix + AppHelper.SettingsFolder.Replace("\\", "/").Replace("\"", "\"\"");
		string newValue2 = System.IO.Path.Combine(text2, "TabThemes/" + Settings.Default.ActiveTheme).Replace("\\", "/");
		string text3 = SpotHelper.MakeMsg(xComment.MessageId, tag: false).Split('@')[0];
		string newValue3 = AppHelper.HtmlEncode(string.Format(xComment.From));
		string newValue4 = (xComment.User.ValidSignature ? AppHelper.MakeUnique(xComment.User.Modulus) : Words.Unknown);
		return text.Replace("[SN:AVATAR]", GetAvatar(xComment.User.Avatar, xComment.User.Modulus)).Replace("[SN:TOOLTIP]", sTooltip).Replace("[SN:STAMP]", (xComment.Created - AppHelper.Epoch).TotalSeconds.ToStringSafely())
			.Replace("[SN:DATE]", xComment.Created.ToLocalTime().ToString("%d MMM yyyy HH:mm"))
			.Replace("[SN:MODULUS]", xComment.User.Modulus)
			.Replace("[SN:SERVERMODULUS]", "null")
			.Replace("[SN:FROM]", newValue3)
			.Replace("[SN:UNIQUE]", newValue4)
			.Replace("[SN:PATH]", text2)
			.Replace("[SN:THEME]", newValue2)
			.Replace("[SN:CLASS]", sClass)
			.Replace("[SN:CLASSBODY]", "")
			.Replace("[SN:ARTICLE]", "c" + text3)
			.Replace("[SN:COMMENTID]", "c" + text3)
			.Replace("[SN:BLACKLIST]", "")
			.Replace("[SN:REPLYWORD]", Words.Reply)
			.Replace("[SN:QUOTEWORD]", Words.Quote)
			.Replace("[SN:ADDTOBLACKVISIBILE]", newValue)
			.Replace("[SN:ADDTOBLACKLISTWORD]", Words.BlackListAddTo)
			.Replace("[SN:WHITELIST]", (!isPreview && !BlackAndWhite.WhiteList().Contains(xComment.User.Modulus)) ? "False" : "True")
			.Replace("[SN:DESC]", MakeCommentBody(xComment.Body, "d" + text3));
	}

	internal static void ResetThemeFiles()
	{
		_spotHtm = null;
		_commentHtml = null;
	}

	internal static string ParseSpot(SpotEx spotEx, byte fontSize)
	{
		if (_spotHtm == null)
		{
			_spotHtm = new StreamReader(AppHelper.GetSpotThemeFile()).ReadToEnd();
		}
		CultureInfo culture = UserLanguageHelper.Culture;
		string newValue;
		try
		{
			UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("nl");
			newValue = AppHelper.HtmlEncode(AppHelper.CatDesc(spotEx.Category, 0));
			if (spotEx.Category == 1 && (spotEx.SubCat == 12 || spotEx.SubCat == 13))
			{
				newValue = AppHelper.HtmlEncode(AppHelper.CatDesc(spotEx.Category, 5));
			}
		}
		finally
		{
			UserLanguageHelper.Culture = culture;
		}
		string text = LocalFilePrefix + AppHelper.SettingsFolder.Replace("\\", "/").Replace("\"", "\"\"");
		string newValue2 = System.IO.Path.Combine(System.IO.Path.Combine(text, "TabThemes/"), Settings.Default.ActiveTheme);
		string text2 = AppHelper.HtmlEncode(spotEx.Title).Replace("[SN:", "[SN:]");
		string text3 = (spotEx.Web.IsNullOrWhiteSpace() ? MakeGoogleSearch(spotEx.Title) : spotEx.Web);
		string text4 = _spotHtm.Replace("[SN:NICK]", AppHelper.StripNonAlphaNumericCharacters(Settings.Default.Nickname)).Replace("[SN:AVATAR]", GetAvatar(spotEx.User.Avatar, spotEx.User.Modulus)).Replace("[SN:FONTSIZE]", fontSize.ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:FONTSIZESMALL]", (fontSize - 2).ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:FONTEM]", (fontSize - 2).ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:FILESIZE]", spotEx.Filesize.ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:STAMP]", spotEx.Stamp.ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:MODULUS]", spotEx.User.Modulus)
			.Replace("[SN:CAT]", newValue)
			.Replace("[SN:PATH]", text)
			.Replace("[SN:THEME]", newValue2)
			.Replace("[SN:TAG]", AppHelper.HtmlEncode(AppHelper.StripNonAlphaNumericCharacters(spotEx.Tag)))
			.Replace("[SN:FROM]", AppHelper.HtmlEncode(AppHelper.StripNonAlphaNumericCharacters(spotEx.Poster)))
			.Replace("[SN:UNIQUE]", spotEx.User.ValidSignature ? AppHelper.MakeUnique(spotEx.User.Modulus) : Words.Unknown)
			.Replace("[SN:WHITELIST]", (spotEx.User.ValidSignature & BlackAndWhite.WhiteList().Contains(spotEx.User.Modulus)) ? "True" : "False")
			.Replace("[SN:WEB]", AppHelper.SafeHref(text3).Replace("[SN:", "[SN:]"))
			.Replace("[SN:SUBCATS]", AppHelper.HtmlEncode(spotEx.SubCats))
			.Replace("[SN:TITLE]", text2)
			.Replace("[SN:STATS]", "")
			.Replace("[SN:IMGX]", spotEx.ImageWidth.ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:IMGY]", spotEx.ImageHeight.ToString(CultureInfo.InvariantCulture))
			.Replace("[SN:INFO]", GenerateSpotInfoTableHtml(spotEx))
			.Replace("[SN:BGCOLOR]", "#F9F9F9")
			.Replace("[SN:DESC]", MakeDesc(spotEx.Body.Trim(), spotEx.OldInfo != null))
			.Replace("[SN:SMILEYS]", AppHelper.SmileysPath)
			.Replace("[SN:SPOTLINK]", GenerateSpotUrl(spotEx.MessageId, text2))
			.Replace("[WORD:DOWNLOAD]", Words.SpotThemeDownload)
			.Replace("[WORD:LOADINGCOMMENTS]", Words.SpotThemeLoadingComments)
			.Replace("[WORD:NAME]", Words.SpotThemeName)
			.Replace("[WORD:PREVIEW]", Words.SpotThemePreview)
			.Replace("[WORD:SMILEYS]", Words.SpotThemeSmileys)
			.Replace("[WORD:COMMENT]", Words.SpotThemeComment)
			.Replace("[WORD:BOLD]", Words.Bold)
			.Replace("[WORD:ITALIC]", Words.Italic)
			.Replace("[WORD:UNDERLINE]", Words.Underline)
			.Replace("[WORD:LINKTOSPOT]", Words.LinkToASpot)
			.Replace("[WORD:REPLY]", Words.SpotThemeReply)
			.Replace("[WORD:CHARSLEFT]", Words.CharsLeft)
			.Replace("[WORD:IMDBFILMNOTFOUND]", Words.ImdbFilmNotFound)
			.Replace("[WORD:IMDBALBUMNOTFOUND]", Words.ImdbAlbumNotFound)
			.Replace("[WORD:ERROR]", Words.Error)
			.Replace("[WORD:ARTIST]", Words.Artist)
			.Replace("[WORD:TRACKS]", Words.Tracks)
			.Replace("[WORD:OPENCLOSETRACKS]", Words.OpenCloseiTunesTracks)
			.Replace("[WORD:PIECES]", Words.Pieces)
			.Replace("[WORD:EXTERNALLINKS]", Words.ExternalLinks);
		if (Settings.Default.ActiveTheme.Equals("Default"))
		{
			text4 = text4.Replace("=".Repeat(60), "=".Repeat(50)).Replace("-".Repeat(60), "-".Repeat(50));
		}
		if (ThemeHelper.IsModernDark)
		{
			// The tab templates paint their panels through ID selectors (#part-one,
			// #part-two, ...). Those outrank the element selectors below, so overriding
			// text colour globally without also overriding those panels left light text
			// on a white background - the panels have to be named explicitly.
			// body carries a bgcolor attribute too, which CSS on the same element wins.
			string darkCss = @"<style type='text/css' id='spotnet-modern-dark-css'>
html, body {
    background-color: #0d1520 !important;
    background-image: none !important;
    color: #e2e8f0 !important;
}
#part-button, #part-one, #part-one-centered, #part-two, #part-three, #part-four, #part-five,
#part-six, #ImdbPanel, #ImdbPanel2, #wrapper, #wrapper-one, #wrapper-two {
    background: #131d28 none !important;
    background-color: #131d28 !important;
    border-color: #1e293b !important;
    color: #e2e8f0 !important;
}
#part-two a, #ImdbPanel a, #ImdbPanel2 a, #part-five a, #part-three a, #part-four a {
    color: #38bdf8 !important;
}
.header a {
    background: #131d28 !important;
    color: #38bdf8 !important;
}
table, tr, td, th, div, span, p, label, b, strong, i, em {
    color: #e2e8f0 !important;
}
.header {
    background-color: #0d1520 !important;
    color: #f8fafc !important;
    border-bottom: 1px solid #1e293b !important;
}
a, a:link, a:visited, .from, .category, .website {
    color: #38bdf8 !important;
}
a:hover, .from:hover, .category:hover {
    color: #7dd3fc !important;
}
.date {
    color: #94a3b8 !important;
}
.reply {
    color: #60a5fa !important;
}
.comment {
    color: #93c5fd !important;
}
.author {
    color: #f43f5e !important;
}
.trusted {
    color: #4ade80 !important;
}
.untrusted {
    color: #64748b !important;
}
blockquote {
    background-color: #152232 !important;
    color: #cbd5e1 !important;
    border-top: 1px solid #1e293b !important;
    border-bottom: 1px solid #1e293b !important;
    border-left: 4px solid #0284c7 !important;
}
cite {
    color: #94a3b8 !important;
}
textarea, input[type='text'], input[type='password'], select {
    background-color: #152232 !important;
    color: #f8fafc !important;
    border: 1px solid #334155 !important;
}
button, .button, input[type='button'], input[type='submit'], .btn, .btn-primary {
    background-color: #0284c7 !important;
    color: #ffffff !important;
    border: 1px solid #0369a1 !important;
    border-radius: 3px !important;
    cursor: pointer !important;
}
button:hover, .button:hover, input[type='button']:hover, input[type='submit']:hover {
    background-color: #0369a1 !important;
}
#div_comments, .comments, .comment_box {
    background-color: #0d1520 !important;
    border-color: #1e293b !important;
}
hr {
    border-color: #1e293b !important;
    background-color: #1e293b !important;
}
#SpotImage {
    border: 1px solid #1e293b !important;
}
.bbcode_quote {
    background-color: #152232 !important;
    border-left: 3px solid #0284c7 !important;
}
</style>";
			if (text4.Contains("</head>"))
			{
				text4 = text4.Replace("</head>", darkCss + "</head>");
			}
			else
			{
				text4 = darkCss + text4;
			}
		}
		return text4;
	}
}
