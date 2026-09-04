using System;
using System.Globalization;
using Spotnet.Extensions;

namespace Spotnet.Model;

/// <summary>
/// Finds where in a newsgroup's article numbering a given date starts.
/// </summary>
public static class ArticleWatermark
{
	public readonly struct ArticleStamp
	{
		public ArticleStamp(long article, DateTime postedUtc)
		{
			Article = article;
			PostedUtc = postedUtc;
		}

		public long Article { get; }
		public DateTime PostedUtc { get; }
	}

	public delegate ArticleStamp? ProbeRange(long from, long to);

	public const long Undetermined = -1L;
	private const long ProbeWindow = 200L;

	public static long FindFirstArticleOnOrAfter(long first, long last, DateTime cutoffUtc, ProbeRange probe, int maxProbes = 32)
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
		return anyDateRead ? last : Undetermined;
	}

	public static ArticleStamp? FirstStampIn(string? overviewResponse)
	{
		if (string.IsNullOrEmpty(overviewResponse))
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

	public static DateTime? ParseOverviewDate(string? date)
	{
		if (string.IsNullOrWhiteSpace(date))
		{
			return null;
		}
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
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime plain))
		{
			return plain;
		}
		return null;
	}
}
