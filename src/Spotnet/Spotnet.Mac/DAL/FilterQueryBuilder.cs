using System;
using System.Collections.Generic;
using System.Linq;

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
        "spots.rowid, spots.key, spots.cat, spots.subcat, spots.extcat, spots.date, spots.filesize, spots.cats, spots.sender, spots.tag, spots.subject, spots.msgid, spots.modulus, IFNULL(s.cnt, 0), (f.msgid IS NOT NULL) AS isfavorite";

    /// <summary>Windows hides its own placeholder rows (key 2 and 5) from every filter.</summary>
    public const string KeyGuard = "spots.key != 2 AND spots.key != 5";

    public static bool IsSearchFilter(string? filter)
        => filter != null && filter.Contains(" match ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the <c>[SN:DATE]</c> / <c>[SN:NEW]</c> / <c>[SN:FAV]</c> markers the bundled filters carry.
    /// </summary>
    public static string ResolveMarkers(string filter, long nowUnix, long rowNew)
    {
        string resolved = filter
            .Replace("[SN:DATE]", nowUnix.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("[SN:NEW]", rowNew.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (resolved.Contains("spots.msgid in favorieten", StringComparison.OrdinalIgnoreCase))
        {
            resolved = resolved.Replace("spots.msgid in favorieten", "spots.msgid IN (SELECT msgid FROM favorites)", StringComparison.OrdinalIgnoreCase);
        }
        else if (resolved.StartsWith("favorites", StringComparison.OrdinalIgnoreCase))
        {
            resolved = "spots.msgid IN (SELECT msgid FROM favorites)" + resolved["favorites".Length..];
        }
        else if (resolved.Contains("[SN:FAV]", StringComparison.OrdinalIgnoreCase))
        {
            resolved = resolved.Replace("[SN:FAV]", "spots.msgid IN (SELECT msgid FROM favorites)", StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static readonly System.Text.RegularExpressions.Regex PosterIdentRegex =
        new(@"^PosterIdent\s+IN\s+\(([W|B|F|T|N|,|\s|V|O]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string? BuildPosterIdentSql(string tokens)
    {
        var letters = tokens
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (letters.Count == 0) return null;

        var subclauses = new List<string>();
        foreach (var letter in letters)
        {
            switch (letter)
            {
                case "W":
                    subclauses.Add("modulus IN (SELECT key FROM whitelist WHERE type = 1)");
                    break;
                case "B":
                    subclauses.Add("(modulus IN (SELECT key FROM blacklist WHERE type = 1) OR msgid IN (SELECT key FROM blacklist WHERE type = 2))");
                    break;
                case "V" or "T":
                    subclauses.Add("(modulus IN (SELECT key FROM whitelist WHERE type = 1) OR msgid IN (SELECT key FROM whitelist WHERE type = 2) OR date < 1356998400)");
                    break;
                case "O" or "F":
                    subclauses.Add("(modulus NOT IN (SELECT key FROM whitelist WHERE type = 1) AND modulus != '' AND modulus != 'none')");
                    break;
                case "N":
                    subclauses.Add("(modulus NOT IN (SELECT key FROM blacklist WHERE type = 1) AND modulus NOT IN (SELECT key FROM whitelist WHERE type = 1) AND msgid NOT IN (SELECT key FROM blacklist WHERE type = 2) AND msgid NOT IN (SELECT key FROM whitelist WHERE type = 2) AND date >= 1356998400)");
                    break;
            }
        }

        return subclauses.Count > 0 ? $"({string.Join(" OR ", subclauses)})" : null;
    }

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

        string? posterIdentSql = null;
        var match = PosterIdentRegex.Match(filter.Trim());
        if (match.Success)
        {
            posterIdentSql = BuildPosterIdentSql(match.Groups[1].Value);
            filter = filter.Trim()[match.Length..].Trim();
            if (filter.StartsWith("AND ", StringComparison.OrdinalIgnoreCase))
            {
                filter = filter[4..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            if (posterIdentSql != null)
            {
                string spotsGuard = showErotica ? "" : "cat<9 AND ";
                return $"({spotsGuard}{posterIdentSql})";
            }
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
            string searchPredicate = $"spots.rowid IN (SELECT rowid FROM search WHERE ({guard}{compiled.CommandText}))";
            return posterIdentSql != null ? $"({posterIdentSql} AND {searchPredicate})" : searchPredicate;
        }

        string collapsed = filter.Replace(" ", "").ToLowerInvariant();
        string spotsCatGuard = showErotica || collapsed.Contains("cat=", StringComparison.Ordinal)
                                           || collapsed.Contains("cat<", StringComparison.Ordinal)
            ? ""
            : "cat<9 AND ";

        string compiledPredicate = $"({spotsCatGuard}{compiled.CommandText})";
        return posterIdentSql != null ? $"({posterIdentSql} AND {compiledPredicate})" : compiledPredicate;
    }
}
