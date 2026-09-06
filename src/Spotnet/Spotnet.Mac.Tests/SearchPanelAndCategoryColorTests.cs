using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Fase 6 (weergave), deel 1: het ZOEKEN-paneel van Windows — zoekveld
/// (Titel/Afzender/Label), Uitgebreid en Favorieten — en de categoriekleur van
/// Windows' Spots.CategoryToColor voor de rijstrepen en filterstippen.
/// </summary>
public class SearchPanelAndCategoryColorTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public SearchPanelAndCategoryColorTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_searchpanel_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
        _service.EnsureCreatedAsync().GetAwaiter().GetResult();

        _service.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "t1@x", Subject = "Linux Mint 22 released", Sender = "willem@poster", Tag = "mint", Category = 4, Key = 0, Date = 1700000000, Filesize = 1200000000 },
            new SpotItem { MsgId = "t2@x", Subject = "Windows 11 ISO", Sender = "karel@poster", Tag = "win", Category = 4, Key = 0, Date = 1700000100, Filesize = 5000000000 },
            new SpotItem { MsgId = "t3@x", Subject = "Film: De Tocht", Sender = "willem@poster", Tag = "film", Category = 1, Key = 0, Date = 1700000200, Filesize = 4400000000 }
        }).GetAwaiter().GetResult();

        _service.AddFavoriteAsync("t2@x").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_tempDbFile); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SearchField_Tag_SearchesOnlyTagColumn()
    {
        // "mint" staat in de tag van t1 en niet in de titel; "win" alleen in de tag van t2.
        var hits = await _service.QueryByFilterAsync(null, "win", searchField: "tag");
        var hit = Assert.Single(hits);
        Assert.Equal("t2@x", hit.MsgId);
    }

    [Fact]
    public async Task SearchField_Sender_SearchesOnlySenderColumn()
    {
        var hits = await _service.QueryByFilterAsync(null, "karel", searchField: "sender");
        var hit = Assert.Single(hits);
        Assert.Equal("t2@x", hit.MsgId);

        // "willem" heeft twee spots geplaatst.
        var two = await _service.QueryByFilterAsync(null, "willem", searchField: "sender");
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public async Task SearchField_Title_Default_SearchesAllColumns()
    {
        // Titel-zoekopdracht (de stand): "karel" staat nergens in de titel maar wél
        // in de afzender — het brede FTS-zoeken (oude gedrag) vindt hem alsnog.
        var hits = await _service.QueryByFilterAsync(null, "karel", searchField: "subject");
        Assert.Single(hits);
    }

    [Fact]
    public async Task ExtensiveSearch_PrefixMatches_PartOfWord()
    {
        // Niet-uitgebreid: alleen hele term "mint" tref je in de tag.
        var exact = await _service.QueryByFilterAsync(null, "mint", searchField: "tag", extensiveSearch: false);
        Assert.Single(exact);

        // Niet-uitgebreid: een deel van het woord ("min") geeft niets.
        var partialOff = await _service.QueryByFilterAsync(null, "min", searchField: "tag", extensiveSearch: false);
        Assert.Empty(partialOff);

        // Uitgebreid: "min" matcht prefix van "mint".
        var partialOn = await _service.QueryByFilterAsync(null, "min", searchField: "tag", extensiveSearch: true);
        Assert.Single(partialOn);
    }

    [Fact]
    public async Task FavoritesOnly_ReturnsOnlyFavorites()
    {
        var hits = await _service.QueryByFilterAsync(null, favoritesOnly: true);
        var hit = Assert.Single(hits);
        Assert.Equal("t2@x", hit.MsgId);

        var count = await _service.CountByFilterAsync(null, favoritesOnly: true);
        Assert.Equal(1, count);

        // Zonder het vinkje alles (3).
        var all = await _service.QueryByFilterAsync(null);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task FavoritesOnly_CombinesWithSearch()
    {
        // "willem" heeft t1 en t3 geplaatst, maar geen van beiden is favoriet.
        var hits = await _service.QueryByFilterAsync(null, "willem", searchField: "sender", favoritesOnly: true);
        Assert.Empty(hits);
    }

    // ── Categoriekleuren, letterlijk Windows' Spots.CategoryToColor ───────────

    [Theory]
    [InlineData(1, "#21409A")]  // Films
    [InlineData(2, "#FFFFAA")]  // Muziek
    [InlineData(3, "#FF4D25")]  // Spellen
    [InlineData(4, "#FF7BAC")]  // Applicaties
    [InlineData(5, "#7AC943")]  // Boeken
    [InlineData(6, "#3FA9F5")]  // Series
    [InlineData(9, "#BDCCD4")]  // Erotiek
    [InlineData(0, "#FF4500")]  // default: OrangeRed
    public void CategoryToColor_MatchesWindowsMapping(int cat, string expected)
    {
        Assert.Equal(expected, SpotItem.CategoryToColor(cat));
    }

    [Fact]
    public void FilterDot_ExtractsCategoryFromQuery()
    {
        var byId = new FilterItem { Kind = FilterKind.Category, CategoryId = 6, Query = "cat = 6" };
        Assert.Equal("#3FA9F5", byId.GenreDotColor);
        Assert.True(byId.HasGenreDot);

        var byQuery = new FilterItem { Kind = FilterKind.Preset, Query = "spots.cat = 3 AND cats MATCH '3a0'" };
        Assert.Equal("#FF4D25", byQuery.GenreDotColor);

        var noCat = new FilterItem { Kind = FilterKind.Preset, Query = "date > 123" };
        Assert.Equal("", noCat.GenreDotColor);
        Assert.False(noCat.HasGenreDot);
    }
}
