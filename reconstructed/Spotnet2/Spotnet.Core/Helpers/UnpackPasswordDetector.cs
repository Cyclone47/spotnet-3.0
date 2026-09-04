using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

/// <summary>
/// Recovers the archive password a download carries with it, so unpacking does not stop
/// on a prompt the poster already answered.
/// </summary>
public static class UnpackPasswordDetector
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const long MaxNzbBytesToScan = 128L * 1024 * 1024;
	private const int MaxPasswordLength = 128;

	private static readonly XmlReaderSettings SafeReaderSettings = new()
	{
		DtdProcessing = DtdProcessing.Ignore,
		XmlResolver = null
	};

	private static readonly Regex BbCodeTag = new(
		@"\[/?[a-zA-Z][a-zA-Z0-9*]*(?:=[^\]]*)?\]", RegexOptions.Compiled);

	private static readonly Regex HtmlTag = new("<[^>]{1,200}>", RegexOptions.Compiled);

	private static readonly Regex LabelledPassword = new(
		@"(?<![\p{L}\p{N}])(?:wachtwoord|paswoord|passwoord|password|passwort|passwd|pass|pwd)"
		+ @"[ \t]*[:=][ \t]*(?:\r?\n[ \t]*)?"
		+ @"(?:""(?<value>[^""\r\n]{1,128})""|'(?<value>[^'\r\n]{1,128})'|(?<value>[^\s]{1,128})(?![^\s]))",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly string[] NotAPassword =
	{
		"geen", "geen.", "none", "no", "nee", "n/a", "na", "nvt", "n.v.t.", "n.v.t", "-", "--",
		"unknown", "onbekend", "niet", "nvtb", "?", "x"
	};

	private static readonly char[] TrailingNoise = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '*', '<', '>' };
	private static readonly char[] LeadingNoise = { '(', '[', '{', '"', '\'', '*', '<', '>' };

	public static string? FromNzbFile(string? nzbPath)
	{
		if (string.IsNullOrWhiteSpace(nzbPath) || !File.Exists(nzbPath))
		{
			return null;
		}
		try
		{
			if (new FileInfo(nzbPath).Length > MaxNzbBytesToScan)
			{
				return null;
			}
			using FileStream stream = File.OpenRead(nzbPath);
			return FromNzbStream(stream);
		}
		catch (Exception ex)
		{
			Log.Debug("Could not read NZB metadata from {0}: {1}", nzbPath, ex.Message);
			return null;
		}
	}

	public static string? FromNzbStream(Stream? nzb)
	{
		if (nzb == null)
		{
			return null;
		}
		try
		{
			using XmlReader reader = XmlReader.Create(nzb, SafeReaderSettings);
			return FromNzbReader(reader);
		}
		catch (XmlException ex)
		{
			Log.Debug("NZB metadata is not readable: {0}", ex.Message);
			return null;
		}
	}

	public static string? FromNzbText(string? nzbXml)
	{
		if (string.IsNullOrWhiteSpace(nzbXml))
		{
			return null;
		}
		try
		{
			using XmlReader reader = XmlReader.Create(new StringReader(nzbXml), SafeReaderSettings);
			return FromNzbReader(reader);
		}
		catch (XmlException ex)
		{
			Log.Debug("NZB metadata is not readable: {0}", ex.Message);
			return null;
		}
	}

	private static string? FromNzbReader(XmlReader reader)
	{
		while (reader.Read())
		{
			if (reader.NodeType != XmlNodeType.Element)
			{
				continue;
			}
			if (reader.LocalName.EqualsIgnoreCase("file"))
			{
				return null;
			}
			if (!reader.LocalName.EqualsIgnoreCase("meta"))
			{
				continue;
			}
			string? type = reader.GetAttribute("type");
			if (type == null || !type.Trim().EqualsIgnoreCase("password"))
			{
				continue;
			}
			string? value = reader.GetAttribute("value");
			if (string.IsNullOrWhiteSpace(value) && !reader.IsEmptyElement)
			{
				value = reader.ReadElementContentAsString();
			}
			string? cleaned = Clean(value);
			if (cleaned != null)
			{
				return cleaned;
			}
		}
		return null;
	}

	public static string? FromDescription(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string plain = StripMarkup(text);
		foreach (Match match in LabelledPassword.Matches(plain))
		{
			string? candidate = Clean(match.Groups["value"].Value);
			if (candidate != null)
			{
				return candidate;
			}
		}
		return null;
	}

	public static string? Detect(string? nzbPath, params string?[]? descriptions)
	{
		string? fromNzb = FromNzbFile(nzbPath);
		if (fromNzb != null)
		{
			return fromNzb;
		}
		if (descriptions == null)
		{
			return null;
		}
		foreach (string? description in descriptions)
		{
			string? found = FromDescription(description);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static string StripMarkup(string text)
	{
		string withoutTags = HtmlTag.Replace(BbCodeTag.Replace(text, " "), " ");
		string decoded = WebUtility.HtmlDecode(withoutTags);
		return decoded.Replace(' ', ' ');
	}

	private static string? Clean(string? value)
	{
		if (value == null)
		{
			return null;
		}
		string trimmed = value.Trim().Trim(LeadingNoise).TrimEnd(TrailingNoise).Trim();
		if (trimmed.Length == 0 || trimmed.Length > MaxPasswordLength)
		{
			return null;
		}
		foreach (string word in NotAPassword)
		{
			if (trimmed.EqualsIgnoreCase(word))
			{
				return null;
			}
		}
		bool hasContent = false;
		foreach (char c in trimmed)
		{
			if (char.IsLetterOrDigit(c))
			{
				hasContent = true;
				break;
			}
		}
		return hasContent ? trimmed : null;
	}
}
