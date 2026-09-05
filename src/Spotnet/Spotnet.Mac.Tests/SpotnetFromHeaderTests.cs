using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// A Spotnet spot carries its category, subcategories, size, tag and timestamp in the
/// From address. These are real-shaped headers from free.pt.
/// </summary>
public class SpotnetFromHeaderTests
{
    private const string RealFrom =
        "Rappie <6Ka84cRIw01GML4WoRlFcUOTtP0DyiEjB3LY02WAPCB8coRALLSfAgwD2Qstfy-sl.EmigK-sW4@" +
        "17a09b03b11b12b13c06c07d04d12z00.2189674633.20.1788381627.1.NL.ZD2-smwW1qKxbI9iyky>";

    [Fact]
    public void CatsColumnGetsTheShapeTheBundledFiltersMatchOn()
    {
        var spot = SpotnetHeaderParser.ParseHeader(
            "Spider Island (2026) 1080p WEB-DL X264 MULTI RETAIL NL SUBS",
            RealFrom,
            "Wed, 03 Sep 2026 11:32:54 +0000",
            "<abc@spot.net>");

        Assert.Equal(1, spot.Category);
        Assert.Equal("1 1a9 1b3 1b11 1b12 1b13 1c6 1c7 1d4 1d12 1z0", spot.Cats);
        Assert.Equal(109, spot.Subcat);          // category * 100 + a-subcategory
        Assert.Equal(7, spot.Key);
        Assert.Equal("NL", spot.Tag);
    }

    [Fact]
    public void TimestampComesFromTheSpotNotTheDateHeader()
    {
        var spot = SpotnetHeaderParser.ParseHeader("Title", RealFrom, "Wed, 03 Sep 2026 11:32:54 +0000", "<a@spot.net>");
        Assert.Equal(1_788_381_627, spot.Date);
    }

    [Fact]
    public void FilesizeComesFromTheHeaderNotTheOverviewByteCount()
    {
        var spot = SpotnetHeaderParser.ParseHeader("Title", RealFrom, "", "<a@spot.net>", bytes: 4321);
        Assert.Equal(2_189_674_633, spot.Filesize);
    }

    [Theory]
    // b4 / d11 / z1 mark a series, so a category-1 spot becomes category 6.
    [InlineData("16a03b04c06z00", 6)]
    // a5 / z2 mark an e-book (category 5).
    [InlineData("16a05b03c06z00", 5)]
    // d23..d26 / d72..d75 / z3 mark erotica (category 9).
    [InlineData("16a09b03d23z00", 9)]
    // Everything else stays where it is.
    [InlineData("16a09b03c06z00", 1)]
    public void CategoryOneIsSplitOutByItsSubcategories(string cats, int expected)
    {
        string from = $"Poster <KEY@{cats}.1000.20.1788381627.1.NL.HASH>";
        Assert.Equal(expected, SpotnetHeaderParser.ParseHeader("Title", from, "", "<a@spot.net>").Category);
    }

    [Fact]
    public void PosterNameSurvivesForTheAfzenderColumn()
    {
        var spot = SpotnetHeaderParser.ParseHeader("Title", RealFrom, "", "<a@spot.net>");
        Assert.Equal("Rappie", spot.SenderName);
    }

    [Fact]
    public void SubjectTagAfterAPipeIsSplitOff()
    {
        var spot = SpotnetHeaderParser.ParseHeader("Spider Island (2026)|RappieReleases", RealFrom, "", "<a@spot.net>");
        Assert.Equal("Spider Island (2026)", spot.Subject);
        Assert.Equal("RappieReleases", spot.Tag);
    }

    [Fact]
    public void NonSpotnetHeaderStillFallsBackToTheSubjectTag()
    {
        var spot = SpotnetHeaderParser.ParseHeader("[1a03] Big Buck Bunny 1080p", "Creator <creator@blender.org>", "", "<bunny@spot.net>");
        Assert.Equal(1, spot.Category);
        Assert.Equal("1a03", spot.Cats);
        Assert.Equal("Big Buck Bunny 1080p", spot.Subject);
    }
}
