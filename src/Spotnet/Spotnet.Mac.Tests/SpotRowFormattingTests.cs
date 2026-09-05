using System;
using Spotnet.Mac.Models;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The "Leeftijd" and "Afzender" columns are expected to read exactly as they do on
/// Windows (DateTimeExtension.ToAge and Spot.Poster + StripNonAlphaNumericCharacters).
/// </summary>
public class SpotRowFormattingTests
{
    // A Wednesday, 14:30.
    private static readonly DateTime Now = new(2026, 9, 2, 14, 30, 0, DateTimeKind.Local);

    [Theory]
    // Same calendar day → "vandaag"
    [InlineData(2026, 9, 2, 6, 37, "vandaag (06:37)")]
    [InlineData(2026, 9, 2, 0, 1, "vandaag (00:01)")]
    // Exactly midnight is the one edge Windows resolves the other way, and this
    // port keeps that behaviour rather than correcting it.
    [InlineData(2026, 9, 2, 0, 0, "gisteren (00:00)")]
    // The previous calendar day → "gisteren"
    [InlineData(2026, 9, 1, 15, 29, "gisteren (15:29)")]
    // Two to six days back → the weekday name
    [InlineData(2026, 8, 31, 20, 39, "maandag (20:39)")]
    [InlineData(2026, 8, 30, 18, 19, "zondag (18:19)")]
    [InlineData(2026, 8, 27, 7, 3, "donderdag (07:03)")]
    // Seven days and beyond → a day count, one higher than the elapsed days,
    // which is what Windows prints.
    [InlineData(2026, 8, 26, 10, 59, "8 dagen (10:59)")]
    [InlineData(2026, 8, 25, 9, 6, "9 dagen (09:06)")]
    [InlineData(2026, 8, 23, 15, 11, "11 dagen (15:11)")]
    public void Age_MatchesTheWindowsWording(int y, int m, int d, int hh, int mm, string expected)
    {
        Assert.Equal(expected, SpotItem.FormatAge(new DateTime(y, m, d, hh, mm, 0), Now));
    }

    [Fact]
    public void Age_IsEmptyForAnUnsetTimestamp()
    {
        Assert.Equal(string.Empty, SpotItem.FormatAge(new DateTime(1999, 12, 31), Now));
    }

    [Theory]
    [InlineData("Rappie <6Ka84cIqRuVW7bBhcpEPZg@spot.net>", "Rappie")]
    [InlineData("RappieReleases <abc@spot.net>", "RappieReleases")]
    [InlineData("Rappie Releases <abc@spot.net>", "RappieReleases")]
    [InlineData("goofy2005 <x@spot.net>", "goofy2005")]
    [InlineData("Superbit", "Superbit")]
    [InlineData("", "")]
    public void SenderName_DropsThePosterIdentity(string sender, string expected)
    {
        Assert.Equal(expected, new SpotItem { Sender = sender }.SenderName);
    }
}
