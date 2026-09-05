using System;
using System.Collections.Generic;

namespace Spotnet.Mac.DAL;

/// <summary>
/// Maps a spot-list column onto the database column it sorts by.
///
/// Sorting happens in SQL, not in the loaded page: with
/// <see cref="DataVirtualization.VirtualSpotCollection"/> only a few pages are in memory
/// at a time, so an in-memory sort would order those pages and leave the rest of the
/// result set untouched — which is what the client did before.
///
/// Several displayed columns are computed rather than stored. "Formaat" and "Genre" are
/// derived from <c>subcat</c> and <c>cats</c>, and "Leeftijd" is a rendering of
/// <c>date</c>. Sorting therefore runs on the underlying column, which is what the
/// Windows client does too.
///
/// The dictionary is also the allow-list: <see cref="ToSqlColumn"/> never returns
/// anything a caller supplied, so a stored preference cannot reach the query text.
/// </summary>
public static class SpotSort
{
    /// <summary>The property the "Leeftijd" column binds to — newest first, as on Windows.</summary>
    public const string DefaultColumn = "Age";

    public const string DefaultDirection = "DESC";

    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FormatLabel"] = "subcat",
        ["Subject"] = "subject",
        ["GenreLabel"] = "cats",
        ["SenderName"] = "sender",
        ["Age"] = "date",
        ["FormattedSize"] = "filesize",
    };

    /// <summary>Whether the grid may offer sorting on this column.</summary>
    public static bool IsSortable(string? sortMemberPath) =>
        !string.IsNullOrEmpty(sortMemberPath) && Columns.ContainsKey(sortMemberPath);

    /// <summary>
    /// The database column for a grid column, or <c>date</c> for anything unrecognised.
    /// Falling back rather than throwing means a preferences file written by an older or
    /// newer build still opens.
    /// </summary>
    public static string ToSqlColumn(string? sortMemberPath) =>
        sortMemberPath != null && Columns.TryGetValue(sortMemberPath, out var column)
            ? column
            : "date";

    /// <summary>Normalises a stored direction to exactly "ASC" or "DESC".</summary>
    public static string NormalizeDirection(string? direction) =>
        "ASC".Equals(direction, StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

    /// <summary>Normalises a stored column name to one this build knows.</summary>
    public static string NormalizeColumn(string? sortMemberPath) =>
        IsSortable(sortMemberPath) ? sortMemberPath! : DefaultColumn;
}
