using System;
using System.IO;
using System.Security.Cryptography;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class ComplaintServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StandardAppPaths _appPaths;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;

    public ComplaintServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpotnetComplaintTest_" + Guid.NewGuid().ToString("N"));
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

    // -----------------------------------------------------------------------
    // ValidateDescription
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateDescription_Empty_ReturnsFalse()
    {
        var (valid, error) = ComplaintService.ValidateDescription("");
        Assert.False(valid);
        Assert.Equal("Vul een beschrijving in.", error);
    }

    [Fact]
    public void ValidateDescription_Null_ReturnsFalse()
    {
        var (valid, error) = ComplaintService.ValidateDescription(null);
        Assert.False(valid);
        Assert.Equal("Vul een beschrijving in.", error);
    }

    [Fact]
    public void ValidateDescription_TooShort_ReturnsFalse()
    {
        var (valid, error) = ComplaintService.ValidateDescription("ab");
        Assert.False(valid);
        Assert.Equal("Beschrijving is te kort.", error);
    }

    [Fact]
    public void ValidateDescription_TooLong_ReturnsFalse()
    {
        string tooLong = new string('x', 901);
        var (valid, error) = ComplaintService.ValidateDescription(tooLong);
        Assert.False(valid);
        Assert.Equal("Beschrijving is te lang.", error);
    }

    [Fact]
    public void ValidateDescription_Valid_ReturnsTrue()
    {
        var (valid, error) = ComplaintService.ValidateDescription("Spam post, nep links");
        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateDescription_ExactlyThreeChars_IsValid()
    {
        var (valid, _) = ComplaintService.ValidateDescription("abc");
        Assert.True(valid);
    }

    [Fact]
    public void ValidateDescription_Exactly900Chars_IsValid()
    {
        var (valid, _) = ComplaintService.ValidateDescription(new string('y', 900));
        Assert.True(valid);
    }

    // -----------------------------------------------------------------------
    // CleanNickName
    // -----------------------------------------------------------------------

    [Fact]
    public void CleanNickName_AlphanumericOnly_ReturnsUnchanged()
    {
        Assert.Equal("Spotter42", ComplaintService.CleanNickName("Spotter42"));
    }

    [Fact]
    public void CleanNickName_StripsSpecialChars()
    {
        Assert.Equal("JanDeVries", ComplaintService.CleanNickName("Jan De-Vries!@#"));
    }

    [Fact]
    public void CleanNickName_TooShort_FallsBackToSpotter()
    {
        Assert.Equal("Spotter", ComplaintService.CleanNickName("AB"));
    }

    [Fact]
    public void CleanNickName_Empty_FallsBackToSpotter()
    {
        Assert.Equal("Spotter", ComplaintService.CleanNickName(""));
    }

    [Fact]
    public void CleanNickName_Null_FallsBackToSpotter()
    {
        Assert.Equal("Spotter", ComplaintService.CleanNickName(null));
    }

    [Fact]
    public void CleanNickName_Whitespace_FallsBackToSpotter()
    {
        Assert.Equal("Spotter", ComplaintService.CleanNickName("   "));
    }

    // -----------------------------------------------------------------------
    // BuildReportHeaders
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildReportHeaders_SubjectFormat_MatchesWindows()
    {
        using var rsa = RSA.Create(2048);
        var spot = new SpotItem { MsgId = "<test123@spot.net>", Subject = "Test Spot Title" };

        var (subject, from, references, extraHeaders) = ComplaintService.BuildReportHeaders(spot, "Tester", rsa);

        Assert.StartsWith("REPORT <test123@spot.net> - ", subject);
        Assert.Contains("Test Spot Title", subject);
    }

    [Fact]
    public void BuildReportHeaders_References_ContainsTargetMsgId()
    {
        using var rsa = RSA.Create(2048);
        var spot = new SpotItem { MsgId = "<abc@spot.net>", Subject = "Title" };

        var (_, _, references, _) = ComplaintService.BuildReportHeaders(spot, "Nick", rsa);

        Assert.Equal("<abc@spot.net>", references);
    }

    [Fact]
    public void BuildReportHeaders_From_ContainsNickAndModulus()
    {
        using var rsa = RSA.Create(2048);
        var spot = new SpotItem { MsgId = "<abc@spot.net>", Subject = "Title" };

        var (_, from, _, _) = ComplaintService.BuildReportHeaders(spot, "TestUser", rsa);

        Assert.StartsWith("TestUser <", from);
        Assert.EndsWith("@spot.net>", from);
        // Modulus should be present (escaped) in the from address
        var rsaParams = rsa.ExportParameters(false);
        string modulusEscaped = PosterIdentity.Escape(Convert.ToBase64String(rsaParams.Modulus!));
        // Hash10 + "." + modulus + "." + hashSig — check modulus part is there
        Assert.Contains(modulusEscaped, from);
    }

    [Fact]
    public void BuildReportHeaders_ExtraHeaders_ContainsRequiredFields()
    {
        using var rsa = RSA.Create(2048);
        var spot = new SpotItem { MsgId = "<abc@spot.net>", Subject = "Title" };

        var (_, _, _, extraHeaders) = ComplaintService.BuildReportHeaders(spot, "Nick", rsa);

        Assert.Contains("X-No-Archive: yes", extraHeaders);
        Assert.Contains("X-User-Signature:", extraHeaders);
        Assert.Contains("X-User-Key:", extraHeaders);
    }

    // -----------------------------------------------------------------------
    // PosterIdentity.Escape / Unescape roundtrip
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("abc/def+ghi=")]     // length 12 → padding "=" is correct
    [InlineData("AQAB")]             // length 4, no padding needed
    [InlineData("/+++//==")]        // length 8 → padding "==" is correct (6 chars + 2 padding)
    [InlineData("")]
    [InlineData("YQ==")]             // standard base64 for "a"
    [InlineData("YWI=")]             // standard base64 for "ab"
    public void Escape_Unescape_Roundtrips(string original)
    {
        string escaped = PosterIdentity.Escape(original);
        string unescaped = PosterIdentity.Unescape(escaped);
        Assert.Equal(original, unescaped);
    }

    [Fact]
    public void Escape_ReplacesSlashAndPlus_StripsEquals()
    {
        string result = PosterIdentity.Escape("a/b+c=d==");
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("+", result);
        Assert.DoesNotContain("=", result);
        Assert.Contains("-s", result); // replaced /
        Assert.Contains("-p", result); // replaced +
    }

    // -----------------------------------------------------------------------
    // ComplaintViewModel defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void ComplaintViewModel_InitializesWithDefaults()
    {
        var spot = new SpotItem { MsgId = "<test@spot.net>", Subject = "Test" };
        var prefsService = new UserPreferencesService(_appPaths);
        var trustService = new TrustService(_appPaths, prefsService);
        var service = new ComplaintService(
            _appPaths, new MacKeychainSecretStore(), _dbService,
            prefsService, trustService);

        var vm = new Spotnet.Mac.ViewModels.ComplaintViewModel(spot, service);

        Assert.Equal("", vm.Reason);
        Assert.True(vm.AddToBlacklist);
        Assert.NotNull(vm.SubmitCommand);
        Assert.NotNull(vm.CancelCommand);
    }
}
