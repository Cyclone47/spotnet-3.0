using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

public class FavoritesTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public FavoritesTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_fav_test_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAndRemoveFavorites_UpdatesTableAndCatsBackwardsCompatibility()
    {
        await _service.EnsureCreatedAsync();

        var spot1 = new SpotItem { Id = 1, MsgId = "msg1@test", Subject = "Spot 1", Category = 1, Cats = "1 0", Date = 1400000000 };
        var spot2 = new SpotItem { Id = 2, MsgId = "msg2@test", Subject = "Spot 2", Category = 1, Cats = "1 0", Date = 1400000001 };
        await _service.InsertSpotsAsync(new[] { spot1, spot2 });

        Assert.False(await _service.IsFavoriteAsync("msg1@test"));
        Assert.Equal(0, await _service.GetFavoritesCountAsync());

        // Add to favorites
        await _service.AddFavoriteAsync("msg1@test");
        Assert.True(await _service.IsFavoriteAsync("msg1@test"));
        Assert.False(await _service.IsFavoriteAsync("msg2@test"));
        Assert.Equal(1, await _service.GetFavoritesCountAsync());

        var favIds = await _service.GetFavoriteMsgIdsAsync();
        Assert.Single(favIds);
        Assert.Equal("msg1@test", favIds[0]);

        // Verify spot row cats has ' f1' appended for backwards compatibility
        var loadedSpot1 = await _service.GetSpotByMsgIdAsync("msg1@test");
        Assert.NotNull(loadedSpot1);
        Assert.True(loadedSpot1.IsFavorite);
        Assert.Contains("f1", loadedSpot1.Cats);

        // Remove from favorites
        await _service.RemoveFavoriteAsync("msg1@test");
        Assert.False(await _service.IsFavoriteAsync("msg1@test"));
        Assert.Equal(0, await _service.GetFavoritesCountAsync());

        loadedSpot1 = await _service.GetSpotByMsgIdAsync("msg1@test");
        Assert.NotNull(loadedSpot1);
        Assert.False(loadedSpot1.IsFavorite);
        Assert.DoesNotContain("f1", loadedSpot1.Cats);
    }

    [Fact]
    public async Task QueryByFilter_SpotsMsgIdInFavorieten_ReturnsOnlyFavorites()
    {
        await _service.EnsureCreatedAsync();

        var spots = new[]
        {
            new SpotItem { Id = 1, MsgId = "spot1@test", Subject = "Favoriet 1", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 2, MsgId = "spot2@test", Subject = "Normaal 2", Category = 1, Cats = "1 0", Date = 1400000001 },
            new SpotItem { Id = 3, MsgId = "spot3@test", Subject = "Favoriet 3", Category = 1, Cats = "1 0", Date = 1400000002 }
        };
        await _service.InsertSpotsAsync(spots);

        await _service.AddFavoriteAsync("spot1@test");
        await _service.AddFavoriteAsync("spot3@test");

        // Test with "spots.msgid in favorieten"
        var favCount = await _service.CountByFilterAsync("spots.msgid in favorieten");
        Assert.Equal(2, favCount);

        var favSpots = await _service.QueryByFilterAsync("spots.msgid in favorieten");
        Assert.Equal(2, favSpots.Count);
        Assert.Contains(favSpots, s => s.MsgId == "spot1@test" && s.IsFavorite);
        Assert.Contains(favSpots, s => s.MsgId == "spot3@test" && s.IsFavorite);
        Assert.DoesNotContain(favSpots, s => s.MsgId == "spot2@test");

        // Test with "[SN:FAV]" marker
        var markerCount = await _service.CountByFilterAsync("[SN:FAV]");
        Assert.Equal(2, markerCount);
    }

    [Fact]
    public async Task QueryByFilter_SortingByIsFavorite_OrdersFavoritesAppropriately()
    {
        await _service.EnsureCreatedAsync();

        var spots = new[]
        {
            new SpotItem { Id = 1, MsgId = "spot1@test", Subject = "Gewoon", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 2, MsgId = "spot2@test", Subject = "Ster", Category = 1, Cats = "1 0", Date = 1400000001 }
        };
        await _service.InsertSpotsAsync(spots);
        await _service.AddFavoriteAsync("spot2@test");

        // Sort DESC: favorite (1) first
        var descSpots = await _service.QueryByFilterAsync(null, sortColumn: "IsFavorite", sortDirection: "DESC");
        Assert.Equal(2, descSpots.Count);
        Assert.True(descSpots[0].IsFavorite);
        Assert.Equal("spot2@test", descSpots[0].MsgId);
        Assert.False(descSpots[1].IsFavorite);

        // Sort ASC: non-favorite (0) first
        var ascSpots = await _service.QueryByFilterAsync(null, sortColumn: "IsFavorite", sortDirection: "ASC");
        Assert.Equal(2, ascSpots.Count);
        Assert.False(ascSpots[0].IsFavorite);
        Assert.True(ascSpots[1].IsFavorite);
    }

    [Fact]
    public void SpotItem_FavoritePropertiesAndNotifications_BehaveCorrectly()
    {
        var spot = new SpotItem { MsgId = "fav@test" };
        Assert.False(spot.IsFavorite);
        Assert.Equal("☆", spot.FavoriteStar);
        Assert.Equal("Toevoegen aan Favorieten", spot.FavoriteTooltip);

        string? lastProp = null;
        spot.PropertyChanged += (_, e) => lastProp = e.PropertyName;

        spot.IsFavorite = true;
        Assert.True(spot.IsFavorite);
        Assert.Equal("★", spot.FavoriteStar);
        Assert.Equal("Verwijderen uit Favorieten", spot.FavoriteTooltip);
        Assert.NotNull(lastProp);
    }
}
