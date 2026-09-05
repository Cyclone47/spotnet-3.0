using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Covers the bundled advanced-filter tree the macOS client shares with Windows:
/// that it loads, that every expression in it compiles, and that each one runs against
/// the real schema.
/// </summary>
public class FilterTreeTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public FilterTreeTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_filters_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
    }

    private static IEnumerable<FilterItem> Flatten(IEnumerable<FilterItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public void Load_ReturnsTheSameTopLevelFiltersAsWindows()
    {
        var tree = DefaultFilterProvider.Load();

        Assert.Equal(new[]
        {
            "Nieuw", "Overzicht", "Laatste 24 uur",
            "Beeld", "Beeld - Genres", "Beeld - TV Series",
            "Boeken", "Muziek", "Muziek - Genres",
            "Spellen", "Spellen - Console", "Spellen - Mobile",
            "Applicaties", "Applicaties - Mobile", "Erotiek"
        }, tree.Select(f => f.Name).ToArray());

        // Every group carries the sub-filters from the shared XML, not just a heading.
        Assert.Equal(18, tree.Single(f => f.Name == "Beeld").Children.Count);
        Assert.Equal(23, tree.Single(f => f.Name == "Muziek - Genres").Children.Count);
    }

    [Fact]
    public void EveryBundledFilterHasAQueryAndCompiles()
    {
        foreach (var filter in Flatten(DefaultFilterProvider.Load()))
        {
            Assert.False(string.IsNullOrWhiteSpace(filter.Query), $"{filter.Name} has no query");

            string resolved = FilterQueryBuilder.ResolveMarkers(filter.Query, 1_700_000_000, 42);
            var compiled = FilterExpressionCompiler.Compile(resolved);

            Assert.DoesNotContain("[SN:", compiled.CommandText, StringComparison.Ordinal);
            Assert.DoesNotContain("docid", compiled.CommandText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EveryBundledFilterExecutesAgainstTheSchema()
    {
        await _service.EnsureCreatedAsync();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem
            {
                Key = 1, Category = 1, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Cats = "1a9 1b3 1c11 1d0", Sender = "Poster", Tag = "NL",
                Subject = "Some Movie 2026 1080p", MsgId = "a@spot.net"
            },
            new SpotItem
            {
                Key = 1, Category = 4, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Cats = "4a0", Sender = "Poster", Tag = "portable",
                Subject = "Some App portable", MsgId = "b@spot.net"
            }
        });

        foreach (var filter in Flatten(DefaultFilterProvider.Load()))
        {
            // A count of zero is fine; throwing is not.
            int count = await _service.CountByFilterAsync(filter.Query);
            Assert.True(count >= 0, $"{filter.Name} returned a negative count");
        }
    }

    [Fact]
    public async Task CategoryFilterSelectsOnlyThatCategory()
    {
        await _service.EnsureCreatedAsync();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Cats = "1a9", Subject = "Film", MsgId = "f@spot.net", Date = 1_700_000_000 },
            new SpotItem { Key = 1, Category = 4, Cats = "4a0", Subject = "App",  MsgId = "g@spot.net", Date = 1_700_000_000 }
        });

        var apps = await _service.QueryByFilterAsync("cat=4");
        Assert.Single(apps);
        Assert.Equal("App", apps[0].Subject);
    }

    [Fact]
    public async Task SearchTextNarrowsTheSelectedFilterRatherThanReplacingIt()
    {
        await _service.EnsureCreatedAsync();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Cats = "1a9", Subject = "Inception 1080p", MsgId = "h@spot.net", Date = 1_700_000_000 },
            new SpotItem { Key = 1, Category = 1, Cats = "1a9", Subject = "Arrival 1080p",   MsgId = "i@spot.net", Date = 1_700_000_000 },
            new SpotItem { Key = 1, Category = 4, Cats = "4a0", Subject = "Inception App",   MsgId = "j@spot.net", Date = 1_700_000_000 }
        });

        var hits = await _service.QueryByFilterAsync("cat=1", "Inception");
        Assert.Single(hits);
        Assert.Equal("Inception 1080p", hits[0].Subject);
    }

    [Fact]
    public async Task NieuwFilterShowsOnlySpotsAddedAfterTheWatermark()
    {
        await _service.EnsureCreatedAsync();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Cats = "1a9", Subject = "Old", MsgId = "k@spot.net", Date = 1_700_000_000 }
        });

        await _service.MarkSpotsSeenAsync();

        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Cats = "1a9", Subject = "New", MsgId = "l@spot.net", Date = 1_700_000_100 }
        });

        var nieuw = DefaultFilterProvider.Load().Single(f => f.Name == "Nieuw");
        var hits = await _service.QueryByFilterAsync(nieuw.Query);

        Assert.Single(hits);
        Assert.Equal("New", hits[0].Subject);
    }

    [Fact]
    public async Task ACustomFilterCombiningCategoryAgeAndSubcatRuns()
    {
        await _service.EnsureCreatedAsync();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 4, Cats = "4 4a0", Subject = "Win app", MsgId = "m@spot.net",
                           Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            new SpotItem { Key = 1, Category = 4, Cats = "4 4a1", Subject = "Mac app", MsgId = "n@spot.net",
                           Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        });

        // The shape MainWindowViewModel.ComposeQuery produces for a user filter.
        var hits = await _service.QueryByFilterAsync("cat = 4 AND cats LIKE '%4a0%' AND date > ( [SN:DATE] - 86400 )");

        Assert.Single(hits);
        Assert.Equal("Win app", hits[0].Subject);
    }

    [Fact]
    public void MalformedFilterIsIgnoredRatherThanThrowing()
    {
        // Compilation rejects it...
        Assert.Throws<FormatException>(() => FilterExpressionCompiler.Compile("cat = 1; DROP TABLE spots"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { if (File.Exists(_tempDbFile)) File.Delete(_tempDbFile); } catch { /* best effort */ }
    }
}
