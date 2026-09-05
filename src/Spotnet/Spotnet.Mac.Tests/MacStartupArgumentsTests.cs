using System;
using System.IO;
using Spotnet.Mac.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests for the launch-argument classification of fase 3 item 4: spotnet:// links
/// and .nzb file paths reach the app through Open With / the URL scheme, the way
/// Windows receives them through its pipe parameters.
/// </summary>
public sealed class MacStartupArgumentsTests
{
    [Theory]
    [InlineData("spotnet://abc123@news.example.com")]
    [InlineData("SPOTNET://abc123@news.example.com")]
    public void Spotnet_links_are_recognized_regardless_of_case(string arg)
    {
        Assert.Equal(arg, MacStartupArguments.Classify(arg));
    }

    [Fact]
    public void An_existing_nzb_file_is_recognized()
    {
        string path = Path.Combine(Path.GetTempPath(), "spotnet-args-" + Guid.NewGuid().ToString("N") + ".nzb");
        File.WriteAllText(path, "<nzb/>");
        try
        {
            Assert.Equal(path, MacStartupArguments.Classify(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--exitOnUninstall")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("notaurl")]
    public void Ordinary_arguments_are_ignored(string? arg)
    {
        Assert.Null(MacStartupArguments.Classify(arg!));
    }

    [Fact]
    public void A_nonexistent_nzb_path_is_ignored()
    {
        Assert.Null(MacStartupArguments.Classify("/tmp/does-not-exist-abc.nzb"));
    }

    [Fact]
    public void GetStartupTargets_keeps_only_actionable_arguments()
    {
        string path = Path.Combine(Path.GetTempPath(), "spotnet-args-" + Guid.NewGuid().ToString("N") + ".nzb");
        File.WriteAllText(path, "<nzb/>");
        try
        {
            var targets = MacStartupArguments.GetStartupTargets(
                new[] { "--some-flag", path, "spotnet://x@y.z", "" });

            Assert.Equal(new[] { path, "spotnet://x@y.z" }, targets);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
