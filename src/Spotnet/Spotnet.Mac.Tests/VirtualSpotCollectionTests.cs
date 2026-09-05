using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.DataVirtualization;
using Spotnet.Mac.Models;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Runs every dispatched action inline, on the calling thread, so a test can assert on
/// what a page load produced without pumping an Avalonia message loop.
/// </summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}

/// <summary>
/// Covers the data virtualisation that replaced the old <c>LIMIT 100</c> spot list:
/// that the collection reports the whole result, fetches only what is looked at, fills
/// rows in place, and bounds its memory.
/// </summary>
public class VirtualSpotCollectionTests
{
    private static SpotItem Row(int i) => new()
    {
        Id = i,
        Date = 1_700_000_000 + i,
        Subject = $"Spot {i}",
        Sender = $"poster{i}",
        MsgId = $"<{i}@spot.net>",
        Filesize = 1024L * i,
    };

    /// <summary>A loader over a synthetic table, recording every window it was asked for.</summary>
    private static SpotPageLoader Loader(int total, List<(int skip, int take)> calls)
        => (skip, take, _) =>
        {
            calls.Add((skip, take));
            var page = Enumerable.Range(skip, Math.Max(0, Math.Min(take, total - skip))).Select(Row).ToList();
            return Task.FromResult<IReadOnlyList<SpotItem>>(page);
        };

    [Fact]
    public void Count_is_the_whole_result_not_the_page_size()
    {
        var calls = new List<(int, int)>();
        using var list = new VirtualSpotCollection(Loader(250_000, calls), 250_000, new ImmediateDispatcher());

        Assert.Equal(250_000, list.Count);

        // Nothing has been looked at, so nothing has been fetched. This is the whole
        // point: the old code ran one query for a hundred rows and called it the result.
        Assert.Empty(calls);
    }

    [Fact]
    public void Touching_a_row_fetches_only_that_page()
    {
        var calls = new List<(int skip, int take)>();
        using var list = new VirtualSpotCollection(Loader(10_000, calls), 10_000, new ImmediateDispatcher(), pageSize: 200);

        _ = list[0];
        _ = list[5_000];

        Assert.Equal(2, calls.Count);
        Assert.Contains((0, 200), calls);
        Assert.Contains((5_000, 200), calls);
    }

    [Fact]
    public void A_page_is_requested_once_however_many_rows_are_touched()
    {
        var calls = new List<(int, int)>();
        using var list = new VirtualSpotCollection(Loader(1_000, calls), 1_000, new ImmediateDispatcher(), pageSize: 200);

        for (int i = 0; i < 200; i++) _ = list[i];

        Assert.Single(calls);
    }

    [Fact]
    public void Rows_are_filled_in_place_so_the_grid_keeps_its_selection()
    {
        var calls = new List<(int, int)>();
        using var list = new VirtualSpotCollection(Loader(1_000, calls), 1_000, new ImmediateDispatcher(), pageSize: 200);

        var first = list[7];
        var again = list[7];

        // Same instance before and after loading: a selected row stays selected.
        Assert.Same(first, again);
        Assert.False(first.IsPlaceholder);
        Assert.Equal("Spot 7", first.Subject);
    }

    [Fact]
    public async Task A_row_reads_as_a_placeholder_until_its_page_arrives()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<SpotItem>>();
        using var list = new VirtualSpotCollection((_, _, _) => gate.Task, 500, new ImmediateDispatcher(), pageSize: 100);

        var filled = new TaskCompletionSource();
        list.PageLoaded += _ => filled.TrySetResult();

        var row = list[3];
        Assert.True(row.IsPlaceholder);
        Assert.Equal(string.Empty, row.Subject);

        gate.SetResult(Enumerable.Range(0, 100).Select(Row).ToList());

