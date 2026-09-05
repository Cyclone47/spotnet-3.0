using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The Formaat and Genre columns come from Spotnet's category tables, the same ones the
/// Windows client reads in AppHelper.TranslateCatShort / TranslateCat / TranslateInfo.
/// </summary>
public class SpotCategoriesTests
{
    [Theory]
    // Beeld: the a-subcategory in short form.
    [InlineData(1, 0, "DivX")]
    [InlineData(1, 9, "x264")]
    [InlineData(1, 6, "Bluray")]
    [InlineData(1, 3, "DVD5")]
    [InlineData(1, 10, "DVD9")]
    // Muziek.
    [InlineData(2, 0, "MP3")]
    [InlineData(2, 2, "WAV")]
    [InlineData(2, 8, "FLAC")]
    // Spellen and Applicaties are platforms, not formats.
    [InlineData(3, 12, "PS3")]
    [InlineData(4, 0, "Win")]
    [InlineData(4, 7, "Android")]
    // No a-subcategory means an empty cell, as on Windows.
    [InlineData(1, 99, "")]
    public void FormaatMatchesTheWindowsShortNames(int cat, int subCat, string expected)
    {
        Assert.Equal(expected, SpotCategories.FormatShort(cat, subCat));
    }

    [Theory]
    [InlineData(111, "Televisie")]
    [InlineData(154, "Waargebeurd")]
    [InlineData(104, "Komedie")]
    [InlineData(106, "Documentaire")]
    [InlineData(621, "Oorlog")]        // series keep the same genre list
    [InlineData(209, "Klassiek")]      // muziek
    [InlineData(307, "Shooter")]       // spellen
    [InlineData(424, "Systeem")]       // applicaties
    [InlineData(427, "Kantoor")]
    [InlineData(199, "")]              // 99 = no genre
    [InlineData(0, "")]                // unset extcat
    public void GenreMatchesTheWindowsNames(int extCat, string expected)
    {
        Assert.Equal(expected, SpotCategories.GenreFromExtCat(extCat));
    }

    [Theory]
    // The first d-code that has a name wins.
    [InlineData(1, "1 1a9 1b3 1c6 1d4 1d12 1z0", 4)]
    [InlineData(1, "1 1a9 1b3 1d11 1z1", 11)]
    // Nothing named at all falls back to 99.
    [InlineData(1, "1 1a9 1b3", 99)]
    // Software reads the b-list, games the c-list.
    [InlineData(4, "4 4a0 4b24", 24)]
    [InlineData(3, "3 3a0 3c7", 7)]
    public void GenreCodeIsPickedTheWayWindowsPicksIt(int cat, string cats, int expected)
    {
        Assert.Equal(expected, SpotCategories.PickGenreCode(cat, cats));
    }

    [Fact]
    public void ARealHeaderFillsBothColumns()
    {
        const string from =
            "Rappie <KEY@17a09b03b11b12b13c06c07d04d12z00.2189674633.20.1788381627.1.NL.HASH>";

        var spot = SpotnetHeaderParser.ParseHeader("Spider Island (2026) 1080p", from, "", "<a@spot.net>");

        Assert.Equal("x264", spot.FormatLabel);   // a09
        Assert.Equal("Komedie", spot.GenreLabel); // d04
    }
}
