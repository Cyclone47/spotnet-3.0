using System;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests;

public sealed class VersionHelperTests
{
    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenCurrentVersionNewer_ReturnsTrue()
    {
        var current = new Version(3, 0, 14, 0);
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.13.0"));
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.13"));
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "2.0.0.0"));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenCurrentVersionSame_ReturnsFalse()
    {
        var current = new Version(3, 0, 14, 0);
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.14.0"));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenCurrentVersionOlder_ReturnsFalse()
    {
        var current = new Version(3, 0, 13, 0);
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.14.0"));
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.1.0.0"));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenLastSeenNullOrEmpty_ReturnsTrue()
    {
        var current = new Version(3, 0, 14, 0);
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, null));
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, ""));
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "   "));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenLastSeenCorrupted_ReturnsTrue()
    {
        var current = new Version(3, 0, 14, 0);
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "corrupt_version"));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_WhenCurrentVersionNull_ReturnsFalse()
    {
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(null, "3.0.13.0"));
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(null, null));
    }

    [Fact]
    public void ShouldShowReleaseNotesOnStartup_SupportsRevisionIncrements()
    {
        var current = new Version(3, 0, 14, 1);
        Assert.True(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.14.0"));
        Assert.False(VersionHelper.ShouldShowReleaseNotesOnStartup(current, "3.0.14.1"));
    }
}
