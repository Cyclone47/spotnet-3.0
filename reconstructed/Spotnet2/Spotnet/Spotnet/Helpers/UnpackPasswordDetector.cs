using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using NLog;
using Spotnet.Extensions;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Helpers;

/// <summary>
/// Recovers the archive password a download carries with it, so unpacking does not stop
/// on a prompt the poster already answered.
/// </summary>
/// <remarks>
/// Two places carry it. An NZB may declare it in the head section that the newzbin DTD
/// defines - <c>&lt;meta type="password"&gt;secret&lt;/meta&gt;</c>, and in the wild also
/// <c>&lt;meta type="password" value="secret"/&gt;</c>. Otherwise the poster writes it into
/// the spot title or body, in a handful of conventional shapes.
///
/// Nothing here ever guesses: a value is returned only when the text actually labels it as
/// the password. The manual dialog stays the fallback, and a password the user typed
/// himself is never overwritten - see <see cref="Downloader.PostProcessing.Unpack"/>.
/// </remarks>
public static class UnpackPasswordDetector
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	/// <summary>An NZB large enough to be a header dump is not read for metadata.</summary>
	private const long MaxNzbBytesToScan = 128L * 1024 * 1024;

	/// <summary>Longer than any real archive password; a match this long is prose.</summary>
	private const int MaxPasswordLength = 128;

	/// <summary>BBCode the themes wrap a label in: <c>[b]</c>, <c>[color=red]</c>.</summary>
	private static readonly Regex BbCodeTag = new Regex(
		@"\[/?[a-zA-Z][a-zA-Z0-9*]*(?:=[^\]]*)?\]", RegexOptions.Compiled);

	private static readonly Regex HtmlTag = new Regex("<[^>]{1,200}>", RegexOptions.Compiled);

	/// <summary>
	/// A password label, then the value. The value may be quoted, and may sit on the line
	/// below the label - which is how the label-on-its-own-line style posts read.
	/// </summary>
	private static readonly Regex LabelledPassword = new Regex(
		@"(?<![\p{L}\p{N}])(?:wachtwoord|paswoord|passwoord|password|passwort|passwd|pass|pwd)"
		+ @"[ \t]*[:=][ \t]*(?:\r?\n[ \t]*)?"
		// The unquoted form has to match the whole token. Without the lookahead a 200
		// character blob matched its first 128 characters and became a "password".
		+ @"(?:""(?<value>[^""\r\n]{1,128})""|'(?<value>[^'\r\n]{1,128})'|(?<value>[^\s]{1,128})(?![^\s]))",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>
	/// Words that follow the label when the poster is saying there is no password. Taking
	/// one of these literally would send unrar a password for an unprotected archive.
	/// </summary>
	private static readonly string[] NotAPassword =
	{
		"geen", "geen.", "none", "no", "nee", "n/a", "na", "nvt", "n.v.t.", "-", "--",
		"unknown", "onbekend", "niet", "nvtb", "?", "x"
	};

	/// <summary>Punctuation a password picks up from the sentence around it.</summary>
	private static readonly char[] TrailingNoise = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '*', '<', '>' };

	private static readonly char[] LeadingNoise = { '(', '[', '{', '"', '\'', '*', '<', '>' };

	/// <summary>
	/// The password an NZB file declares, or null when it declares none.
	/// </summary>
	/// <remarks>Never throws: a download must not fail because its NZB is odd.</remarks>
	public static string FromNzbFile(string nzbPath)
	{
		if (nzbPath.IsNullOrWhiteSpace() || !File.Exists(nzbPath))
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
			Log.Debug("Could not read NZB metadata from " + nzbPath + ": " + ex.Message);
			return null;
		}
	}

	/// <summary>The password an NZB document declares, or null.</summary>
	public static string FromNzbStream(Stream nzb)
	{
		if (nzb == null)
		{
			return null;
		}
		try
		{
			using XmlReader reader = XmlReader.Create(nzb, Module.ReaderSettings);
			return FromNzbReader(reader);
		}
		catch (XmlException ex)
		{
			Log.Debug("NZB metadata is not readable: " + ex.Message);
			return null;
		}
	}

	/// <summary>The password an NZB document declares, or null. Takes the XML as text.</summary>
	public static string FromNzbText(string nzbXml)
	{
		if (nzbXml.IsNullOrWhiteSpace())
		{
			return null;
		}
		try
		{
			using XmlReader reader = XmlReader.Create(new StringReader(nzbXml), Module.ReaderSettings);
			return FromNzbReader(reader);
		}
		catch (XmlException ex)
		{
			Log.Debug("NZB metadata is not readable: " + ex.Message);
			return null;
		}
	}

	/// <remarks>
	/// Matches on the local name, so an NZB carrying the newzbin namespace and one carrying
	/// no namespace at all are both read. Reading stops at the first file element: the head
	/// section precedes them, and the rest of the document can be hundreds of megabytes.
	/// </remarks>
	private static string FromNzbReader(XmlReader reader)
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
			string type = reader.GetAttribute("type");
			if (type == null || !type.Trim().EqualsIgnoreCase("password"))
			{
				continue;
			}
			// The DTD puts the value in the element body; several posting tools write a
			// value attribute instead, so both are accepted.
			string value = reader.GetAttribute("value");
			if (value.IsNullOrWhiteSpace() && !reader.IsEmptyElement)
			{
				value = reader.ReadElementContentAsString();
			}
			string cleaned = Clean(value);
			if (cleaned != null)
			{
				return cleaned;
			}
		}
		return null;
	}

	/// <summary>
	/// The password a spot title or body labels, or null when it labels none.
	/// </summary>
	/// <remarks>
	/// The text is a Usenet posting: BBCode, HTML or both. Tags are stripped first so
	/// <c>[b]Wachtwoord:[/b] secret</c> and <c>&lt;b&gt;Password:&lt;/b&gt; secret</c> read
	/// the same as the plain form.
	/// </remarks>
	public static string FromDescription(string text)
	{
		if (text.IsNullOrWhiteSpace())
		{
			return null;
		}
		string plain = StripMarkup(text);
		foreach (Match match in LabelledPassword.Matches(plain))
		{
			string candidate = Clean(match.Groups["value"].Value);
			if (candidate != null)
			{
				return candidate;
			}
		}
		return null;
	}

	/// <summary>
	/// The first password any of the sources names, preferring the NZB's own declaration
	/// over anything read out of prose.
	/// </summary>
	public static string Detect(string nzbPath, params string[] descriptions)
	{
		string fromNzb = FromNzbFile(nzbPath);
		if (fromNzb != null)
		{
			return fromNzb;
		}
		if (descriptions == null)
		{
			return null;
		}
		foreach (string description in descriptions)
		{
			string found = FromDescription(description);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	/// <summary>Removes BBCode and HTML tags, and resolves HTML entities.</summary>
	private static string StripMarkup(string text)
	{
		// Entities are resolved after the tags are gone, so a &lt;b&gt; written as text
		// cannot turn into a tag that then eats the password after it.
		string withoutTags = HtmlTag.Replace(BbCodeTag.Replace(text, " "), " ");
		string decoded = HttpUtility.HtmlDecode(withoutTags);
		// Non-breaking spaces come out of HTML-formatted bodies and are not \s.
		return decoded.Replace(' ', ' ');
	}

	/// <summary>
	/// Trims a candidate down to the password itself, or returns null when what is left is
	/// not one.
	/// </summary>
	private static string Clean(string value)
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
		// A run of punctuation is decoration left over from the layout, not a password.
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
