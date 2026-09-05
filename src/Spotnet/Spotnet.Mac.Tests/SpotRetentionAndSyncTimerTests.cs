using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public class SpotRetentionAndSyncTimerTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;
    private readonly string _tempDir;

    public SpotRetentionAndSyncTimerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spotnet_test_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbFile = Path.Combine(_tempDir, "spots.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetDatabaseStats_OnEmptyDatabase_ReturnsZeros()
    {
        await _service.EnsureCreatedAsync();

        var (min, max, count) = await _service.GetDatabaseStatsAsync();

        Assert.Equal(0, min);
        Assert.Equal(0, max);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetDatabaseStats_WithSpots_ReturnsAccurateMinMaxAndCount()
    {
        await _service.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var spots = new List<SpotItem>
        {
            new() { Key = 1, Category = 1, Date = now - 100, Subject = "Spot 1", MsgId = "msg1@test" },
            new() { Key = 2, Category = 1, Date = now - 50, Subject = "Spot 2", MsgId = "msg2@test" },
            new() { Key = 3, Category = 2, Date = now, Subject = "Spot 3", MsgId = "msg3@test" }
        };

        await _service.InsertSpotsAsync(spots);

        var (min, max, count) = await _service.GetDatabaseStatsAsync();

        Assert.True(min > 0);
        Assert.True(max >= min);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task UpdateDatabaseStats_UpdatesPreferencesSuccessfully()
    {
        await _service.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Date = now, Subject = "Spot 1", MsgId = "msg1@test" },
            new SpotItem { Key = 2, Category = 1, Date = now, Subject = "Spot 2", MsgId = "msg2@test" }
        });

        var appPaths = new TestAppPaths(_tempDir);
        var prefsService = new UserPreferencesService(appPaths);

        var stats = await _service.UpdateDatabaseStatsAsync(prefsService);

        Assert.Equal(2, stats.count);
        Assert.Equal(stats.min, prefsService.Current.DatabaseMin);
        Assert.Equal(stats.max, prefsService.Current.DatabaseMax);
        Assert.Equal(2, prefsService.Current.DatabaseCount);

        // Reload from disk to verify persistence
        var reloaded = prefsService.Load();
        Assert.Equal(2, reloaded.DatabaseCount);
        Assert.Equal(stats.min, reloaded.DatabaseMin);
        Assert.Equal(stats.max, reloaded.DatabaseMax);
    }

    [Fact]
    public async Task RemoveOutOfRetentionSpots_WhenDisabled_DoesNotDeleteAnySpots()
    {
        await _service.EnsureCreatedAsync();

        var oldDate = DateTimeOffset.UtcNow.AddDays(-100).ToUnixTimeSeconds();
        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Date = oldDate, Subject = "Old Spot", MsgId = "old@test" }
        });

        int removedNegative = await _service.RemoveOutOfRetentionSpotsAsync(-1);
        int removedZero = await _service.RemoveOutOfRetentionSpotsAsync(0);

        Assert.Equal(0, removedNegative);
        Assert.Equal(0, removedZero);

        var count = await _service.CountByFilterAsync(null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RemoveOutOfRetentionSpots_DeletesOnlyOldSpotsAndCleansFtsIndex()
    {
        await _service.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sixtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds();
        var tenDaysAgo = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();

        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 1, Category = 1, Date = sixtyDaysAgo, Subject = "OldMovie", MsgId = "old@test" },
            new SpotItem { Key = 7, Category = 1, Date = tenDaysAgo, Subject = "RecentMovie", MsgId = "recent@test" }
        });

        // 30 days retention: sixtyDaysAgo should be deleted, tenDaysAgo should be kept
        int removed = await _service.RemoveOutOfRetentionSpotsAsync(30);

        Assert.Equal(1, removed);

        var remaining = await _service.QueryByFilterAsync(null);
        Assert.Single(remaining);
        Assert.Equal("recent@test", remaining[0].MsgId);

        // Verify FTS5 search index is cleaned up
        var ftsOld = await _service.CountByFilterAsync(null, "OldMovie");
        var ftsRecent = await _service.CountByFilterAsync(null, "RecentMovie");
        Assert.Equal(0, ftsOld);
        Assert.Equal(1, ftsRecent);
    }

    [Fact]
    public async Task RemoveOutOfRetentionSpots_WithMoreThan2000Spots_LoopsInBatches()
    {
        await _service.EnsureCreatedAsync();

        var oldDate = DateTimeOffset.UtcNow.AddDays(-50).ToUnixTimeSeconds();
        var recentDate = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds();

        // Generate 2200 old spots and 2 recent spots to test the >2000 batching loop
        const int oldBatchCount = 2200;
        var oldSpots = new List<SpotItem>(oldBatchCount);
        for (int i = 0; i < oldBatchCount; i++)
        {
            oldSpots.Add(new SpotItem
            {
                Key = 1,
                Category = 1,
                Date = oldDate,
                Subject = $"Old Batch Spot {i}",
                MsgId = $"old_{i}@test"
            });
        }
        await _service.InsertSpotsAsync(oldSpots);

        await _service.InsertSpotsAsync(new[]
        {
            new SpotItem { Key = 7, Category = 1, Date = recentDate, Subject = "Recent 1", MsgId = "recent1@test" },
            new SpotItem { Key = 3, Category = 1, Date = recentDate, Subject = "Recent 2", MsgId = "recent2@test" }
        });

        int totalBefore = await _service.CountByFilterAsync(null);
        Assert.Equal(oldBatchCount + 2, totalBefore);

        // Retention of 30 days removes all 2200 old spots in 2 batches (2000 then 200)
        int removed = await _service.RemoveOutOfRetentionSpotsAsync(30);

        Assert.Equal(oldBatchCount, removed);

        int totalAfter = await _service.CountByFilterAsync(null);
        Assert.Equal(2, totalAfter);
    }

    [Fact]
    public void UserPreferencesService_PersistsAndLoadsAutoSyncAndRetentionSettings()
    {
        var appPaths = new TestAppPaths(_tempDir);
        var prefsService = new UserPreferencesService(appPaths);

        // Defaults
        Assert.True(prefsService.Current.DbAutoUpdateEnabled);
        Assert.Equal(10, prefsService.Current.DbAutoUpdateIntervalMin);
        Assert.Equal(-1, prefsService.Current.Retention);

        // Modify and save
        var prefs = prefsService.Current;
        prefs.DbAutoUpdateEnabled = false;
        prefs.DbAutoUpdateIntervalMin = 25;
        prefs.Retention = 45;
        prefs.DatabaseMin = 100;
        prefs.DatabaseMax = 5000;
        prefs.DatabaseCount = 4900;
        prefsService.Save(prefs);

        // Reload in new instance
        var reloadedService = new UserPreferencesService(appPaths);
        Assert.False(reloadedService.Current.DbAutoUpdateEnabled);
        Assert.Equal(25, reloadedService.Current.DbAutoUpdateIntervalMin);
        Assert.Equal(45, reloadedService.Current.Retention);
        Assert.Equal(100, reloadedService.Current.DatabaseMin);
        Assert.Equal(5000, reloadedService.Current.DatabaseMax);
        Assert.Equal(4900, reloadedService.Current.DatabaseCount);
    }

    private sealed class TestAppPaths : IAppPaths
    {
        public string AppFolder => _baseDir;
        public string DataFolder => _baseDir;
        public string CacheFolder => Path.Combine(_baseDir, "cache");
        public string LogsFolder => Path.Combine(_baseDir, "logs");
        public string FiltersFolder => Path.Combine(_baseDir, "filters");
        public string DownloadsFolder => Path.Combine(_baseDir, "downloads");
        public string TempFolder => Path.Combine(_baseDir, "temp");

        private readonly string _baseDir;
        public TestAppPaths(string baseDir) => _baseDir = baseDir;

        public void EnsureDirectoriesExist() => Directory.CreateDirectory(_baseDir);
        public string GetDatabasePath(string name) => Path.Combine(_baseDir, $"{name}.db");
        public string GetTempFileName(string? ext = null, string? filename = null) =>
            Path.Combine(TempFolder, filename ?? $"{Guid.NewGuid():N}.{(ext ?? "tmp").TrimStart('.')}");
    }
}
