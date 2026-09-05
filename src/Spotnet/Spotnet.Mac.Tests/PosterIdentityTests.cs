using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The spot detail panel shows "Paaldanser (5I54zQ)" on Windows: the poster name plus a
/// CRC32 of their RSA modulus. This is the real header of that spot.
/// </summary>
public class PosterIdentityTests
{
    private const string PaaldanserFrom =
        "Paaldanser <tZ-sDNmKoDIXH4P0v9zbalNL2U-sZKYFmkLWMqr1y6dgdkYVoKROkTp18gmu6cMWsl." +
        "M52zSrSjEcb3GlgvTP9tj6e2e0dU0RiMi31T5A32d-sO4E9qcafiTTdZvP5sLxwHN@" +
        "27a02b00c08d13z00.4687135728.20.1788430041.1.NL.dMnz-pxmsboI48CdsHWDNqxNV-p4thXDM-sjq-sNflgzIFOjgA4oF8J7WXOcc7ZCRD-sg>";

    [Fact]
    public void ShortIdMatchesTheOneWindowsShows()
    {
        var spot = SpotnetHeaderParser.ParseHeader("Setje CDM + CDS 2026-10", PaaldanserFrom, "", "<wD598v7yqCk1kaZagn8AE@spot.net>");

        Assert.Equal("Paaldanser", spot.SenderName);
        Assert.Equal("5I54zQ", spot.PosterId);
        Assert.Equal("Paaldanser (5I54zQ)", spot.SenderWithId);
    }

    [Fact]
    public void MissingModulusReadsAsUnknown()
    {
        Assert.Equal("Onbekend", PosterIdentity.MakeUnique(null));
        Assert.Equal("Onbekend", PosterIdentity.MakeUnique("none"));
    }

    [Theory]
    // The subcategory tables behind the detail panel, for that same spot
    // (cats "2 2a2 2b0 2c8 2d13 2z0"): Formaat WAV, Bron CD, Bitrate Lossless, Genre Pop.
    [InlineData(2, 'a', 2, "WAV")]
    [InlineData(2, 'b', 0, "CD")]
    [InlineData(2, 'c', 8, "Lossless")]
    [InlineData(2, 'd', 13, "Pop")]
    [InlineData(1, 'a', 9, "x264")]
    [InlineData(1, 'b', 3, "Retail")]
    [InlineData(1, 'c', 11, "Nederlands gesproken")]
    [InlineData(4, 'a', 0, "Windows")]
    public void SubcategoryNamesMatchWindows(int cat, char letter, int code, string expected)
    {
        Assert.Equal(expected, SpotCategories.Translate(cat, letter, code));
    }

    [Theory]
    [InlineData(2, 'b', "Bron")]
    [InlineData(2, 'c', "Bitrate")]
    [InlineData(1, 'c', "Taal")]
    [InlineData(3, 'a', "Platform")]
    [InlineData(4, 'b', "Genre")]
    public void SubcategoryLabelsMatchWindows(int cat, char letter, string expected)
    {
        Assert.Equal(expected, SpotCategories.DescribeLetter(cat, letter));
    }
}
