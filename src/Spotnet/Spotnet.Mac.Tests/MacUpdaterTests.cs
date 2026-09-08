using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Mac.Updates;

namespace Spotnet.Mac.Tests;

public sealed class MacUpdaterTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task InstalledAppWithNewManifest_OffersUpdateAtStartup()
    {
        string root = Path.Combine(Path.GetTempPath(), "spotnet-updater-app-" + Guid.NewGuid().ToString("N"));
        string appPath = Path.Combine(root, "Spotnet.app");
        Directory.CreateDirectory(Path.Combine(appPath, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(appPath, "Contents", "Info.plist"), "plist");

        const string json = """
        {
          "schema": 1,
          "clientUpdate": 1,
          "version": "3.0.0.3",
          "minimumVersion": "3.0.0.0",
          "forced": 0,
          "macUrl": "https://github.com/Cyclone47/spotnet-3.0/releases/download/macos-v3.0.0.3/Spotnet-macOS-x64-v3.0.0.3.zip",
          "macSize": 1234,
          "macSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
        """;

        try
        {
            using var updater = new MacUpdater(
                root,
                new Uri("https://updates.example.test/latest-mac.json"),
                Path.Combine(appPath, "Contents", "MacOS"),
                new StaticResponseHandler(json));

            Assert.True(updater.IsInstalledApp);
            var result = await updater.CheckAsync(new Version(3, 0, 0, 2));
            Assert.Equal(MacUpdateAction.Offer, result.Decision.Action);
            Assert.Equal(new Version(3, 0, 0, 3), result.Manifest!.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsManifestWithoutMacAsset_IsIgnoredByMacClient()
    {
        const string json = """
        {
          "schema": 1,
          "clientUpdate": 1,
          "version": "3.0.13.0",
          "minimumVersion": "3.0.0.0",
          "forced": 1,
          "url": "https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.13.0/Spotnet-3.0-x64-Setup.exe",
          "size": 103680762,
          "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
          "releaseNotesUrl": "https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.13.0"
        }
        """;

        Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);
        Assert.NotNull(manifest);
        Assert.False(manifest!.HasMacAsset);
        Assert.Equal("Geen macOS-installatiebestand in het updatebestand.",
            MacUpdatePolicy.Evaluate(manifest, new Version(3, 0, 0, 0)).Reason);
    }

    [Fact]
    public void MacAsset_UsesSameVersionForcedAndMinimumVersionRules()
    {
        string json = $$"""
        {
          "schema": 1,
          "clientUpdate": 1,
          "version": "3.0.13.0",
          "minimumVersion": "3.0.10.0",
          "forced": 0,
          "url": "https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.13.0/Spotnet-macOS-x64.zip",
          "size": 1234,
          "sha256": "{{Sha}}",
          "releaseNotesUrl": "https://github.com/Cyclone47/spotnet-3.0.0/releases/tag/v3.0.13.0"
        }
        """;

        Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);
        Assert.NotNull(manifest);
        Assert.True(manifest!.HasMacAsset);
        Assert.Equal(MacUpdateAction.Required,
            MacUpdatePolicy.Evaluate(manifest, new Version(3, 0, 9, 0)).Action);
        Assert.Equal(MacUpdateAction.Offer,
            MacUpdatePolicy.Evaluate(manifest, new Version(3, 0, 10, 0)).Action);
    }

    [Fact]
    public void SkippedVersionSuppressesOptionalUpdateButNotForcedUpdate()
    {
        var manifest = ParseMacManifest(forced: false);
        Assert.Equal(MacUpdateAction.None,
            MacUpdatePolicy.Evaluate(manifest, new Version(3, 0, 1, 0), "3.0.13.0").Action);

        var forced = ParseMacManifest(forced: true);
        Assert.Equal(MacUpdateAction.Required,
            MacUpdatePolicy.Evaluate(forced, new Version(3, 0, 1, 0), "3.0.13.0").Action);
    }

    [Fact]
    public void UpdateWindowFormatsBytesForProgressAndSize()
    {
        Assert.Equal("0 B", Views.MacUpdateWindowViewModel.FormatBytes(0));
        Assert.Equal("1.0 KB", Views.MacUpdateWindowViewModel.FormatBytes(1024));
        Assert.Equal("1.0 MB", Views.MacUpdateWindowViewModel.FormatBytes(1024 * 1024));
    }

    [Fact]
    public void MacUpdaterKeepsSkippedVersionInItsDataFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "spotnet-updater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var updater = new MacUpdater(folder, applicationPath: folder);
            var manifest = ParseMacManifest(forced: false);
            updater.SkipVersion(manifest);
            Assert.Equal(manifest.Version.ToString(), updater.ReadSkippedVersion());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepoLatestMacManifest_IsValidAndHasExpectedStructure()
    {
        string baseDir = AppContext.BaseDirectory;
        DirectoryInfo? current = new DirectoryInfo(baseDir);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "updates", "latest-mac.json")))
        {
            current = current.Parent;
        }
        Assert.NotNull(current);
        string manifestPath = Path.Combine(current!.FullName, "updates", "latest-mac.json");
        string json = File.ReadAllText(manifestPath);
        Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);
        Assert.NotNull(manifest);
        Assert.True(manifest!.HasMacAsset);
        Assert.True(manifest.Size > 0);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Sha256));
        Assert.NotNull(manifest.Url);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsVerificationException_WhenSizeDiffers()
    {
        byte[] payload = [1, 2, 3, 4];
        string root = Path.Combine(Path.GetTempPath(), "spotnet-updater-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new ByteArrayResponseHandler(payload);
            using var client = new MacUpdateClient(new Uri("https://updates.example.test/latest-mac.json"), handler);
            string json = $$"""
            {
              "schema": 1,
              "clientUpdate": 1,
              "version": "3.0.0.3",
              "minimumVersion": "3.0.0.0",
              "forced": 0,
              "macUrl": "http://127.0.0.1/app.zip",
              "macSize": {{payload.Length + 10}},
              "macSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            }
            """;
            Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);

            var ex = await Assert.ThrowsAsync<MacUpdateVerificationException>(() =>
                client.DownloadAsync(manifest!, root));
            Assert.Contains($"De download is {payload.Length} bytes; verwacht {payload.Length + 10}.", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_CompletesAndMovesFile_WhenSizeAndHashMatch()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        using var sha = System.Security.Cryptography.SHA256.Create();
        string expectedSha = Convert.ToHexString(sha.ComputeHash(payload)).ToLowerInvariant();
        string root = Path.Combine(Path.GetTempPath(), "spotnet-updater-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new ByteArrayResponseHandler(payload);
            using var client = new MacUpdateClient(new Uri("https://updates.example.test/latest-mac.json"), handler);
            string json = $$"""
            {
              "schema": 1,
              "clientUpdate": 1,
              "version": "3.0.0.3",
              "minimumVersion": "3.0.0.0",
              "forced": 0,
              "macUrl": "http://127.0.0.1/app.zip",
              "macSize": {{payload.Length}},
              "macSha256": "{{expectedSha}}"
            }
            """;
            Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);

            string target = await client.DownloadAsync(manifest!, root);
            Assert.True(File.Exists(target));
            Assert.False(File.Exists(target + ".part"));
            Assert.Equal(payload.Length, new FileInfo(target).Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ByteArrayResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;

        public ByteArrayResponseHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes)
            });
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StaticResponseHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            });
        }
    }

    private static MacUpdateManifest ParseMacManifest(bool forced)
    {
        string json = $$"""
        {
          "schema": 1,
          "clientUpdate": 1,
          "version": "3.0.13.0",
          "minimumVersion": "3.0.0.0",
          "forced": {{(forced ? 1 : 0)}},
          "macUrl": "https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.13.0/Spotnet-macOS-x64.zip",
          "macSize": 1234,
          "macSha256": "{{Sha}}"
        }
        """;
        Assert.True(MacUpdateManifest.TryParse(json, out var manifest, out var error), error);
        return manifest!;
    }
}
