using System;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests voor fase 6, deel 3: de kleurregels (ColoringSpots/ColoringFilters), de
/// nieuw-markering van spots (rownew-watermerk), het IMDb-/iTunes-paneel
/// (titel-opschoning, URL-builders, SpotImdbShow) en de nieuwe voorkeuren.
/// </summary>
public class ColoringNewSpotsAndImdbTests
{
    // ── Titel-opschoning: ports van de JavaScript-functies in spot.htm ────────

    [Theory]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264", "The Matrix")]
    [InlineData("gladiator_(2000)_dvdrip", "Gladiator")]
    [InlineData("Some_Film_2012-1080p", "Some Film")]
    public void CleanTitleStripsTheYearWithTheReleaseTail(string input, string expected)
    {
        // regexstr() knipt "1999 1080p bluray"-achtige staarten inclusief het jaartal weg.
        // De 20xx-groep vreet de rest van de titel op, dus "(2000)_dvdrip" verdwijnt
        // inclusief "dvdrip" — zelfde gedrag als het JavaScript.
        Assert.Equal(expected, ImdbService.CleanTitle(input));
    }

    [Fact]
    public void CleanTitleStripsSeasonMarkers()
    {
        // Seizoensmarkeringen verdwijnen, zoals regexstr() dat doet.
        string result = ImdbService.CleanTitle("Great.Show.S01E02.720p");
        Assert.DoesNotContain("S01E02", result);
        Assert.Equal("Great Show", result);
    }

    [Fact]
    public void ToProperCaseCapitalizesEveryWord()
    {
        Assert.Equal("The Matrix Reloaded", ImdbService.ToProperCase("the matrix reloaded"));
    }

    [Fact]
    public void ExtractYearFindsTheFirstFourDigits()
    {
        Assert.Equal("1999", ImdbService.ExtractYear("The.Matrix.1999.1080p"));
        Assert.Equal("", ImdbService.ExtractYear("No Year Here"));
    }

    [Theory]
    [InlineData("Films", "The.Matrix.1999.1080p.BluRay.x264", "The Matrix")]
    [InlineData("Series", "Breaking.Bad.S01E01.720p", "Breaking Bad")]
    [InlineData("Muziek", "Ubi40 - Labour of Love (1983) [FLAC]", "Ubi40 Labour of Love")]
    public void GetInfoTitleFollowsTheCategoryRules(string category, string title, string expected)
    {
        Assert.Equal(expected, ImdbService.GetInfoTitle(category, title));
    }

    [Fact]
    public void MusicTitleLosesItsDashesLikeLoadImdb()
    {
        // loadImdb(): Music = Music.replace(/-/g, "") vóór de links gebouwd worden.
        // allmusic() doet geen proper-case, dus "of" blijft klein.
        Assert.Equal("Ubi40 Labour of Love", ImdbService.MusicTitle("Ubi40 - Labour of Love"));
    }

    [Fact]
    public void AllMusicTitleStripsCodecAndBitrate()
    {
        // allmusic(): cd-aantallen, codecs en bitrates verdwijnen.
        string result = ImdbService.AllMusicTitle("Dark Side of the Moon (1973) [FLAC] 2cds 950kbps");
        Assert.DoesNotContain("FLAC", result);
        Assert.DoesNotContain("kbps", result);
        Assert.Contains("Dark Side of the Moon", result);
    }

    // ── URL-builders ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildOmdbUrlUsesCleanTitleAndEmptyYearParameter()
    {
        string url = ImdbService.BuildOmdbUrl("Films", "The.Matrix.1999.1080p");
        Assert.Equal("http://www.omdbapi.com/?i=&t=The%20Matrix&y=&plot=full&r=json", url);
    }

    [Fact]
    public void BuildOmdbUrlAlwaysCarriesAnEmptyYearParameter()
    {
        // Setimdb() plakt letterlijk "&y=&plot=full&r=json" achter de naam; de
        // opschoning houdt geen jaartal over, dus &y= blijft leeg.
        string url = ImdbService.BuildOmdbUrl("Films", "Gladiator (2000) Director's Cut");
        Assert.EndsWith("&y=&plot=full&r=json", url);
    }

    [Fact]
    public void BuildOmdbUrlForSeriesHasNoYear()
    {
        string url = ImdbService.BuildOmdbUrl("Series", "Breaking.Bad.S01E01.720p");
        // "&y=" hoort er altijd letterlijk in (zoals Setimdb() die plakt), maar er
        // staat nooit een jaartal achter.
        Assert.DoesNotContain("&y=1", url);
        Assert.DoesNotContain("&y=2", url);
    }

    [Fact]
    public void BuildYouTubeTrailerUrlSearchesForTrailer()
    {
        string url = ImdbService.BuildYouTubeTrailerUrl("Films", "The Matrix 1999");
        Assert.StartsWith("http://www.youtube.com/results?search_query=", url);
        Assert.EndsWith("%20trailer", url);
    }

    [Fact]
    public void BuildMusicLinksReturnsTheFourStoreLinks()
    {
        var links = ImdbService.BuildMusicLinks("Ubi40 - Labour of Love (1983) [FLAC]");

        Assert.Equal(4, links.Count);
        Assert.All(links, l => Assert.True(l.IsLink));
        Assert.Equal("Allmusic.com", links[0].Label);
        Assert.Contains("allmusic.com/search/all/", links[0].Value);
        Assert.Contains("amazon.com", links[1].Value);
        Assert.Contains("last.fm/search", links[2].Value);
        Assert.Contains("bol.com", links[3].Value);
    }

