using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Guards the two silent ways the overview ended up empty - or stuck behind a spinner -
/// on the first screen after startup.
/// </summary>
/// <remarks>
/// Neither failure is one the compiler or a unit test on the view model can see. The
/// first is an ordering mistake in startup code that only shows up on a machine where
/// the step in front of it is slow; the second is a XAML default that only shows up when
/// a trigger does not fire. Both are checked against the source, the way
/// <see cref="ExternalResourceKeyTests"/> checks resource names.
/// </remarks>
public sealed class StartupSpotsListTests
{
    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, Path.Combine(parts));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("Cannot find " + Path.Combine(parts) + " from the test output.");
    }

    private static string MainWindowSource =>
        RepoFile("src", "Spotnet", "Spotnet", "Spotnet", "Views", "MainWindow.cs");

    private static string RunAfterStartActions()
    {
        string source = MainWindowSource;
        int start = source.IndexOf("private void RunAfterStartActions()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RunAfterStartActions is gone or was renamed.");
        int end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source.Substring(start, end - start);
    }

    /// <summary>
    /// The list the window opens on is filled before the optional startup work, so a slow
    /// or failing release notes tab, promo fetch or migration cannot leave it empty.
    /// </summary>
    [Fact]
    public void SpotsListIsLoadedBeforeTheOptionalStartupWork()
    {
        string body = RunAfterStartActions();

        int load = body.IndexOf("LoadContentForTheFirstTime", StringComparison.Ordinal);
        Assert.True(load >= 0, "RunAfterStartActions no longer loads the spots list.");

        foreach (string later in new[] { "ReleaseNotes", "PromotionHelper", "MigrateFromFileToDatabase" })
        {
            int index = body.IndexOf(later, StringComparison.Ordinal);
            Assert.True(index < 0 || index > load, later + " now runs before the spots list is loaded.");
        }
    }

    /// <summary>Startup never blocks on a browser-backed tab coming up.</summary>
    [Fact]
    public void StartupDoesNotWaitForTheReleaseNotesTab()
    {
        Assert.DoesNotContain("OpenPage(PageTypeEnum.ReleaseNotes).Wait()", RunAfterStartActions(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both spot views hide their loading overlay by default and reveal it from a trigger.
    /// Defaulting to Visible leaves the spinner on screen whenever the trigger that was
    /// meant to take it away does not fire.
    /// </summary>
    [Theory]
    [InlineData("spotsthumbnailsview.xaml")]
    [InlineData("spotslistwithdetailsgrid.xaml")]
    public void TheLoadingOverlayIsHiddenUnlessATriggerShowsIt(string view)
    {
        string xaml = RepoFile("src", "Spotnet", "Spotnet", "controls", view);

        // The overlay is the style that wraps the full-size ProgressRing at the end of
        // the file: the last Grid style before it is the one under test.
        int ring = xaml.LastIndexOf("controls:ProgressRing", StringComparison.Ordinal);
        Assert.True(ring > 0, view + " no longer has a loading ring.");
        string overlay = xaml.Substring(0, ring);

        int style = overlay.LastIndexOf("<Style TargetType=\"{x:Type Grid}\">", StringComparison.Ordinal);
        Assert.True(style > 0, view + " no longer styles its loading overlay.");
        overlay = overlay.Substring(style);

        Match setter = Regex.Match(overlay, @"<Setter Property=""Visibility"" Value=""(?<value>\w+)"" />");
        Assert.True(setter.Success, view + " no longer sets the overlay's visibility.");
        Assert.Equal("Collapsed", setter.Groups["value"].Value);
    }
}
