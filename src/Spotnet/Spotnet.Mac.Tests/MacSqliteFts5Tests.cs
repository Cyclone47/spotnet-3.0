using System;
using System.IO;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacSqliteFts5Tests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _service;

    public MacSqliteFts5Tests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"spotnet_test_{Guid.NewGuid():N}.db");
        _db = new MacSqliteDb(_tempDbFile);
        _service = new SpotDatabaseService(_db);
    }

    [Fact]
    public async Task SchemaInitialization_CreatesFts5SearchTable()
    {
        // Act
        await _service.EnsureCreatedAsync();

        // Assert: query sqlite_master for search table
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name='search';";
        var sql = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(sql);
        Assert.Contains("fts5", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InsertAndSearch_UsingFts5Match_ReturnsMatchingSpots()
    {
        // Arrange
        await _service.EnsureCreatedAsync();

        var spot1 = new SpotItem
        {
            Key = 1,
            Category = 1, // Beeld
            Subject = "Inception 2010 1080p BluRay Remux",
            Sender = "MoviePoster",
            Tag = "NL",
            Cats = "1a03",
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Filesize = 15_000_000_000,
            MsgId = "spot1@spot.net"
        };

        var spot2 = new SpotItem
        {
            Key = 2,
            Category = 4, // Applicaties
            Subject = "Ubuntu Linux 24.04 LTS Desktop ISO",
            Sender = "LinuxFan",
            Tag = "OS",
            Cats = "4a02",
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100,
            Filesize = 4_000_000_000,
            MsgId = "spot2@spot.net"
        };

        await _service.InsertSpotsAsync(new[] { spot1, spot2 });

        // Act 1: Search for Inception
        var resultsInception = await _service.QuerySpotsAsync(ftsQuery: "Inception");
        Assert.Single(resultsInception);
        Assert.Equal("spot1@spot.net", resultsInception[0].MsgId);

        // Act 2: Search for Ubuntu
        var resultsUbuntu = await _service.QuerySpotsAsync(ftsQuery: "Ubuntu");
        Assert.Single(resultsUbuntu);
        Assert.Equal("spot2@spot.net", resultsUbuntu[0].MsgId);

        // Act 3: Search with category filter
        var resultsCat = await _service.QuerySpotsAsync(categoryId: 1);
        Assert.Single(resultsCat);
        Assert.Equal(1, resultsCat[0].Category);

        // Act 4: Total count
        int total = await _service.CountSpotsAsync();
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task Comments_CanBeInsertedAndRetrieved()
    {
        await _service.EnsureCreatedAsync();

        var comment = new CommentItem
        {
            MsgId = "c1@spot.net",
            SpotMsgId = "spot1@spot.net",
            Sender = "Commenter",
            Rating = 9,
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Body = "Geweldige film, bedankt voor het spotten!"
        };

        await _service.InsertCommentsAsync(new[] { comment });

        var retrieved = await _service.GetCommentsAsync("spot1@spot.net");
        Assert.Single(retrieved);
        Assert.Equal("Commenter", retrieved[0].Sender);
        Assert.Equal(9, retrieved[0].Rating);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (File.Exists(_tempDbFile)) File.Delete(_tempDbFile);
            if (File.Exists(_tempDbFile + "-wal")) File.Delete(_tempDbFile + "-wal");
            if (File.Exists(_tempDbFile + "-shm")) File.Delete(_tempDbFile + "-shm");
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