    [Fact]
    public void BuildiTunesSearchUrlTargetsTheDutchStoreByDefault()
    {
        string url = ImdbService.BuildiTunesSearchUrl("Labour of Love");
        Assert.StartsWith("https://itunes.apple.com/search?term=", url);
        Assert.Contains("country=NL", url);
        Assert.Contains("media=music", url);
        Assert.Contains("entity=album", url);
    }

    [Fact]
    public void BuildiTunesTracksUrlMatchesGetTracks()
    {
        string url = ImdbService.BuildiTunesTracksUrl(1234567);
        Assert.Equal("https://itunes.apple.com/lookup?id=1234567&country=NL&entity=song&limit=200", url);
    }

    // ── Nieuw-markering (rownew-watermerk) ─────────────────────────────────────

    [Fact]
    public void NewSpotGetsBoldTitleAndHighlight()
    {
        var spot = new SpotItem { Id = 5000, Subject = "Nieuwe spot" };
        Assert.False(spot.IsNew);

        spot.IsNew = true;
        Assert.Equal(Avalonia.Media.FontWeight.Bold, spot.TitleFontWeight);

        spot.IsNew = false;
        Assert.Equal(Avalonia.Media.FontWeight.Normal, spot.TitleFontWeight);
    }

    [Fact]
    public void FillCopiesIsNew()
    {
        var source = new SpotItem { Id = 5000, IsNew = true, Subject = "x" };
        var placeholder = SpotItem.Placeholder();
        placeholder.Fill(source);

        Assert.True(placeholder.IsNew);
        Assert.Equal(Avalonia.Media.FontWeight.Bold, placeholder.TitleFontWeight);
    }

    // ── Voorkeuren: Windows-defaults ───────────────────────────────────────────

    [Fact]
    public void ColoringAndImdbPreferencesMatchWindowsDefaults()
    {
        var prefs = new UserPreferences();
        Assert.True(prefs.ColoringSpots);
        Assert.True(prefs.ColoringFilters);
        Assert.False(prefs.SpotImdbShow);
    }

    // ── IMDb-paneel in het detail-VM ───────────────────────────────────────────

    [Fact]
    public void ImdbPanelOnlyAppliesToFilmSeriesAndMusic()
    {
        using var db = new TestDbFixture();
        var vm = new SpotDetailViewModel(db.Service);

        vm.Spot = new SpotItem { Category = 1 }; // Films
        Assert.True(vm.HasImdbPanel);
        Assert.Equal("IMDb", vm.ImdbPanelHeader);

        vm.Spot = new SpotItem { Category = 6 }; // Series
        Assert.True(vm.HasImdbPanel);

        vm.Spot = new SpotItem { Category = 2 }; // Muziek
        Assert.True(vm.HasImdbPanel);
        Assert.Equal("Links & iTunes", vm.ImdbPanelHeader);

        vm.Spot = new SpotItem { Category = 3 }; // Spellen: geen paneel
        Assert.False(vm.HasImdbPanel);
    }

    [Fact]
    public void SpotImdbShowProviderOpensThePanelAutomatically()
    {
        using var db = new TestDbFixture();
        bool pref = true;
        var vm = new SpotDetailViewModel(db.Service, spotImdbShowProvider: () => pref);

        vm.Spot = new SpotItem { Category = 1, Subject = "The.Matrix.1999" }; // Films
        Assert.True(vm.IsImdbPanelOpen);

        // Uitzetten: het paneel klapt daarna niet meer vanzelf open.
        pref = false;
        vm.Spot = new SpotItem { Category = 6, Subject = "Some.Series.S01" };
        Assert.False(vm.IsImdbPanelOpen);
    }

    [Fact]
    public async Task ToggleImdbPanelClosesAnOpenPanel()
    {
        // De opvrager zit achter een inspringpunt zodat deze test het netwerk niet raakt.
        ImdbService.MovieInfoFetcher = (category, title) =>
            Task.FromResult<IReadOnlyList<SpotInfoRow>?>(new[] { new SpotInfoRow("Title", "The Matrix") });
        try
        {
            using var db = new TestDbFixture();
            bool? persisted = null;
            var vm = new SpotDetailViewModel(db.Service,
                spotImdbShowProvider: () => false,
                spotImdbShowSetter: value => persisted = value);

            vm.Spot = new SpotItem { Category = 1, Subject = "The.Matrix.1999" };
            Assert.False(vm.IsImdbPanelOpen);

            vm.ToggleImdbPanelCommand.Execute(null);
            await WaitForConditionAsync(() => vm.ImdbRows.Count > 0);
            Assert.True(vm.IsImdbPanelOpen);
            Assert.True(persisted); // openen onthoudt de voorkeur als aan

            vm.ToggleImdbPanelCommand.Execute(null);
            Assert.False(vm.IsImdbPanelOpen);
            Assert.False(persisted); // sluiten onthoudt de voorkeur als uit
        }
        finally
        {
            ImdbService.MovieInfoFetcher = (category, title) => ImdbService.FetchMovieInfoAsync(category, title);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}

/// <summary>Een database in een tijdelijke map voor één test.</summary>
internal sealed class TestDbFixture : IDisposable
{
    public TempAppPaths Paths { get; } = new();
    public DAL.MacSqliteDb Db { get; }
    public DAL.SpotDatabaseService Service { get; }

    public TestDbFixture()
    {
        Db = new DAL.MacSqliteDb(System.IO.Path.Combine(Paths.DataFolder, "imdb-test.db"));
        Service = new DAL.SpotDatabaseService(Db);
    }

    public void Dispose() => Paths.Dispose();
}
