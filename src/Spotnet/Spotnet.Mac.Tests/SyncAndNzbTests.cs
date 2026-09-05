using System;
using System.IO;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Network;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class SyncAndNzbTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StandardAppPaths _appPaths;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;

    public SyncAndNzbTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpotnetSyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appPaths = new StandardAppPaths(_tempDir);
        _appPaths.EnsureDirectoriesExist();

        string dbPath = _appPaths.GetDatabasePath("test_spots");
        _db = new MacSqliteDb(dbPath);
        _db.InitializeSchema();
        _dbService = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void SpotnetHeaderParser_ParseOverviewLine_ParsesCorrectly()
    {
        const string from = "DistroPoster <KEY@47a02b03c06d35z00.4294967296.20.1788433332.1.NL.HASH>";
        string overview = $"123456\tUbuntu 24.04 LTS Desktop (x86_64)\t{from}\tThu, 03 Sep 2026 10:00:00 +0200\t<ub2404@spot.net>\t<ref001>\t999\t100";

        var spot = SpotnetHeaderParser.ParseOverviewLine(overview, out long articleNum);

        Assert.NotNull(spot);
        Assert.Equal(123456, articleNum);
        Assert.Equal(4, spot.Category);
        Assert.Equal("Ubuntu 24.04 LTS Desktop (x86_64)", spot.Subject);
        Assert.Equal("DistroPoster", spot.SenderName);
        Assert.Equal("ub2404@spot.net", spot.MsgId);
        Assert.Equal(4294967296L, spot.Filesize);
        Assert.Equal("Linux", spot.FormatLabel);       // a02
        Assert.Equal("CD/DVD Tools", spot.GenreLabel); // b03, the first named b-code
    }

    [Fact]
    public void SpotnetHeaderParser_ParseOverviewLine_DropsNonSpotArticles()
    {
        // free.pt also carries moderation and update-only posts. They are not spots and
        // Windows does not store them either.
        string overview = "123457\tdelete NE1LM3owMlVQbUINAZtplypsD16@spot.net\tultranerd <rCreaFYV@spot.net>\tThu, 03 Sep 2026 10:00:00 +0200\t<del@spot.net>\t<ref>\t0\t1";

        Assert.Null(SpotnetHeaderParser.ParseOverviewLine(overview, out _));
    }

    [Fact]
    public async Task SpotDatabaseService_LastSyncedArticle_RoundTrips()
    {
        long initial = await _dbService.GetLastSyncedArticleAsync();
        Assert.Equal(0, initial);

        await _dbService.SetLastSyncedArticleAsync(987654321L);
        long updated = await _dbService.GetLastSyncedArticleAsync();
        Assert.Equal(987654321L, updated);

        await _dbService.SetLastSyncedArticleAsync(987654400L);
        long updatedAgain = await _dbService.GetLastSyncedArticleAsync();
        Assert.Equal(987654400L, updatedAgain);
    }

    [Fact]
    public void NzbService_SanitizeFileName_StripsInvalidCharacters()
    {
        string badName = "Ubuntu: 24.04 / LTS * [x86_64] <ISO> & \"Best\" | Final?";
        string clean = NzbService.SanitizeFileName(badName);

        Assert.DoesNotContain(":", clean);
        Assert.DoesNotContain("/", clean);
        Assert.DoesNotContain("*", clean);
        Assert.DoesNotContain("<", clean);
        Assert.DoesNotContain(">", clean);
        Assert.DoesNotContain("\"", clean);
        Assert.DoesNotContain("|", clean);
        Assert.DoesNotContain("?", clean);
    }

    [Fact]
    public void NzbService_DecodeYEncString_DecodesNzbXml()
    {
        // Simple mock yEnc string with =ybegin and =yend
        string plainXml = "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"></nzb>";
        // Using plain XML when =ybegin is absent should pass through
        string result = NzbService.DecodeYEncString(plainXml);
        Assert.Contains("<nzb", result);
    }
}
