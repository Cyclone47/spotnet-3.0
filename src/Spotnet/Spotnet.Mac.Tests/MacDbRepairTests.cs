using System;
using System.IO;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacDbRepairTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public MacDbRepairTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"spotnet_repair_test_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbPath);
        _db.InitializeSchema();
        _service = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            string wal = _tempDbPath + "-wal";
            string shm = _tempDbPath + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task QuickRepairAsync_CheckpointsAndValidatesDatabase()
    {
        // Insert a spot
        var spot = new SpotItem
        {
            Subject = "Test Repair Spot",
            MsgId = "test-repair-123@spotnet",
            Sender = "Spotter",
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Filesize = 1024 * 1024,
            Category = 1
        };
        await _service.InsertSpotsAsync(new[] { spot });

        // Run repair
        var (success, message) = await _service.QuickRepairAsync();

        Assert.True(success);
        Assert.Contains("succesvol", message);

        // Verify spot is still queryable via FTS
        var queried = await _service.QuerySpotsAsync(ftsQuery: "Repair");
        Assert.Single(queried);
        Assert.Equal("Test Repair Spot", queried[0].Subject);
    }
}
