using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The shape here is the one SpotHelper.CreateSpotLocal writes and
/// Worker.ParseSpotXML reads: a &lt;Spotnet&gt;&lt;Posting&gt; document whose Image and
/// NZB elements each hold one Segment per article.
/// </summary>
public sealed class SpotArticleNzbTests
{
    private const string Posting = """
        <Spotnet><Posting>
          <Key>7</Key>
          <Poster>Paaldanser</Poster>
          <Title>Setje CDM</Title>
          <Description>Een omschrijving</Description>
          <Website>http://www.google.nl/search?q=Setje</Website>
          <Image Width='600' Height='600'><Segment>img1@spot.net</Segment><Segment>img2@spot.net</Segment></Image>
          <Size>4700000000</Size>
          <Category>02<Sub>02a02</Sub></Category>
          <NZB><Segment>nzb1@spot.net</Segment><Segment>nzb2@spot.net</Segment></NZB>
        </Posting></Spotnet>
        """;

    [Fact]
    public void Parses_every_nzb_and_image_segment_in_order()
    {
        var posting = SpotArticle.ParsePosting(Posting);

        Assert.NotNull(posting);
        Assert.Equal(new[] { "nzb1@spot.net", "nzb2@spot.net" }, posting!.NzbSegments);
        Assert.Equal(new[] { "img1@spot.net", "img2@spot.net" }, posting.ImageSegments);
        Assert.True(posting.HasNzb);
    }

    [Fact]
    public void Reads_the_fields_the_detail_panel_shows()
    {
        var posting = SpotArticle.ParsePosting(Posting);

        Assert.Equal("Een omschrijving", posting!.Description);
        Assert.Equal("http://www.google.nl/search?q=Setje", posting.Website);
        Assert.Equal("Paaldanser", posting.Poster);
    }

    [Fact]
    public void A_spot_without_an_nzb_reports_no_segments()
    {
        var posting = SpotArticle.ParsePosting("<Spotnet><Posting><Description>x</Description></Posting></Spotnet>");

        Assert.NotNull(posting);
        Assert.False(posting!.HasNzb);
        Assert.Empty(posting.NzbSegments);
        Assert.Null(posting.ImageSegment);
    }

    [Fact]
    public void Segment_ids_lose_their_angle_brackets_and_quotes()
    {
        var posting = SpotArticle.ParsePosting(
            "<Spotnet><Posting><NZB><Segment>&lt;a@spot.net&gt;</Segment></NZB></Posting></Spotnet>");

        Assert.Equal(new[] { "a@spot.net" }, posting!.NzbSegments);
    }

    [Fact]
    public void An_image_url_is_kept_apart_from_segments()
    {
        var posting = SpotArticle.ParsePosting(
            "<Spotnet><Posting><Image>http://example.org/cover.jpg</Image></Posting></Spotnet>");

        Assert.Equal("http://example.org/cover.jpg", posting!.ImageUrl);
        Assert.Empty(posting.ImageSegments);
    }

    private static string Escape(byte[] payload)
    {
        // The inverse of SpotHelper.GetBinary's unescaping, so the test posts what a
        // real article carries.
        var sb = new StringBuilder();
        foreach (byte b in payload)
        {
            switch (b)
            {
                case (byte)'=':  sb.Append("=D"); break;
                case (byte)'\n': sb.Append("=C"); break;
                case (byte)'\r': sb.Append("=B"); break;
                case 0:          sb.Append("=A"); break;
                default:         sb.Append((char)b); break;
            }
        }
        return sb.ToString();
    }

    private static byte[] Deflate(string xml)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] raw = Encoding.Latin1.GetBytes(xml);
            deflate.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }

    [Fact]
    public void A_split_deflated_nzb_survives_the_whole_round_trip()
    {
        const string xml = """<?xml version="1.0"?><nzb><file subject="Café =test"/></nzb>""";

        // Escape, then split across two articles with line breaks, the way a poster does.
        string escaped = Escape(Deflate(xml));
        int half = escaped.Length / 2;
        string first = escaped[..half];
        string second = escaped[half..];

        var joined = SpotArticle.DecodeBinary(Wrap(first))
            .Concat(SpotArticle.DecodeBinary(Wrap(second)))
            .ToArray();

        Assert.Equal(xml, SpotArticle.InflateNzb(joined));

        static string Wrap(string s) =>
            string.Join("\r\n", Enumerable.Range(0, (s.Length + 127) / 128)
                                          .Select(i => s.Substring(i * 128, Math.Min(128, s.Length - i * 128)))) + "\r\n";
    }

    [Fact]
    public void Inflate_returns_null_rather_than_throwing_on_junk()
    {
        Assert.Null(SpotArticle.InflateNzb(Encoding.ASCII.GetBytes("not deflate data at all")));
        Assert.Null(SpotArticle.InflateNzb(Array.Empty<byte>()));
    }

    [Fact]
    public void Image_bytes_are_not_inflated_only_unescaped()
    {
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'=', (byte)'\n' };

        Assert.Equal(jpeg, SpotArticle.DecodeBinary(Escape(jpeg)));
    }
}
