using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

public class FilterTogglesTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public FilterTogglesTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_toggles_test_{Guid.NewGuid():N}.db");
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
    public async Task HideBlacklistedSpots_ExcludesBlacklistedModulusAndMsgId()
    {
        await _service.EnsureCreatedAsync();

        var spots = new[]
        {
            new SpotItem { Id = 1, MsgId = "msg1@test", Modulus = "mod_clean", Subject = "Clean Spot", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 2, MsgId = "msg2@test", Modulus = "mod_blacklisted", Subject = "Blacklisted Modulus Spot", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 3, MsgId = "msg_blacklisted@test", Modulus = "mod_clean2", Subject = "Blacklisted MsgId Spot", Category = 1, Cats = "1 0", Date = 1400000000 }
        };
        await _service.InsertSpotsAsync(spots);

        await _service.SyncTrustListsAsync(
            blackModuli: new[] { "mod_blacklisted" },
            blackMsgIds: new[] { "msg_blacklisted@test" },
            whiteModuli: Array.Empty<string>(),
            whiteMsgIds: Array.Empty<string>()
        );

        // When hideBlacklisted is false, all 3 spots are returned
        var countAll = await _service.CountByFilterAsync(null, hideBlacklisted: false);
        var spotsAll = await _service.QueryByFilterAsync(null, hideBlacklisted: false);
        Assert.Equal(3, countAll);
        Assert.Equal(3, spotsAll.Count);

        // When hideBlacklisted is true, only the clean spot is returned
        var countFiltered = await _service.CountByFilterAsync(null, hideBlacklisted: true);
        var spotsFiltered = await _service.QueryByFilterAsync(null, hideBlacklisted: true);
        Assert.Equal(1, countFiltered);
        Assert.Single(spotsFiltered);
        Assert.Equal("msg1@test", spotsFiltered[0].MsgId);
    }

    [Fact]
    public async Task ShowTrustedOnlyMode_FiltersUnverifiedSpots_AllowsWhitelistAndPre2013Spots()
    {
        await _service.EnsureCreatedAsync();

        var spots = new[]
        {
            new SpotItem { Id = 1, MsgId = "msg1@test", Modulus = "white_modulus", Subject = "Whitelisted Modulus", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 2, MsgId = "white_msg@test", Modulus = "other_mod", Subject = "Whitelisted MsgId", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 3, MsgId = "msg3@test", Modulus = "untrusted_mod", Subject = "Untrusted Modulus", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 4, MsgId = "msg4@test", Modulus = "", Subject = "Pre-2013 Legacy Spot", Category = 1, Cats = "1 0", Date = 1200000000 } // < 1356998400
        };
        await _service.InsertSpotsAsync(spots);

        await _service.SyncTrustListsAsync(
            blackModuli: Array.Empty<string>(),
            blackMsgIds: Array.Empty<string>(),
            whiteModuli: new[] { "white_modulus" },
            whiteMsgIds: new[] { "white_msg@test" }
        );

        // When showTrustedOnly is false, all 4 spots are returned
        var countAll = await _service.CountByFilterAsync(null, showTrustedOnly: false);
        var spotsAll = await _service.QueryByFilterAsync(null, showTrustedOnly: false);
        Assert.Equal(4, countAll);
        Assert.Equal(4, spotsAll.Count);

        // When showTrustedOnly is true, untrusted spot is excluded, but whitelisted and pre-2013 spots are kept
        var countTrusted = await _service.CountByFilterAsync(null, showTrustedOnly: true);
        var spotsTrusted = await _service.QueryByFilterAsync(null, showTrustedOnly: true);
        Assert.Equal(3, countTrusted);
        Assert.Equal(3, spotsTrusted.Count);
        Assert.Contains(spotsTrusted, s => s.MsgId == "msg1@test");
        Assert.Contains(spotsTrusted, s => s.MsgId == "white_msg@test");
        Assert.Contains(spotsTrusted, s => s.MsgId == "msg4@test");
        Assert.DoesNotContain(spotsTrusted, s => s.MsgId == "msg3@test");
    }

    [Fact]
    public async Task ShowEroticaInSearchResults_ControlsCat9VisibilityInSearches()
    {
        await _service.EnsureCreatedAsync();

        var spots = new[]
        {
            new SpotItem { Id = 1, MsgId = "spot1@test", Modulus = "m1", Subject = "Avontuur Algemeen", Category = 1, Cats = "1 0", Date = 1400000000 },
            new SpotItem { Id = 2, MsgId = "spot2@test", Modulus = "m2", Subject = "Avontuur Erotiek", Category = 9, Cats = "9 1", Date = 1400000000 }
        };
        await _service.InsertSpotsAsync(spots);

        // When search is active and showErotica is false:
        var countNoErotica = await _service.CountByFilterAsync(null, searchText: "Avontuur", showErotica: false);
        var spotsNoErotica = await _service.QueryByFilterAsync(null, searchText: "Avontuur", showErotica: false);
        Assert.Equal(1, countNoErotica);
        Assert.Single(spotsNoErotica);
        Assert.Equal(1, spotsNoErotica[0].Category);

        // When search is active and showErotica is true:
        var countWithErotica = await _service.CountByFilterAsync(null, searchText: "Avontuur", showErotica: true);
        var spotsWithErotica = await _service.QueryByFilterAsync(null, searchText: "Avontuur", showErotica: true);
        Assert.Equal(2, countWithErotica);
        Assert.Equal(2, spotsWithErotica.Count);
    }
}
