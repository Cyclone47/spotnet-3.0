using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public class TrustServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly TestAppPaths _paths;
    private readonly UserPreferencesService _prefsService;
    private readonly string _dbPath;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;

    public TrustServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"spotnet_trust_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _paths = new TestAppPaths(_tempFolder);
        _prefsService = new UserPreferencesService(_paths);

        _dbPath = Path.Combine(_tempFolder, "spots.db");
        _db = new MacSqliteDb(_dbPath);
        _dbService = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_InitializesDefaultWhitelistWithOfficialUploaders()
    {
        using var service = new TrustService(_paths, _prefsService);

        string whitelistFile = Path.Combine(_tempFolder, "whitelist.xml");
        Assert.True(File.Exists(whitelistFile));

        // Should include Albertina and Zoutoplossing from official list
        Assert.Contains("1wt6jlePL/IADm4wL8lMqHaGVznPTiUvcovAtj3eCgvt3wTyM9Fd8ptx8+xzmAHL", service.WhitelistModuli);
        Assert.Contains("uH19iDBeTjye6rhOi4uLR+T59MThUBQNL0ZgQhsX6BQqQxZNYflwccud9ZN64Rb5", service.WhitelistModuli);
        Assert.True(service.WhitelistModuli.Count >= 29);
    }

    [Fact]
    public void AddBlack_And_RemoveBlack_PersistsAndUpdatesSets()
    {
        using var service = new TrustService(_paths, _prefsService);
        string testMod = "TEST_MODULUS_BLACK_123";

        bool added = service.AddBlack("Spammer", testMod);
        Assert.True(added);
        Assert.Contains(testMod, service.BlacklistModuli);

        // Verify XML file
        string xmlContent = File.ReadAllText(Path.Combine(_tempFolder, "blacklist.xml"));
        Assert.Contains(testMod, xmlContent);
        Assert.Contains("Spammer", xmlContent);

        // Remove
        bool removed = service.RemoveBlack(testMod);
        Assert.True(removed);
        Assert.DoesNotContain(testMod, service.BlacklistModuli);

        string updatedXml = File.ReadAllText(Path.Combine(_tempFolder, "blacklist.xml"));
        Assert.DoesNotContain(testMod, updatedXml);
    }

    [Fact]
    public void AddSpotBlack_And_AddSpotWhite_WorkAsExpected()
    {
        using var service = new TrustService(_paths, _prefsService);
        string msgId = "badspot123@spot.net";
        string goodMsgId = "goodspot123@spot.net";

        service.AddSpotBlack(msgId);
        Assert.Contains(msgId, service.BlacklistMsgIds);
        Assert.True(service.IsBlacklisted(null, msgId));

        service.AddSpotWhite(goodMsgId);
        Assert.Contains(goodMsgId, service.WhitelistMsgIds);
        Assert.True(service.IsWhitelisted(null, goodMsgId));

        service.RemoveSpotBlack(msgId);
        Assert.DoesNotContain(msgId, service.BlacklistMsgIds);

        service.RemoveSpotWhite(goodMsgId);
        Assert.DoesNotContain(goodMsgId, service.WhitelistMsgIds);
    }

    [Fact]
    public void GetPosterIdent_EvaluatesPrioritiesCorrectly()
    {
        using var service = new TrustService(_paths, _prefsService);

        string blackMod = "BLACK_MODULUS";
        string whiteMod = "WHITE_MODULUS";
        string blackMsg = "black_msg@spot.net";
        string whiteMsg = "white_msg@spot.net";

        service.AddBlack("BadUser", blackMod);
        service.AddWhite("GoodUser", whiteMod);
        service.AddSpotBlack(blackMsg);
        service.AddSpotWhite(whiteMsg);

        // 1. Blacklist takes precedence
        Assert.Equal(PosterIdentType.Black, service.GetPosterIdent(blackMod, "BadUser", "any@spot.net", 1_700_000_000));

        // 2. Spot blacklist
        Assert.Equal(PosterIdentType.SpotBlack, service.GetPosterIdent("SOME_MOD", "NormalUser", blackMsg, 1_700_000_000));

        // 3. Whitelist modulus
        Assert.Equal(PosterIdentType.White, service.GetPosterIdent(whiteMod, "GoodUser", "any@spot.net", 1_700_000_000));

        // 4. Pre-2013 spots without key are Verified
        Assert.Equal(PosterIdentType.Verified, service.GetPosterIdent(null, "OldUser", "old@spot.net", 1_300_000_000));

        // 5. Spot whitelist
        Assert.Equal(PosterIdentType.SpotWhite, service.GetPosterIdent("OTHER_MOD", "OtherUser", whiteMsg, 1_700_000_000));

        // 6. Regular spot with key is None
        Assert.Equal(PosterIdentType.None, service.GetPosterIdent("NORMAL_MOD", "NormalUser", "normal@spot.net", 1_700_000_000));
    }

    [Fact]
    public void ServerWhitelist_DetectsFakes_WhenModulusDoesNotMatch()
    {
        // Write a mock whitelist.srv.csv
        string serverWhiteCsv = Path.Combine(_tempFolder, "whitelist.srv.csv");
        File.WriteAllText(serverWhiteCsv, "TrustedArtist,ARTIST_REAL_MODULUS\n");

        using var service = new TrustService(_paths, _prefsService);

        // Imposter using same name but different key
        var fakeIdent = service.GetPosterIdent("IMPOSTER_KEY", "TrustedArtist", "fake@spot.net", 1_700_000_000);
        Assert.Equal(PosterIdentType.Fake, fakeIdent);

        // Real artist with real key
        var realIdent = service.GetPosterIdent("ARTIST_REAL_MODULUS", "TrustedArtist", "real@spot.net", 1_700_000_000);
        Assert.Equal(PosterIdentType.Verified, realIdent);
    }

    [Fact]
    public async Task SyncToDatabaseAsync_PopulatesDatabaseAndFiltersQueries()
    {
        await _dbService.EnsureCreatedAsync();

        using var service = new TrustService(_paths, _prefsService);
        string blackMod = "BLOCKED_POSTER_MOD";
        string whiteMod = "TRUSTED_POSTER_MOD";
        string normalMod = "REGULAR_POSTER_MOD";

        service.AddBlack("BadUser", blackMod);
        service.AddWhite("GoodUser", whiteMod);

        await service.SyncToDatabaseAsync(_dbService);

        // Insert spots
        var s1 = new SpotItem { Key = 1, Category = 1, Modulus = blackMod, Sender = "BadUser", Subject = "Spam Spot", MsgId = "spam@spot.net", Date = 1_700_000_000 };
        var s2 = new SpotItem { Key = 1, Category = 1, Modulus = whiteMod, Sender = "GoodUser", Subject = "Good Spot", MsgId = "good@spot.net", Date = 1_700_000_000 };
        var s3 = new SpotItem { Key = 1, Category = 1, Modulus = normalMod, Sender = "Normie", Subject = "Normal Spot", MsgId = "norm@spot.net", Date = 1_700_000_000 };
        await _dbService.InsertSpotsAsync(new[] { s1, s2, s3 });

        // Query unfiltered
        var all = await _dbService.QueryByFilterAsync("cat=1");
        Assert.Equal(3, all.Count);

        // Query with hideBlacklisted
        var noBlack = await _dbService.QueryByFilterAsync("cat=1", hideBlacklisted: true);
        Assert.Equal(2, noBlack.Count);
        Assert.DoesNotContain(noBlack, s => s.Modulus == blackMod);

        // Count with hideBlacklisted
        int countNoBlack = await _dbService.CountByFilterAsync("cat=1", hideBlacklisted: true);
        Assert.Equal(2, countNoBlack);

        // Query with showTrustedOnly
        var trustedOnly = await _dbService.QueryByFilterAsync("cat=1", showTrustedOnly: true);
        Assert.Single(trustedOnly);
        Assert.Equal(whiteMod, trustedOnly[0].Modulus);

        int countTrustedOnly = await _dbService.CountByFilterAsync("cat=1", showTrustedOnly: true);
        Assert.Equal(1, countTrustedOnly);
    }

    [Fact]
    public async Task FilterQueryBuilder_HandlesPosterIdentInSyntax()
    {
        await _dbService.EnsureCreatedAsync();

        using var service = new TrustService(_paths, _prefsService);
        service.AddWhite("Whitelisted", "MOD_WHITE");
        service.AddBlack("Blacklisted", "MOD_BLACK");
        await service.SyncToDatabaseAsync(_dbService);

        await _dbService.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Modulus = "MOD_WHITE", Subject = "White Spot", MsgId = "w@spot.net", Date = 1_700_000_000 },
            new SpotItem { Key = 1, Category = 1, Modulus = "MOD_BLACK", Subject = "Black Spot", MsgId = "b@spot.net", Date = 1_700_000_000 },
            new SpotItem { Key = 1, Category = 1, Modulus = "MOD_OTHER", Subject = "Other Spot", MsgId = "o@spot.net", Date = 1_700_000_000 }
        });

        // Filter: PosterIdent IN (W)
        var whiteOnly = await _dbService.QueryByFilterAsync("PosterIdent IN (W)");
        Assert.Single(whiteOnly);
        Assert.Equal("MOD_WHITE", whiteOnly[0].Modulus);

        // Filter: PosterIdent IN (B)
        var blackOnly = await _dbService.QueryByFilterAsync("PosterIdent IN (B)");
        Assert.Single(blackOnly);
        Assert.Equal("MOD_BLACK", blackOnly[0].Modulus);
    }

    private sealed class TestAppPaths : IAppPaths
    {
        public string DataFolder { get; }
        public string CacheFolder => DataFolder;
        public string LogsFolder => DataFolder;
        public string FiltersFolder => DataFolder;
        public string DownloadsFolder => DataFolder;
        public string TempFolder => DataFolder;
        public string GetDatabasePath(string name) => Path.Combine(DataFolder, $"{name}.db");
        public string GetTempFileName(string ext = null!, string filename = null!) =>
            Path.Combine(DataFolder, (filename ?? Guid.NewGuid().ToString("N")) + (ext ?? ".tmp"));
        public void EnsureDirectoriesExist() { }

        public TestAppPaths(string folder)
        {
            DataFolder = folder;
        }
    }
}
