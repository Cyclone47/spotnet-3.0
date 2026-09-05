using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Helpers;
using Spotnet.Model;
using Xunit;

namespace Spotnet.Tests;

public sealed class JsonSpotCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SpotnetJsonCacheTests-" + Guid.NewGuid().ToString("N"));
    private string DirectoryPath => Path.Combine(root, "Cache", "Json-v1");

    [Fact]
    public void ReopenedCachePreservesDetailsImageAndNestedFields()
    {
        var spot = new SpotEx {
            MessageId = "../../test@spot", Article = 123, Filesize = 9000000000,
            Title = "Nederlandse titel é", Body = "<p>Details</p>", NZB = "nzb@test",
            ImageSource = new byte[] { 0, 1, 255 }, Modulus = "not-a-real-key", Poster = "author",
            User = new UserInfo { Organisation = "community", ValidSignature = true },
            OldInfo = new FTDInfo { Groups = "group", FileName = "file" }
        };
        new JsonSpotCache(root).Save(spot);
        var loaded = new JsonSpotCache(root).Get(spot.MessageId);
        Assert.NotNull(loaded);
        Assert.Equal(spot.Article, loaded.Article);
        Assert.Equal(spot.Filesize, loaded.Filesize);
        Assert.Equal(spot.Title, loaded.Title);
        Assert.Equal(spot.Body, loaded.Body);
        Assert.Equal(spot.NZB, loaded.NZB);
        Assert.Equal(spot.ImageSource, loaded.ImageSource);
        Assert.Equal("community", loaded.User.Organisation);
        Assert.True(loaded.User.ValidSignature);
        Assert.Equal("file", loaded.OldInfo.FileName);
        string path = Assert.Single(Directory.GetFiles(DirectoryPath));
        Assert.Equal(64 + 5, Path.GetFileName(path).Length);
        Assert.DoesNotContain("PosterIdent", File.ReadAllText(path));
    }

    [Fact]
    public void PartialUpdatesRetainPreviouslyLoadedBodyAndImage()
    {
        var cache = new JsonSpotCache(root);
        cache.Save(new SpotEx { MessageId = "id", Body = "body" });
        cache.Save(new SpotEx { MessageId = "id", ImageSource = new byte[] { 7 } });
        cache.Save(new SpotEx { MessageId = "id", Title = "new title" });
        var loaded = new JsonSpotCache(root).Get("id");
        Assert.Equal("body", loaded.Body);
        Assert.Equal(new byte[] { 7 }, loaded.ImageSource);
        Assert.Equal("new title", loaded.Title);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Version\":2,\"Spot\":{\"MessageId\":\"id\"}}")]
    [InlineData("{\"Version\":1,\"Spot\":{\"MessageId\":\"wrong\"}}")]
    public void InvalidCacheIsAMissAndCanBeReplaced(string invalid)
    {
        var cache = new JsonSpotCache(root);
        cache.Save(new SpotEx { MessageId = "id", Body = "initial" });
        File.WriteAllText(Assert.Single(Directory.GetFiles(DirectoryPath)), invalid);
        Assert.Null(cache.Get("id"));
        cache.Save(new SpotEx { MessageId = "id", Body = "recovered" });
        Assert.Equal("recovered", cache.Get("id").Body);
    }

    [Fact]
    public void LegacyFilesAndUnrelatedProfileDataAreIgnored()
    {
        Directory.CreateDirectory(Path.Combine(root, "Cache"));
        string legacy = Path.Combine(root, "Cache", "Spotid.cache");
        File.WriteAllText(legacy, "legacy binary placeholder");
        new JsonSpotCache(root).Save(new SpotEx { MessageId = "id", Body = "fresh" });
        Assert.Equal("legacy binary placeholder", File.ReadAllText(legacy));
    }

    [Fact]
    public void SizeBudgetEvictsOldEntriesAndRejectsOversizedEntry()
    {
        var cache = new JsonSpotCache(root, 2500);
        cache.Save(new SpotEx { MessageId = "old", Body = new string('a', 800) });
        Assert.NotNull(cache.Get("old"));
        cache.Save(new SpotEx { MessageId = "new", Body = new string('b', 800) });
        Assert.NotNull(cache.Get("new"));
        Assert.Null(cache.Get("old"));
        Assert.True(new DirectoryInfo(DirectoryPath).GetFiles("*.json").Sum(f => f.Length) <= 2500);
        cache.Save(new SpotEx { MessageId = "huge", Body = new string('x', 3000) });
        Assert.Null(cache.Get("huge"));
        Assert.NotNull(cache.Get("new"));
    }

    [Fact]
    public void UnwritableCacheDoesNotBreakSpotRetrieval()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Cache"), "obstruct directory");
        var cache = new JsonSpotCache(root);
        cache.Save(new SpotEx { MessageId = "id", Body = "body" });
        Assert.Null(cache.Get("id"));
    }

    [Fact]
    public void ConcurrentWritersPublishWholeEntries()
    {
        Parallel.For(0, 30, i => new JsonSpotCache(root).Save(new SpotEx {
            MessageId = "id", Title = i.ToString(), Body = i.ToString()
        }));
        var loaded = new JsonSpotCache(root).Get("id");
        Assert.Equal(loaded.Title, loaded.Body);
        Assert.Single(Directory.GetFiles(DirectoryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
