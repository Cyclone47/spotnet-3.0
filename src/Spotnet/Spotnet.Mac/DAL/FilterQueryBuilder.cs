using System;
using System.Collections.Generic;

namespace Spotnet.Mac.DAL;

/// <summary>
/// Turns a Spotnet filter expression (the mini-language used by the bundled
/// <c>Resources/FiltersAdvanced.xml</c> tree and by user filters) into a WHERE predicate
/// against the <c>spots</c> table.
///
/// Mirrors <c>SpotProvider.BuildQuery</c> / <c>BuildSearchQuery</c> on Windows:
/// a filter that mentions <c>MATCH</c> runs against the FTS5 <c>search</c> table and is
/// folded back into <c>spots</c> through <c>rowid IN (...)</c>; everything else runs
/// directly against <c>spots</c>.
/// </summary>
public static class FilterQueryBuilder
{
    /// <summary>Columns selected by every spot query, in the order <c>MapSpotRow</c> expects.</summary>
    public const string SpotColumns =
        "rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus";

    /// <summary>Windows hides its own placeholder rows (key 2 and 5) from every filter.</summary>
    public const string KeyGuard = "key != 2 AND key != 5";

    public static bool IsSearchFilter(string? filter)
        => filter != null && filter.Contains(" match ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the <c>[SN:DATE]</c> / <c>[SN:NEW]</c> markers the bundled filters carry.
    /// </summary>
    public static string ResolveMarkers(string filter, long nowUnix, long rowNew)
        => filter
            .Replace("[SN:DATE]", nowUnix.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("[SN:NEW]", rowNew.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the predicate for <paramref name="filter"/>, appending its parameters to
    /// <paramref name="values"/>. Returns null when the filter is empty.
    ///
    /// <paramref name="showErotica"/> mirrors the ShowEroticaInSearchResults setting
    /// (Windows default: false). When it is off, a filter that does not mention a
    /// category itself gets the same erotica guard Windows adds — "cat&lt;9" on the spots
    /// table, "cats NOT LIKE '9 %'" on the search table.
    /// </summary>
    public static string? BuildPredicate(string? filter, long nowUnix, long rowNew, List<SqlValue> values,
                                         bool showErotica = false)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        bool isSearch = IsSearchFilter(filter);
        var compiled = FilterExpressionCompiler.Compile(ResolveMarkers(filter, nowUnix, rowNew));
        values.AddRange(compiled.Values);

        if (isSearch)
        {
            string guard = showErotica || filter.Contains("cats match ", StringComparison.OrdinalIgnoreCase)
                ? ""
                : "cats NOT LIKE '9 %' AND ";
            return $"rowid IN (SELECT rowid FROM search WHERE ({guard}{compiled.CommandText}))";
        }

        string collapsed = filter.Replace(" ", "").ToLowerInvariant();
        string spotsGuard = showErotica || collapsed.Contains("cat=", StringComparison.Ordinal)
                                        || collapsed.Contains("cat<", StringComparison.Ordinal)
            ? ""
            : "cat<9 AND ";
        return $"({spotsGuard}{compiled.CommandText})";
    }
}
