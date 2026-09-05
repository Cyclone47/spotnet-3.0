using System;
using System.IO;
using System.Linq;
using Spotnet.Remote;
using Xunit;

namespace Spotnet.Tests;

[CollectionDefinition("Remote configuration", DisableParallelization = true)]
public class RemoteConfigurationCollection { }

[Collection("Remote configuration")]
public class SpotnetRemoteTests : IDisposable
{
    private readonly string previousFolder = Spotnet.Helpers.AppHelper.SettingsFolder;
    private readonly RemoteConfig previousConfig = RemoteAuthManager.Instance.Config;
    private readonly string testFolder = Path.Combine(Path.GetTempPath(), "SpotnetRemoteTests-" + Guid.NewGuid().ToString("N"));

    public SpotnetRemoteTests()
    {
        Directory.CreateDirectory(testFolder);
        Spotnet.Helpers.AppHelper.SettingsFolder = testFolder;
        RemoteAuthManager.Instance.Config = new RemoteConfig();
    }

    public void Dispose()
    {
        Spotnet.Helpers.AppHelper.SettingsFolder = previousFolder;
        RemoteAuthManager.Instance.Config = previousConfig;
        Directory.Delete(testFolder, true);
    }

    [Fact]
    public void RemoteAuthManager_CreatePairingSession_GeneratesValidPinAndToken()
    {
        var auth = RemoteAuthManager.Instance;
        var pairing = auth.CreatePairingSession();

        Assert.NotNull(pairing);
        Assert.Equal(6, pairing.Pin.Length);
        Assert.True(int.TryParse(pairing.Pin, out _));
        Assert.False(string.IsNullOrWhiteSpace(pairing.Token));
        Assert.True(pairing.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void RemoteAuthManager_TryPair_ValidPin_IssuesDeviceTokenAndAuthenticates()
    {
        var auth = RemoteAuthManager.Instance;
        auth.Config.PairedDevices.Clear();

        var pairing = auth.CreatePairingSession();

        var pairReq = new PairRequestDto
        {
            Pin = pairing.Pin,
            DeviceName = "Test iPhone"
        };

        var response = auth.TryPair(pairReq, "192.168.1.50");
        Assert.True(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceToken));
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceId));

        // Validate token
        bool isValid = auth.ValidateToken(response.DeviceToken, "192.168.1.50", out var matchedDevice);
        Assert.True(isValid);
        Assert.NotNull(matchedDevice);
        Assert.Equal("Test iPhone", matchedDevice.Name);

        // Validate invalid token fails
        bool isInvalid = auth.ValidateToken("wrong_token_123", "192.168.1.50", out _);
        Assert.False(isInvalid);

        // Revoke device
        bool revoked = auth.RevokeDevice(response.DeviceId);
        Assert.True(revoked);

