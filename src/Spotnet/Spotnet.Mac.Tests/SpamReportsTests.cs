using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

public class SpamReportsTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public SpamReportsTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_spam_test_{Guid.NewGuid():N}.db");
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
    public void SpamReportParser_ValidOverviewLine_ParsesCorrectly()
    {
        // Overview line format: ArticleNum \t Subject \t From \t Date \t MsgId \t References \t Bytes \t Lines
        string line = "12345\tREPORT <target-spot@spot.net> virussen\tReporter <abc1234567.u1-s2-p3.signature>\t05 Sep 2026 12:00:00 UTC\t<report-msg@spot.net>\t\t100\t10";

        var item = SpamReportParser.ParseOverviewLine(line);

        Assert.NotNull(item);
        Assert.Equal(12345, item.RowId);
        Assert.Equal("target-spot@spot.net", item.MsgId);
        Assert.Equal("Reporter", item.Sender);
        Assert.Equal("report-msg@spot.net", item.ReportMsgId);
        Assert.False(string.IsNullOrEmpty(item.Modulus));
    }

    [Fact]
    public void SpamReportParser_InvalidSubject_ReturnsNull()
    {
        string line = "12345\tNiet een report\tReporter <user@host>\t05 Sep 2026 12:00:00 UTC\t<msg@spot.net>";
        var item = SpamReportParser.ParseOverviewLine(line);
        Assert.Null(item);
    }

    [Fact]
    public void SpamReportParser_MalformedLine_ReturnsNull()
    {
        Assert.Null(SpamReportParser.ParseOverviewLine(""));
        Assert.Null(SpamReportParser.ParseOverviewLine("123\tonly\ttwo"));
    }

    [Fact]
    public async Task InsertSpamReports_InsertsAndAggregatesInSpamGroup()
    {
        await _service.EnsureCreatedAsync();

        var reports = new[]
        {
            new SpamReportItem
            {
                RowId = 1,
                MsgId = "spot1@test",
                Modulus = "modulusA",
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ReportMsgId = "rep1@test",
                Sender = "User1"
            },
            new SpamReportItem
            {
                RowId = 2,
                MsgId = "spot1@test",
                Modulus = "modulusB",
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ReportMsgId = "rep2@test",
                Sender = "User2"
            }
        };

        int inserted = await _service.InsertSpamReportsAsync(reports);
        Assert.Equal(2, inserted);

        int count = await _service.GetSpamReportCountAsync("spot1@test");
        Assert.Equal(2, count);

        var list = await _service.GetSpamReportsAsync("spot1@test");
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task InsertSpamReports_DeduplicatesSameModulusForSameSpot()
    {
        await _service.EnsureCreatedAsync();

        var report1 = new SpamReportItem
        {
            RowId = 10,
            MsgId = "spot1@test",
            Modulus = "modulusSame",
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ReportMsgId = "rep10@test",
            Sender = "UserSame"
        };

        var report2 = new SpamReportItem
        {
            RowId = 11,
            MsgId = "spot1@test",
            Modulus = "modulusSame",
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ReportMsgId = "rep11@test",
            Sender = "UserSame"
        };

        await _service.InsertSpamReportsAsync(new[] { report1 });
        int insertedSecond = await _service.InsertSpamReportsAsync(new[] { report2 });

        // Second report from identical modulus for the same spot should be skipped
        Assert.Equal(0, insertedSecond);

        int count = await _service.GetSpamReportCountAsync("spot1@test");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SpamReportWatermark_CanBeSavedAndRetrieved()
    {
        await _service.EnsureCreatedAsync();

        long initial = await _service.GetLastIndexedSpamReportAsync();
        Assert.Equal(0, initial);

        await _service.SetLastIndexedSpamReportAsync(98765);
        long updated = await _service.GetLastIndexedSpamReportAsync();
        Assert.Equal(98765, updated);
    }

    [Fact]
    public async Task QueryByFilter_FiltersOutSpotsExceedingSpamThreshold()
    {
        await _service.EnsureCreatedAsync();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var cleanSpot = new SpotItem
        {
            Key = 1,
            Category = 1,
            Subject = "Clean Movie",
            Sender = "PosterA",
            Cats = "1a01",
            Date = now - 10,
            Filesize = 1_000_000_000,
            MsgId = "clean@test"
        };

        var spamSpot = new SpotItem
        {
            Key = 1,
            Category = 1,
            Subject = "Spam Movie",
            Sender = "PosterB",
            Cats = "1a01",
            Date = now - 5,
            Filesize = 1_000_000_000,
            MsgId = "spam@test"
        };

        await _service.InsertSpotsAsync(new[] { cleanSpot, spamSpot });

        // Add 5 reports to spamSpot
        var reports = Enumerable.Range(1, 5).Select(i => new SpamReportItem
        {
            RowId = 100 + i,
            MsgId = "spam@test",
            Modulus = $"modulus_{i}",
            Date = now,
            ReportMsgId = $"rep_{i}@test",
            Sender = $"Reporter_{i}"
        }).ToList();

        await _service.InsertSpamReportsAsync(reports);

        // Verify spam count in database
        Assert.Equal(5, await _service.GetSpamReportCountAsync("spam@test"));
        Assert.Equal(0, await _service.GetSpamReportCountAsync("clean@test"));

        // Query with threshold 5: spamSpot (5 reports) should be hidden, cleanSpot (0 reports) shown
        var filteredSpots = await _service.QueryByFilterAsync("cat=1", spamReportsThreshold: 5);
        Assert.Single(filteredSpots);
        Assert.Equal("clean@test", filteredSpots[0].MsgId);
        Assert.Equal(0, filteredSpots[0].NumberOfSpamReports);

        int filteredCount = await _service.CountByFilterAsync("cat=1", spamReportsThreshold: 5);
        Assert.Equal(1, filteredCount);

        // Query with threshold 0 or -1: both spots should be visible
        var allSpots = await _service.QueryByFilterAsync("cat=1", spamReportsThreshold: 0);
        Assert.Equal(2, allSpots.Count);

        var spamSpotLoaded = allSpots.First(s => s.MsgId == "spam@test");
        Assert.Equal(5, spamSpotLoaded.NumberOfSpamReports);
        Assert.True(spamSpotLoaded.HasSpamReports);

        // GetSpotByMsgIdAsync loads spam count
        var singleSpot = await _service.GetSpotByMsgIdAsync("spam@test");
        Assert.NotNull(singleSpot);
        Assert.Equal(5, singleSpot.NumberOfSpamReports);
    }
}
