using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Platform;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class CommentServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StandardAppPaths _appPaths;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;
    private readonly CommentService _commentService;

    public CommentServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpotnetCommentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appPaths = new StandardAppPaths(_tempDir);
        _appPaths.EnsureDirectoriesExist();

        string dbPath = _appPaths.GetDatabasePath("test_spots");
        _db = new MacSqliteDb(dbPath);
        _db.InitializeSchema();
        _dbService = new SpotDatabaseService(_db);

        _commentService = new CommentService(_appPaths, new MacKeychainSecretStore(), _dbService);
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
    public async Task SpotDatabaseService_UserKeyXml_RoundTrips()
    {
        string? initialKey = await _dbService.GetUserKeyXmlAsync();
        Assert.Null(initialKey);

        using var rsa = RSA.Create(1024);
        string exportedXml = rsa.ToXmlString(true);

        await _dbService.SetUserKeyXmlAsync(exportedXml);
        string? storedXml = await _dbService.GetUserKeyXmlAsync();

        Assert.NotNull(storedXml);
        Assert.Equal(exportedXml, storedXml);
    }

    [Fact]
    public async Task CommentService_Validation_RejectsInvalidInput()
    {
        var spot = new SpotItem { MsgId = "test1234@spot.net", Subject = "Test Spot" };

        // The wording matches the Windows resources.

        // Empty comment
        var (s1, _, m1) = await _commentService.PostCommentAsync(spot, "ValidUser", "");
        Assert.False(s1);
        Assert.Equal("Vul een reactie in.", m1);

        // Comment too short (< 3)
        var (s2, _, m2) = await _commentService.PostCommentAsync(spot, "ValidUser", "hi");
        Assert.False(s2);
        Assert.Equal("Reactie is te kort.", m2);

        // No sender
        var (s3, _, m3) = await _commentService.PostCommentAsync(spot, "  ", "Dit is een geldige reactie!");
        Assert.False(s3);
        Assert.Equal("Afzender niet ingevuld.", m3);

        // Comment too long (> 900)
        string tooLongComment = new string('x', 901);
        var (s4, _, m4) = await _commentService.PostCommentAsync(spot, "ValidUser", tooLongComment);
        Assert.False(s4);
        Assert.Equal("Reactie is te lang.", m4);
    }

    [Fact]
    public void SpotDetailViewModel_PostComment_InitializesDefaults()
    {
        var vm = new SpotDetailViewModel(_dbService, null, _commentService);

        Assert.Equal("Spotter", vm.SenderNickname);
        Assert.Equal("", vm.NewCommentText);
        Assert.False(vm.IsPostingComment);
        Assert.NotNull(vm.PostCommentCommand);
    }
}
