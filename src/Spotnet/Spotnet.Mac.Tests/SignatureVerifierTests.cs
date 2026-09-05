using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Platform;
using SpotnetEnc;
using Xunit;

namespace Spotnet.Mac.Tests;

public class SignatureVerifierTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly TestAppPaths _paths;
    private readonly UserPreferencesService _prefsService;

    public SignatureVerifierTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "spotnet_sig_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _paths = new TestAppPaths(_tempFolder);
        _prefsService = new UserPreferencesService(_paths);
        SpotnetSignatureVerifier.ClearCache();
    }

    public void Dispose()
    {
        SpotnetSignatureVerifier.ClearCache();
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
        }
        GC.SuppressFinalize(this);
    }

    private static (RSA Rsa, string ModulusBase64) CreateTestKeyPair()
    {
        var rsa = RSA.Create(1024);
        var parameters = rsa.ExportParameters(includePrivateParameters: true);
        string modulusBase64 = Convert.ToBase64String(parameters.Modulus!);
        return (rsa, modulusBase64);
    }

    /// <summary>
    /// Finds a Message-ID whose SHA1 hash begins with two zero bytes (Spotnet proof-of-work).
    /// </summary>
    private static string GeneratePowMessageId(string prefix = "spot")
    {
        for (int nonce = 0; nonce < 1000000; nonce++)
        {
            string candidate = $"<{prefix}_{nonce}@spot.net>";
            if (SpotnetSignatureVerifier.CheckProofOfWork(candidate, out _))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("Failed to find proof of work nonce");
    }

    [Fact]
    public void MakeRsa_CachesAndValidatesModulus()
    {
        var (_, modulus) = CreateTestKeyPair();

        RSA? rsa1 = SpotnetSignatureVerifier.MakeRsa(modulus);
        RSA? rsa2 = SpotnetSignatureVerifier.MakeRsa(modulus);

        Assert.NotNull(rsa1);
        Assert.Same(rsa1, rsa2);

        Assert.Null(SpotnetSignatureVerifier.MakeRsa(null));
        Assert.Null(SpotnetSignatureVerifier.MakeRsa(""));
        Assert.Null(SpotnetSignatureVerifier.MakeRsa("abc")); // not multiple of 4
        Assert.Null(SpotnetSignatureVerifier.MakeRsa("short")); // < 50 chars
    }

    [Fact]
    public void CheckProofOfWork_EnforcesTwoZeroBytes()
    {
        string validMsgId = GeneratePowMessageId();
        Assert.True(SpotnetSignatureVerifier.CheckProofOfWork(validMsgId, out byte[] validHash));
        Assert.Equal(0, validHash[0]);
        Assert.Equal(0, validHash[1]);

        // A regular non-mined ID almost certainly lacks two zero bytes
        string invalidMsgId = "<ordinary-message-id-without-pow@spot.net>";
        byte[] invalidHash = SHA1.HashData(Encoding.Latin1.GetBytes(invalidMsgId));
        if (invalidHash[0] != 0 || invalidHash[1] != 0)
        {
            Assert.False(SpotnetSignatureVerifier.CheckProofOfWork(invalidMsgId, out _));
        }
    }

    [Fact]
    public void VerifyUserSignature_AcceptsValidRsaSignature()
    {
        var (rsa, modulus) = CreateTestKeyPair();
        string msgId = GeneratePowMessageId();
        SpotnetSignatureVerifier.CheckProofOfWork(msgId, out byte[] hash);

        byte[] sigBytes = rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string sigBase64 = Convert.ToBase64String(sigBytes);

        Assert.True(SpotnetSignatureVerifier.VerifyUserSignature(modulus, sigBase64, msgId));
    }

    [Fact]
    public void VerifyUserSignature_RejectsTamperedSignature()
    {
        var (rsa, modulus) = CreateTestKeyPair();
        string msgId = GeneratePowMessageId();
        SpotnetSignatureVerifier.CheckProofOfWork(msgId, out byte[] hash);

        byte[] sigBytes = rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        sigBytes[0] ^= 0xFF; // Tamper signature
        string sigBase64 = Convert.ToBase64String(sigBytes);

        Assert.False(SpotnetSignatureVerifier.VerifyUserSignature(modulus, sigBase64, msgId));
    }

    [Fact]
    public void VerifySpotHeader_Key1LegacySpot_AlwaysValid()
    {
        string from = "OldPoster <MODULUS@11a01b02c03.1000.0.1300000000.0.tag.nosig>";
        var (isValid, _) = SpotnetSignatureVerifier.VerifySpotHeader("OldPoster", "Test Title", from, "<1@spot.net>", keyId: 1);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifySpotHeader_Key7UserSignedSpot_ValidSignaturePasses()
    {
        var (rsa, modulus) = CreateTestKeyPair();
        string msgId = GeneratePowMessageId();
        string poster = "TestUploader";
        string title = "My Signed Spot";

        SpotnetSignatureVerifier.CheckProofOfWork(msgId, out byte[] hash);
        byte[] sigBytes = rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string sigBase64 = Convert.ToBase64String(sigBytes);

        string from = $"{poster} <{modulus}.{sigBase64}@17a01b02c03.1048576.0.1700000000.0.tag.dummy>";

        var (isValid, verifiedMod) = SpotnetSignatureVerifier.VerifySpotHeader(poster, title, from, msgId, keyId: 7);

        Assert.True(isValid);
        Assert.Equal(modulus, verifiedMod);
    }

    [Fact]
    public void VerifySpotHeader_Key7UserSignedSpot_TamperedSignatureFails()
    {
        var (rsa, modulus) = CreateTestKeyPair();
        string msgId = GeneratePowMessageId();
        string poster = "TestUploader";
        string title = "My Signed Spot";

        SpotnetSignatureVerifier.CheckProofOfWork(msgId, out byte[] hash);
        byte[] sigBytes = rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        sigBytes[5] ^= 0xAA; // Corrupt signature
        string sigBase64 = Convert.ToBase64String(sigBytes);

        string from = $"{poster} <{modulus}.{sigBase64}@17a01b02c03.1048576.0.1700000000.0.tag.dummy>";

        var (isValid, _) = SpotnetSignatureVerifier.VerifySpotHeader(poster, title, from, msgId, keyId: 7);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifySpotHeader_Key2TrustedKeySpot_ValidPassesAndInvalidFails()
    {
        var (trustedRsa, trustedModulus) = CreateTestKeyPair();
        var (userRsa, userModulus) = CreateTestKeyPair();
        string msgId = GeneratePowMessageId();

        string poster = "OfficialPoster";
        string title = "Official Release";
        string addressPrefix = "12a01b02c03.2048576.0.1700000000.0.tag";

        // Calculate payload hash: title + addressPrefix + poster
        byte[] payloadBytes = Encoding.Latin1.GetBytes(title + addressPrefix + poster);
        byte[] payloadHash = SHA1.HashData(payloadBytes);
        byte[] headerSigBytes = trustedRsa.SignHash(payloadHash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string headerSigBase64 = Convert.ToBase64String(headerSigBytes);

        SpotnetSignatureVerifier.CheckProofOfWork(msgId, out byte[] msgIdHash);
        byte[] userSigBytes = userRsa.SignHash(msgIdHash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string userSigBase64 = Convert.ToBase64String(userSigBytes);

        string from = $"{poster} <{userModulus}.{userSigBase64}@{addressPrefix}.{headerSigBase64}>";

        var trustedKeys = new Dictionary<int, string> { [2] = trustedModulus };

        // 1. Valid trusted signature
        var (validResult, mod) = SpotnetSignatureVerifier.VerifySpotHeader(poster, title, from, msgId, keyId: 2, trustedKeys);
        Assert.True(validResult);
        Assert.Equal(userModulus, mod);

        // 2. Missing trusted key for key ID 2
        var (noKeyResult, _) = SpotnetSignatureVerifier.VerifySpotHeader(poster, title, from, msgId, keyId: 2, trustedKeys: null);
        Assert.False(noKeyResult);

        // 3. Tampered title fails verification
        var (tamperedResult, _) = SpotnetSignatureVerifier.VerifySpotHeader(poster, "Tampered Title", from, msgId, keyId: 2, trustedKeys);
        Assert.False(tamperedResult);
    }

    [Fact]
    public void ParseOverviewLine_FiltersDroppedSpotsWhenCheckSignaturesIsOn()
    {
        // Line with key 3 and invalid signature
        string line = $"100\tUbuntu 24.04\tPoster <MODULUS@13a01b02c03.1000.0.1700000000.0.tag.badsig>\tDate\t<abc123@spot.net>\t\t1000";

        // With checkSignatures = true: rejected/dropped
        var rejected = SpotnetHeaderParser.ParseOverviewLine(line, out long artNum1, checkSignatures: true);
        Assert.Null(rejected);
        Assert.Equal(100L, artNum1);

        // With checkSignatures = false: accepted unverified
        var accepted = SpotnetHeaderParser.ParseOverviewLine(line, out long artNum2, checkSignatures: false);
        Assert.NotNull(accepted);
        Assert.Equal(100L, artNum2);
        Assert.Equal("Ubuntu 24.04", accepted.Subject);
    }

    [Fact]
    public void ParseOverviewLine_AlwaysAcceptsKey1Spots()
    {
        string line = $"200\tLegacy Post\tOldPoster <MODULUS@11a01b02c03.1000.0.1300000000.0.tag.nosig>\tDate\t<leg123@spot.net>\t\t1000";

        var spot1 = SpotnetHeaderParser.ParseOverviewLine(line, out _, checkSignatures: true);
        var spot2 = SpotnetHeaderParser.ParseOverviewLine(line, out _, checkSignatures: false);

        Assert.NotNull(spot1);
        Assert.NotNull(spot2);
        Assert.Equal(1, spot1.Key);
        Assert.Equal(1, spot2.Key);
    }

    [Fact]
    public void TrustService_LoadsDefaultKeysXml()
    {
        using var trustService = new TrustService(_paths, _prefsService);

        Assert.True(trustService.TrustedKeys.ContainsKey(2));
        Assert.True(trustService.TrustedKeys.ContainsKey(3));
        Assert.True(trustService.TrustedKeys.ContainsKey(4));

        Assert.Equal("ys8WSlqonQMWT8ubG0tAA2Q07P36E+CJmb875wSR1XH7IFhEi0CCwlUzNqBFhC+P", trustService.TrustedKeys[2]);
        Assert.Equal("uiyChPV23eguLAJNttC/o0nAsxXgdjtvUvidV2JL+hjNzc4Tc/PPo2JdYvsqUsat", trustService.TrustedKeys[3]);
        Assert.Equal("1k6RNDVD6yBYWR6kHmwzmSud7JkNV4SMigBrs+jFgOK5Ldzwl17mKXJhl+su/GR9", trustService.TrustedKeys[4]);

        Assert.True(File.Exists(Path.Combine(_paths.DataFolder, "keys.xml")));
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
