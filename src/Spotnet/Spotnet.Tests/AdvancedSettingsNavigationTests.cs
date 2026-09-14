using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Guards the settings dialog's two lists that have to stay in step: the navigation entries and
/// the switch that builds the page for each index. Moving an entry without renumbering the switch
/// compiles fine and then shows the wrong page - and, because the read-only guard picks the
/// downloader pages out by index, can also lock a page that should stay editable.
/// </summary>
/// <remarks>
/// Checked against the source, the way <see cref="StartupSpotsListTests"/> guards startup
/// ordering: neither list is reachable without constructing the window, which needs a running
/// WPF application and the resources behind it.
/// </remarks>
public sealed class AdvancedSettingsNavigationTests
{
    /// <summary>
    /// The navigation entries in order, with the page each index must build. The names do not
    /// follow one convention - the downloader pair is legacy - so the pairing is written out
    /// rather than derived, which also makes a deliberate reorder a visible edit.
    /// </summary>
    private static readonly (string Header, string Page)[] Expected =
    {
        ("Words.MenuAdvCommon", "SettingsForCommon"),
        ("Words.MenuAdvDownloads", "SettingsForDownload"),
        ("Words.MenuAdvDownloadsAdvanced", "SettingsForAdvancedDownload"),
        ("Words.MenuAdvSpotsList", "SettingsForSpotsList"),
        ("Words.MenuAdvTabs", "SettingsForTabs"),
        ("Words.MenuAdvDatabase", "SettingsForDatabase"),
        ("\"Spotnet Remote\"", "SettingsForRemote"),
        ("\"Community\"", "SettingsForCommunity"),
        ("\"Externe integraties\"", "SettingsForIntegrations"),
    };

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, Path.Combine(parts));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("Cannot find " + Path.Combine(parts) + " from the test output.");
    }

    private static string Source =>
        RepoFile("src", "Spotnet", "Spotnet", "Spotnet", "Controls", "AdvancedSettings.cs");

    /// <summary>The navigation entries, in the order they are added to the list.</summary>
    private static List<string> NavigationHeaders()
    {
        return Regex.Matches(Source, @"new KeyValuePair<string, UserControl>\((?<header>[^,]+), null\)")
            .Cast<Match>()
            .Select(m => m.Groups["header"].Value.Trim())
            .ToList();
    }

    /// <summary>The page each switch case builds, keyed by the case index.</summary>
    private static Dictionary<int, string> SwitchPages()
    {
        var pages = new Dictionary<int, string>();
        foreach (Match match in Regex.Matches(Source, @"case (?<index>\d+):\s*(?:\{\s*)?[\s\S]{0,400}?userControl = new (?<page>\w+)\("))
        {
            pages[int.Parse(match.Groups["index"].Value)] = match.Groups["page"].Value;
        }
        return pages;
    }

    [Fact]
    public void AlgemeenIsTheFirstPage()
    {
        Assert.Equal("Words.MenuAdvCommon", Assert.Single(NavigationHeaders().Take(1)));
    }

    [Fact]
    public void EveryNavigationEntryBuildsThePageItNames()
    {
        List<string> headers = NavigationHeaders();
        Dictionary<int, string> pages = SwitchPages();

        Assert.Equal(Expected.Length, headers.Count);
        Assert.Equal(Expected.Length, pages.Count);

        for (int i = 0; i < Expected.Length; i++)
        {
            Assert.Equal(Expected[i].Header, headers[i]);
            Assert.True(pages.ContainsKey(i), $"no page is built for navigation entry {i} ({Expected[i].Header}).");
            Assert.Equal(Expected[i].Page, pages[i]);
        }
    }

    /// <summary>
    /// The two downloader pages are the only ones an external downloader may make read-only.
    /// Expressed as the pages, not as bare indices, so the intent survives another reorder.
    /// </summary>
    [Fact]
    public void OnlyTheDownloaderPagesCanGoReadOnly()
    {
        Match guard = Regex.Match(Source, @"bool isDownloaderPage = (?<expression>[^;]+);");
        Assert.True(guard.Success, "The read-only guard no longer names the downloader pages.");

        // Indices 1 and 2 are Downloads and Downloads Advanced in the expected order above.
        Assert.Contains("selectedIndex == 1", guard.Groups["expression"].Value, StringComparison.Ordinal);
        Assert.Contains("selectedIndex == 2", guard.Groups["expression"].Value, StringComparison.Ordinal);
        Assert.Equal("SettingsForDownload", Expected[1].Page);
        Assert.Equal("SettingsForAdvancedDownload", Expected[2].Page);
        Assert.Equal("SettingsForCommon", Expected[0].Page);
    }
}
