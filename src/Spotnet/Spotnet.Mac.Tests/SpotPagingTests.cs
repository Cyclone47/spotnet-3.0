using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Paging over a real database. The virtual spot list walks the result set with OFFSET
/// and LIMIT, one page at a time, so the query has to return a total order — otherwise
/// SQLite is free to break ties differently per page and rows show up twice or not at
/// all. Every sortable column here has deliberate ties.
/// </summary>
public class SpotPagingTests : IDisposable
{
    private const int Total = 500;
    private const int PageSize = 100;

    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public SpotPagingTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_paging_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
    }

    private async Task SeedAsync()
    {
        await _service.EnsureCreatedAsync();

        var spots = new List<SpotItem>();
        for (int i = 0; i < Total; i++)
        {
            spots.Add(new SpotItem
            {
                Key = 1,
                Category = 1,
                Subcat = 100 + (i % 5),                 // ties on Formaat
                Date = 1_700_000_000 + (i / 10),        // ten spots share every timestamp
                Filesize = 1024L * (i % 7),             // ties on Omvang
                Cats = "a0|d1",                         // every row identical: total tie on Genre
                Sender = $"poster{i % 3}",              // ties on Afzender
                Tag = "",
                Subject = $"Spot {i % 4}",              // ties on Titel
                MsgId = $"<{i}@spot.net>",
                Modulus = "",
            });
        }

        await _service.InsertSpotsAsync(spots);
    }

    /// <summary>Walks the whole result set the way the virtual list does.</summary>
    private async Task<List<string>> PageThroughAsync(string sortColumn, string direction)
    {
        var seen = new List<string>();
        for (int skip = 0; skip < Total; skip += PageSize)
        {
            var page = await _service.QueryByFilterAsync(
                filterQuery: null, searchText: null,
                skip: skip, take: PageSize,
                sortDirection: direction, sortColumn: sortColumn);

            seen.AddRange(page.Select(s => s.MsgId));
        }
        return seen;
    }

    [Theory]
    [InlineData("Age", "DESC")]
    [InlineData("Age", "ASC")]
    [InlineData("Subject", "ASC")]
    [InlineData("SenderName", "ASC")]
    [InlineData("FormattedSize", "DESC")]
    [InlineData("FormatLabel", "ASC")]
    [InlineData("GenreLabel", "ASC")]
    public async Task Paging_through_a_tied_result_set_sees_every_spot_exactly_once(string column, string direction)
    {
        await SeedAsync();

        var seen = await PageThroughAsync(column, direction);

        Assert.Equal(Total, seen.Count);
        Assert.Equal(Total, seen.Distinct().Count());
    }

    [Fact]
    public async Task The_count_matches_what_paging_actually_yields()
    {
        await SeedAsync();

        int count = await _service.CountByFilterAsync(null, null);
        var seen = await PageThroughAsync(SpotSort.DefaultColumn, SpotSort.DefaultDirection);

        // The virtual list reports the count as its size, so a mismatch would leave
        // rows that can never be scrolled to, or rows that are permanently blank.
        Assert.Equal(count, seen.Count);
    }

    [Fact]
    public async Task Sorting_runs_over_the_whole_table_not_just_the_first_page()
    {
        await SeedAsync();

        var firstAscending = await _service.QueryByFilterAsync(
            null, null, skip: 0, take: 1, sortDirection: "ASC", sortColumn: "FormattedSize");
        var firstDescending = await _service.QueryByFilterAsync(
            null, null, skip: 0, take: 1, sortDirection: "DESC", sortColumn: "FormattedSize");

        // Filesizes run 0 .. 6 KB across the whole table. Ordering in memory over one
        // page could not produce both ends.
        Assert.Equal(0, firstAscending[0].Filesize);
        Assert.Equal(1024L * 6, firstDescending[0].Filesize);
    }

    [Fact]
    public async Task An_offset_past_the_end_returns_nothing_rather_than_failing()
    {
        await SeedAsync();

        var page = await _service.QueryByFilterAsync(
            null, null, skip: Total + 500, take: PageSize);

        Assert.Empty(page);
    }

    [Fact]
    public async Task An_unknown_sort_column_falls_back_to_date_instead_of_reaching_the_query()
    {
        await SeedAsync();

        var injected = await _service.QueryByFilterAsync(
            null, null, skip: 0, take: 5, sortDirection: "DESC",
            sortColumn: "date; DROP TABLE spots--");

        // The table is still there and the order is the default one.
        var byDate = await _service.QueryByFilterAsync(null, null, skip: 0, take: 5);
        Assert.Equal(byDate.Select(s => s.MsgId), injected.Select(s => s.MsgId));
        Assert.Equal(Total, await _service.CountByFilterAsync(null, null));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _db.Dispose();
        try { if (File.Exists(_tempDbFile)) File.Delete(_tempDbFile); } catch (IOException) { }
    }
}
