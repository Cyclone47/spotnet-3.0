using System;
using System.Globalization;
using Spotnet.Extensions;

namespace Spotnet.Model;

/// <summary>
/// Finds where in a newsgroup's article numbering a given date starts.
/// </summary>
/// <remarks>
/// A first synchronisation used to begin at the group's lowest article number, which on
/// alt.binaries.ph is fifteen years back. Almost all of those headers are then thrown away
/// again - out of retention, or simply older than the user asked for - after being pulled
/// over the wire. Article numbers rise monotonically with posting time, so the start point
/// can be found with a handful of one-article XOVER probes instead.
///
/// The search is kept free of the network so it can be tested: the caller supplies the
/// probe. Everything here works in UTC.
/// </remarks>
internal static class ArticleWatermark
{
	/// <summary>An article number together with when it was posted.</summary>
	internal readonly struct ArticleStamp
	{
		internal ArticleStamp(long article, DateTime postedUtc)
		{
			Article = article;
			PostedUtc = postedUtc;
		}

		internal long Article { get; }

		internal DateTime PostedUtc { get; }
	}

	/// <summary>
	/// Reads the first article in <c>[from, to]</c>, or null when the range holds none.
	/// </summary>
	internal delegate ArticleStamp? ProbeRange(long from, long to);

	/// <summary>Returned when the group's dates could not be read at all.</summary>
	internal const long Undetermined = -1L;

	/// <summary>
	/// Articles asked for per probe. Cancelled and expired articles leave holes in the
	/// numbering, so a probe covers a window rather than a single number.
	/// </summary>
	private const long ProbeWindow = 200L;

	/// <summary>
	/// The lowest article number posted at or after <paramref name="cutoffUtc"/>.
	/// </summary>
	/// <returns>
	/// An article number in <c>[first, last]</c>; <paramref name="last"/> when the whole
	/// group predates the cutoff; or <see cref="Undetermined"/> when no probe returned a
	/// readable date, in which case the caller should keep its own range.
	/// </returns>
	internal static long FindFirstArticleOnOrAfter(long first, long last, DateTime cutoffUtc, ProbeRange probe, int maxProbes = 32)
	{
		if (probe == null || first < 0 || last < first)
		{
			return Undetermined;
		}
		long lo = first;
		long hi = last;
		long answer = Undetermined;
		bool anyDateRead = false;
		for (int i = 0; i < maxProbes && lo <= hi; i++)
		{
			long mid = lo + (hi - lo) / 2;
			long windowEnd = Math.Min(hi, mid + ProbeWindow - 1);
			ArticleStamp? found = probe(mid, windowEnd);
			if (!found.HasValue)
			{
				// A hole in the numbering, or a server that answered nothing. Either way
				// there is nothing to compare, so continue above the window.
				lo = windowEnd + 1;
				continue;
			}
			anyDateRead = true;
			long article = found.Value.Article;
			if (found.Value.PostedUtc < cutoffUtc)
			{
				lo = Math.Max(lo + 1, article + 1);
			}
			else
			{
				answer = article;
				hi = Math.Min(hi - 1, article - 1);
			}
		}
		if (answer != Undetermined)
		{
			return answer;
		}
		// Every date that was read is older than the cutoff: the group holds nothing the
		// user asked for, so start at the very end and let the normal sync take it from
		// there.
		return anyDateRead ? last : Undetermined;
	}

	/// <summary>
	/// Reads the first article number and posting date out of an XOVER response.
	/// </summary>
	/// <remarks>
	/// The overview format is tab separated, article number first and the Date header
	/// fourth. Status and terminator lines do not start with a number, so they fall out on
	/// their own. A line whose date is unreadable is skipped rather than failing the probe:
	/// Usenet carries plenty of malformed Date headers, and one of them should not push the
	/// whole search off course.
	/// </remarks>
	internal static ArticleStamp? FirstStampIn(string overviewResponse)
	{
		if (overviewResponse.IsNullOrEmpty())
		{
			return null;
		}
		foreach (string rawLine in overviewResponse.Split('\n'))
		{
			string line = rawLine.TrimEnd('\r');
			if (line.Length == 0)
			{
				continue;
			}
			string[] fields = line.Split('\t');
			if (fields.Length < 4 || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long article) || article <= 0)
			{
				continue;
			}
			DateTime? posted = ParseOverviewDate(fields[3]);
			if (posted.HasValue)
			{
				return new ArticleStamp(article, posted.Value);
			}
		}
		return null;
	}

	/// <summary>Parses an RFC 5322 Date header into UTC, or null when it is unreadable.</summary>
	internal static DateTime? ParseOverviewDate(string date)
	{
		if (date.IsNullOrWhiteSpace())
		{
			return null;
		}
		// Trailing "(UTC)" style comments are legal in a Date header and defeat the parser.
		string text = date.Trim();
		int comment = text.IndexOf('(');
		if (comment > 0)
		{
			text = text.Substring(0, comment).Trim();
		}
		if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed))
		{
			return parsed.UtcDateTime;
		}
		// No offset, or one the parser does not recognise: read it as UTC rather than
		// dropping the line. A few hours either way cannot move the answer by a day.
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime plain))
		{
			return plain;
		}
		return null;
	}
}