        bool isValidAfterRevoke = auth.ValidateToken(response.DeviceToken, "192.168.1.50", out _);
        Assert.False(isValidAfterRevoke);
    }

    [Fact]
    public void RemoteAuthManager_TryPair_QrToken_SucceedsWithoutPin()
    {
        var auth = RemoteAuthManager.Instance;
        var pairing = auth.CreatePairingSession();

        var pairReq = new PairRequestDto
        {
            Token = pairing.Token,
            DeviceName = "Android Tablet"
        };

        var response = auth.TryPair(pairReq, "192.168.1.60");
        Assert.True(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceToken));

        bool isValid = auth.ValidateToken(response.DeviceToken, "192.168.1.60", out var device);
        Assert.True(isValid);
        Assert.Equal("Android Tablet", device.Name);
    }

    [Fact]
    public void RemoteCatalogService_SanitizeDescriptionToHtml_EscapesXssAndConvertsBBCode()
    {
        string raw = "Hallo [b]wereld[/b]! <script>alert('xss')</script>\r\nCheck dit: [url=https://spotnet.nl]Spotnet[/url] en [color=red]rood[/color]!";
        string html = RemoteCatalogService.SanitizeDescriptionToHtml(raw);

        // Verify XSS is escaped
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);

        // Verify BBCode conversion
        Assert.Contains("<strong>wereld</strong>", html);
        Assert.Contains("<a href=\"https://spotnet.nl\"", html);
        Assert.Contains("<span style=\"color:red\">rood</span>", html);
        Assert.Contains("<br/>", html);
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864000, "1.46 GB")]
    public void RemoteCatalogService_FormatFileSize_FormatsCorrectly(long bytes, string expected)
    {
        string formatted = RemoteCatalogService.FormatFileSize(bytes);
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(1, "Films")]
    [InlineData(6, "Series")]
    [InlineData(5, "Boeken")]
    [InlineData(2, "Muziek")]
    [InlineData(3, "Spellen")]
    [InlineData(4, "Applicaties")]
    [InlineData(9, "Erotiek")]
    [InlineData(99, "Overig")]
    public void RemoteCatalogService_GetCategoryName_ResolvesKnownCategories(int cat, string expectedName)
    {
        string name = RemoteCatalogService.GetCategoryName(cat);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void RemoteQueueItemDto_CompletedItem_HasExpectedFlags()
    {
        var item = new DownloadItemDto
        {
            Id = "42",
            Title = "Falling Skies S01E01",
            Status = "Voltooid",
            Progress = 100,
            IsComplete = true,
            CanPause = false,
            CanResume = false
        };

        Assert.True(item.IsComplete);
        Assert.False(item.CanPause);
        Assert.False(item.CanResume);
        Assert.Equal(100, item.Progress);
    }

    [Fact]
    public void QrCodeHelper_GenerateQrCodeBitmap_GeneratesValidImage()
    {
        var bitmap = QrCodeHelper.GenerateQrCodeBitmap("http://192.168.1.100:8770/?token=abc123xyz", pixelsPerModule: 4);

        Assert.NotNull(bitmap);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public void RemoteCatalogService_SanitizeDescriptionToHtml_CleansBrTagsAndSmileys()
    {
        string raw = "Regel 1[br]Regel 2[br/][br /]</br>Veel witruimte\r\n\r\n\r\n\r\nRegel 3 :) :D ;) (Y) [img=smile]";
        string html = RemoteCatalogService.SanitizeDescriptionToHtml(raw);

        // Verify [br] tags were converted to <br/>
        Assert.DoesNotContain("[br]", html);
        Assert.DoesNotContain("[br/]", html);
        Assert.DoesNotContain("[br /]", html);
        Assert.DoesNotContain("</br>", html);
        Assert.Contains("<br/>", html);

        // Verify smileys converted to emojis
        Assert.Contains("😊", html);
        Assert.Contains("😃", html);
        Assert.Contains("😉", html);
        Assert.Contains("👍", html);

        // Verify no 3+ consecutive <br/>
        Assert.DoesNotContain("<br/><br/><br/>", html);
    }

    [Fact]
    public void RemoteCatalogService_CleanFilterQuery_RewritesDocidAndDateMacros()
    {
        string query = "docid > 100 AND date > [SN:DATE] - 86400 AND new = [SN:NEW]";
        string cleaned = RemoteCatalogService.CleanFilterQuery(query);

        Assert.DoesNotContain("docid", cleaned);
        Assert.Contains("rowid", cleaned);
        Assert.DoesNotContain("[SN:DATE]", cleaned);
        Assert.DoesNotContain("[SN:NEW]", cleaned);
        Assert.Contains("new = 0", cleaned);
    }

    [Fact]
    public void RemoteCatalogService_GetFilters_ReturnsFiltersList()
    {
        var filters = RemoteCatalogService.Instance.GetFilters();

        Assert.NotNull(filters);
        Assert.NotEmpty(filters);
        Assert.True(filters.Any(f => f.Name.Contains("Beeld", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Film", StringComparison.OrdinalIgnoreCase)));
        Assert.True(filters.All(f => !string.IsNullOrEmpty(f.Id) && !string.IsNullOrEmpty(f.Name)));
    }

    [Fact]
    public void RemoteConfig_KeepAwake_DefaultsToFalse_AndSerializesCorrectly()
    {
        var config = new RemoteConfig();
        Assert.False(config.KeepAwake);

        config.KeepAwake = true;
        string json = System.Text.Json.JsonSerializer.Serialize(config);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RemoteConfig>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.KeepAwake);
    }

    [Fact]
    public void SleepPreventer_UpdateState_TogglesIsPreventingSleep()
    {
        SleepPreventer.UpdateState(false);
        Assert.False(SleepPreventer.IsPreventingSleep);

        SleepPreventer.UpdateState(true);
        Assert.True(SleepPreventer.IsPreventingSleep);

        SleepPreventer.UpdateState(false);
        Assert.False(SleepPreventer.IsPreventingSleep);
    }

    [Fact]
    public void RemoteServer_RegisterClientActivity_UpdatesActiveClientAndTimestamp()
    {
        var server = RemoteServer.Instance;

        server.RegisterClientActivity("Test Telefoon");

        Assert.Equal("Test Telefoon", server.LastActiveClientName);
        Assert.True((DateTime.UtcNow - server.LastActivityUtc).TotalSeconds < 5);
    }

    [Fact]
    public void PasswordSecurity_HashAndVerify_ValidPassword_ReturnsTrue()
    {
        string pwd = "SecretPassword123!";
        PasswordSecurity.HashPassword(pwd, out string hashHex, out string saltHex);

        Assert.False(string.IsNullOrWhiteSpace(hashHex));
        Assert.False(string.IsNullOrWhiteSpace(saltHex));
        Assert.Equal(64, hashHex.Length); // 32 bytes in hex = 64 chars
        Assert.Equal(32, saltHex.Length); // 16 bytes in hex = 32 chars

        bool valid = PasswordSecurity.VerifyPassword(pwd, hashHex, saltHex);
        Assert.True(valid);
    }

    [Fact]
    public void PasswordSecurity_Verify_WrongPassword_ReturnsFalse()
    {
        string pwd = "CorrectPassword";
        PasswordSecurity.HashPassword(pwd, out string hashHex, out string saltHex);

        bool wrongPwd = PasswordSecurity.VerifyPassword("WrongPassword", hashHex, saltHex);
        Assert.False(wrongPwd);

        bool emptyPwd = PasswordSecurity.VerifyPassword("", hashHex, saltHex);
        Assert.False(emptyPwd);
    }

    [Fact]
    public async System.Threading.Tasks.Task PasswordSecurity_RateLimiting_LocksOutAfterMaxAttempts()
    {
        string testIp = "10.0.0.99";
        PasswordSecurity.ResetAttempts(testIp);

        Assert.False(PasswordSecurity.IsIpLockedOut(testIp, out _));

        // 4 failed attempts without delay (for test speed)
        for (int i = 0; i < 4; i++)
        {
            var (locked, _) = await PasswordSecurity.RecordFailedAttemptAsync(testIp, applyDelay: false);
            Assert.False(locked);
        }

        // 5th failed attempt should trigger lockout
        var (isLocked, lockRemaining) = await PasswordSecurity.RecordFailedAttemptAsync(testIp, applyDelay: false);
        Assert.True(isLocked);
        Assert.True(lockRemaining > TimeSpan.FromMinutes(10));
        Assert.True(PasswordSecurity.IsIpLockedOut(testIp, out _));

        // Reset
        PasswordSecurity.ResetAttempts(testIp);
        Assert.False(PasswordSecurity.IsIpLockedOut(testIp, out _));
    }

    [Fact]
    public void RemoteConfig_PasswordManagement_SetsHashAndVerifiesCredentials()
    {
        var config = new RemoteConfig
        {
            AuthUsername = "spotuser"
        };
        config.SetPassword("MySafePass@2026");

        Assert.False(string.IsNullOrWhiteSpace(config.PasswordHash));
        Assert.False(string.IsNullOrWhiteSpace(config.PasswordSalt));

        Assert.True(config.VerifyCredentials("spotuser", "MySafePass@2026"));
        Assert.True(config.VerifyCredentials(null, "MySafePass@2026"));
        Assert.True(config.VerifyCredentials("old-client-username", "MySafePass@2026"));
        Assert.False(config.VerifyCredentials("spotuser", "WrongPassword"));
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoteAuthManager_TryLoginAsync_ValidCredentials_ReturnsTokenAndPairsDevice()
    {
        var auth = RemoteAuthManager.Instance;
        auth.Config.RequireAuth = true;
        auth.Config.AuthUsername = "admin";
        auth.Config.SetPassword("SuperGeheim123");
        auth.Config.PairedDevices.Clear();

        string ip = "192.168.1.120";
        PasswordSecurity.ResetAttempts(ip);

        // Try login with invalid password
        var failRes = await auth.TryLoginAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = "FoutWachtwoord",
            DeviceName = "Test Mobile"
        }, ip);
        Assert.False(failRes.Success);
        Assert.Equal("Onjuist wachtwoord.", failRes.ErrorMessage);

        // Try login with correct credentials
        var successRes = await auth.TryLoginAsync(new LoginRequestDto
        {
            Password = "SuperGeheim123",
            DeviceName = "Test Mobile"
        }, ip);

        Assert.True(successRes.Success);
        Assert.False(string.IsNullOrWhiteSpace(successRes.DeviceToken));
        Assert.Equal("admin", successRes.Username);

        // Token must now validate
        bool isValid = auth.ValidateToken(successRes.DeviceToken, ip, out var matchedDevice);
        Assert.True(isValid);
        Assert.NotNull(matchedDevice);
        Assert.Equal("Test Mobile", matchedDevice.Name);
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("short", "short", false)]
    [InlineData("      ", "      ", false)]
    [InlineData("abcdef", "ABCDEF", false)]
    [InlineData("abcdef", "abcdef", true)]
    public void RemotePasswordConfirmationRequiresTwoMatchingValidEntries(string password, string confirmation, bool valid)
    {
        Assert.Equal(valid, Spotnet.Controls.RemotePasswordWindow.ValidatePasswords(password, confirmation) == null);
    }

    [Fact]
    public void ExistingConfigHashWorksWithoutUsernameAfterReload()
    {
        var config = new RemoteConfig { AuthUsername = "previous-name", RequireAuth = true };
        config.SetPassword("ExistingSecret");
        string hash = config.PasswordHash;
        config.Save();
        var reloaded = RemoteConfig.Load();
        Assert.Equal(hash, reloaded.PasswordHash);
        Assert.True(reloaded.VerifyPassword("ExistingSecret"));
        Assert.False(reloaded.VerifyPassword("incorrect"));
        Assert.False(reloaded.VerifyPassword(null));
    }

    [Fact]
    public void RemoteDiscoveryService_BuildDiscoveryPayload_GeneratesValidJson()
    {
        string json = RemoteDiscoveryService.BuildDiscoveryPayload(8770, false);
        Assert.NotNull(json);
        Assert.Contains("\"service\":\"spotnet-remote\"", json);
        Assert.Contains("\"port\":8770", json);
        Assert.Contains("\"name\":\"Spotnet Desktop\"", json);
    }
}
