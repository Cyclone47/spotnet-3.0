using System;
using System.IO;
using System.Text.RegularExpressions;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// One version number is edited by hand — AssemblyInfo.cs — and everything the user can
/// read has to agree with it. These tests are the enforcement behind docs/VERSIONING.md:
/// bump the assembly version without carrying the rest along and the suite fails here
/// rather than in a shipped build.
/// </summary>
public sealed class VersionConsistencyTests
{
    private static string CurrentVersion => AppHelper.AppVersion.ToString();

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, Path.Combine(parts));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("Cannot find " + Path.Combine(parts) + " from the test output.");
    }

    /// <summary>The version in the first release heading of a What's new document.</summary>
    private static string NewestSection(string html)
    {
        Match match = Regex.Match(html, @"<h3>Spotnet (?<version>\d+(?:\.\d+)+)");
        Assert.True(match.Success, "No '<h3>Spotnet <version>' heading found in the release notes.");
        return match.Groups["version"].Value;
    }

    [Fact]
    public void BundledWhatsNewLeadsWithTheRunningVersion()
    {
        Assert.Equal(CurrentVersion, NewestSection(Spotnet.Properties.Resources.whatsnew));
        Assert.Equal(CurrentVersion, NewestSection(Spotnet.Properties.Resources.whatsnew_nl));
    }

    [Fact]
    public void OnlyTheNewestReleaseCarriesTheNewBadge()
    {
        // A stale badge on an older section makes two releases look current at once.
        Assert.Single(Regex.Matches(Spotnet.Properties.Resources.whatsnew, "gh-tag"));
        Assert.Single(Regex.Matches(Spotnet.Properties.Resources.whatsnew_nl, "gh-tag"));
    }

    [Fact]
    public void EditableReleaseNotesMatchTheCompiledResources()
    {
        // Resources\ReleaseNotes\*.html is the source the resx entries are written from.
        // They drift silently when only one of the two is edited.
        Assert.Equal(CurrentVersion, NewestSection(RepoFile("src", "Spotnet", "Spotnet", "Resources", "ReleaseNotes", "whatsnew.html")));
        Assert.Equal(CurrentVersion, NewestSection(RepoFile("src", "Spotnet", "Spotnet", "Resources", "ReleaseNotes", "whatsnew.nl.html")));
    }

    [Fact]
    public void ThisVersionHasReleaseNotesInDocs()
    {
        string notes = RepoFile("docs", "releases", "v" + CurrentVersion + ".md");
        Assert.Contains(CurrentVersion, notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("README_EN.md")]
    public void ReadmeAdvertisesThisVersionAndItsDownload(string file)
    {
        string readme = RepoFile(file);
        Assert.Contains("| **Versie** | " + CurrentVersion + " |", readme.Replace("**Version**", "**Versie**"), StringComparison.Ordinal);
        Assert.Contains("releases/download/v" + CurrentVersion + "/Spotnet-3.0-x64-Setup.exe", readme, StringComparison.Ordinal);
        Assert.Contains("releases/tag/v" + CurrentVersion, readme, StringComparison.Ordinal);
    }
}