        // The loader awaits off the UI thread, so the fill lands on a continuation
        // rather than inside SetResult.
        await filled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(row.IsPlaceholder);
        Assert.Equal("Spot 3", row.Subject);
    }

    [Fact]
    public void The_last_page_is_short_when_the_count_is_not_a_whole_number_of_pages()
    {
        var calls = new List<(int, int)>();
        using var list = new VirtualSpotCollection(Loader(250, calls), 250, new ImmediateDispatcher(), pageSize: 100);

        var last = list[249];

        Assert.Equal("Spot 249", last.Subject);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[250]);
    }

    [Fact]
    public void Old_pages_are_dropped_once_the_cache_is_full()
    {
        var calls = new List<(int, int)>();
        using var list = new VirtualSpotCollection(
            Loader(100_000, calls), 100_000, new ImmediateDispatcher(), pageSize: 100, maxCachedPages: 3);

        for (int page = 0; page < 6; page++) _ = list[page * 100];

        Assert.Equal(3, list.LoadedPageCount);

        // Scrolling back re-reads the dropped page rather than showing a blank row.
        int before = calls.Count;
        var revisited = list[0];
        Assert.Equal(before + 1, calls.Count);
        Assert.Equal("Spot 0", revisited.Subject);
    }

    [Fact]
    public void A_failing_page_leaves_placeholders_and_can_be_retried()
    {
        int attempts = 0;
        using var list = new VirtualSpotCollection((skip, take, _) =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("database is locked");
            return Task.FromResult<IReadOnlyList<SpotItem>>(
                Enumerable.Range(skip, take).Select(Row).ToList());
        }, 1_000, new ImmediateDispatcher(), pageSize: 100);

        var row = list[0];
        Assert.True(row.IsPlaceholder);

        // Same row object, so the retry fills the instance the grid is already showing.
        var retried = list[0];
        Assert.Same(row, retried);
        Assert.False(retried.IsPlaceholder);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void A_short_page_leaves_the_rest_as_placeholders_rather_than_stale_rows()
    {
        // The table shrank under us: the count said 100 but only 40 rows come back.
        using var list = new VirtualSpotCollection(
            (_, _, _) => Task.FromResult<IReadOnlyList<SpotItem>>(Enumerable.Range(0, 40).Select(Row).ToList()),
            100, new ImmediateDispatcher(), pageSize: 100);

        Assert.False(list[39].IsPlaceholder);
        Assert.True(list[40].IsPlaceholder);
    }

    [Fact]
    public void Mutating_the_list_is_refused_because_it_is_a_view_over_the_database()
    {
        using var list = new VirtualSpotCollection(Loader(10, new List<(int, int)>()), 10, new ImmediateDispatcher());

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(Row(1)));
        Assert.Throws<NotSupportedException>(() => list.Remove(Row(1)));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
    }

    [Fact]
    public void IndexOf_finds_a_loaded_row_so_the_grid_can_track_the_selection()
    {
        using var list = new VirtualSpotCollection(Loader(1_000, new List<(int, int)>()), 1_000, new ImmediateDispatcher(), pageSize: 100);

        var row = list[42];
        Assert.Equal(42, list.IndexOf(row));

        // Contains() over IndexOf, rather than Assert.Contains, which would enumerate
        // the collection and pull every page it walks.
        Assert.True(list.Contains(row), "a loaded row should be found without fetching the rest");

        // A row from another list is not in this one.
        Assert.Equal(-1, list.IndexOf(Row(42)));
    }
}

/// <summary>
/// The column allow-list behind the ORDER BY. Anything a preferences file can carry has
/// to come out of here as a known column name.
/// </summary>
public class SpotSortTests
{
    [Theory]
    [InlineData("FormatLabel", "subcat")]
    [InlineData("Subject", "subject")]
    [InlineData("GenreLabel", "cats")]
    [InlineData("SenderName", "sender")]
    [InlineData("Age", "date")]
    [InlineData("FormattedSize", "filesize")]
    public void Every_grid_column_maps_onto_a_stored_column(string member, string sql)
    {
        Assert.True(SpotSort.IsSortable(member));
        Assert.Equal(sql, SpotSort.ToSqlColumn(member));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bogus")]
    [InlineData("date; DROP TABLE spots--")]
    [InlineData("1) OR 1=1 --")]
    public void Anything_unrecognised_falls_back_to_date(string? member)
    {
        Assert.False(SpotSort.IsSortable(member));
        Assert.Equal("date", SpotSort.ToSqlColumn(member));
        Assert.Equal(SpotSort.DefaultColumn, SpotSort.NormalizeColumn(member));
    }

    [Theory]
    [InlineData("ASC", "ASC")]
    [InlineData("asc", "ASC")]
    [InlineData("DESC", "DESC")]
    [InlineData("sideways", "DESC")]
    [InlineData(null, "DESC")]
    public void Direction_normalises_to_one_of_two_words(string? given, string expected)
    {
        Assert.Equal(expected, SpotSort.NormalizeDirection(given));
    }
}
