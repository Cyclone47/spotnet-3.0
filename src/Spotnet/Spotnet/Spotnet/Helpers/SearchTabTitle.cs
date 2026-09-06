using System;
using System.Collections.Generic;
using System.Globalization;
using Spotnet.Properties;

namespace Spotnet.Helpers;

/// <summary>
/// While a search is showing, the first tab is titled "<c>Search: term</c>" and several
/// places read that title back to decide whether the client is searching and for what.
///
/// The label is translated, so recognising it has to accept every supported language:
/// switching language mid-search leaves a title written in the previous one, and a
/// current-language-only check would quietly decide the search had ended.
/// </summary>
internal static class SearchTabTitle
{
	/// <summary>The prefix a new search title is written with.</summary>
	public static string Prefix => Words.Search + ": ";

	public static bool Matches(string title)
	{
		return TermOf(title) != null;
	}

	/// <summary>The searched-for text, or null when this is not a search title.</summary>
	public static string TermOf(string title)
	{
		if (string.IsNullOrEmpty(title)) return null;
		foreach (string prefix in Prefixes())
		{
			if (title.StartsWith(prefix, StringComparison.Ordinal))
			{
				return title.Substring(prefix.Length);
			}
		}
		return null;
	}

	/// <summary>Rewrites a title left behind by another language into the current one.</summary>
	public static string Retranslate(string title)
	{
		string term = TermOf(title);
		return (term == null) ? title : (Prefix + term);
	}

	private static IEnumerable<string> Prefixes()
	{
		yield return Prefix;
		foreach (string language in UserLanguageHelper.Languages)
		{
			string word = Words.ResourceManager.GetString("Search", CultureInfo.CreateSpecificCulture(language));
			if (!string.IsNullOrEmpty(word))
			{
				yield return word + ": ";
			}
		}
	}
}
