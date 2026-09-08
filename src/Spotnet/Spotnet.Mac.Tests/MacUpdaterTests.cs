using System;
using System.IO;
using Spotnet.Mac.Updates;

namespace Spotnet.Mac.Tests;

public sealed class MacUpdaterTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
