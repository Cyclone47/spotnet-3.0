using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// A spot's description, cover image and NZB come from its article's X-XML headers, and
/// its binaries use Spotnet's own escaping rather than yEnc.
/// </summary>
public class SpotArticleTests
{
    private const string Article =
        "Path: news\r\n" +
        "From: Paaldanser <KEY@27a02b00c08d13z00.4687135728.20.1788430041.1.NL.HASH>\r\n" +
        "Subject: Setje CDM + CDS 2026-10\r\n" +
        "X-XML: <Spotnet><Posting><Description>Regel een[br]Regel twee</Description>\r\n" +
        "X-XML: <Image Width='600' Height='530'><Segment>iMG3gubpJyw1kaZagWsnn@spot.net</Segment></Image>\r\n" +
        "X-XML: <NZB><Segment>ykOss3EmjEs2EaZagYezh@spot.net</Segment></NZB></Posting></Spotnet>\r\n" +
        "\r\n" +
        "Regel een\r\nRegel twee\r\n";

    [Fact]
    public void XmlIsAssembledFromEveryXXmlHeader()
    {
        var (headers, body) = SpotArticle.Split(Article);
        var posting = SpotArticle.ParsePosting(SpotArticle.ExtractXml(headers));

        Assert.NotNull(posting);
        Assert.Equal("Regel een[br]Regel twee", posting.Description);
        Assert.Equal("iMG3gubpJyw1kaZagWsnn@spot.net", posting.ImageSegment);
        Assert.Equal(new[] { "ykOss3EmjEs2EaZagYezh@spot.net" }, posting.NzbSegments);
        Assert.Contains("Regel een", body);
    }

    [Fact]
    public void FoldedHeadersAreJoined()
    {
        var (headers, _) = SpotArticle.Split("Subject: een lange\r\n\tregel\r\n\r\nbody\r\n");
        Assert.Contains(headers, h => h.Key == "Subject" && h.Value == "een langeregel");
    }

    [Fact]
    public void BinaryBodyUsesSpotnetEscapesNotYEnc()
    {
        // =A is NUL, =B a CR, =C an LF and =D an '='; real line breaks are formatting
        // and drop out. The literal bytes are a JPEG's opening marker.
        byte[] bytes = SpotArticle.DecodeBinary("\u00ff\u00d8\u00ff\u00e0=A\u0010JFIF=A\r\n=D=B=C");

        Assert.Equal(
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x3D, 0x0D, 0x0A },
            bytes);
    }

    [Fact]
    public void Utf8PostedTextSurvivesTheLatin1Wire()
    {
        // "caf\u00e9 \U0001F606" as it arrives after a byte-for-byte Latin-1 read.
        string onTheWire = "caf\u00c3\u00a9 \u00f0\u009f\u0098\u0086";
        Assert.Equal("caf\u00e9 \U0001F606", SpotArticle.ReinterpretUtf8(onTheWire));
    }

    [Fact]
    public void PlainAsciiIsLeftAlone()
    {
        Assert.Equal("Hartelijk bedankt weer paaldanser",
                     SpotArticle.ReinterpretUtf8("Hartelijk bedankt weer paaldanser"));
    }

    [Theory]
    [InlineData("Regel een[br]Regel twee", "Regel een\nRegel twee")]
    [InlineData("leuke verzameling dank[img=buigen]", "leuke verzameling dank\U0001F647")]
    [InlineData("[b]vet[/b] en [i]schuin[/i]", "vet en schuin")]
    [InlineData("[color=#ff0000]rood[/color]", "rood")]
    [InlineData("lachen[img=schater][img=biggrin]", "lachen\U0001F606\U0001F603")]
    // An unknown smiley stays visible rather than being silently dropped.
    [InlineData("[img=onbekend]", "[img=onbekend]")]
    public void UbbMarkupRendersLikeTheWindowsView(string markup, string expected)
    {
        Assert.Equal(expected, SpotMarkup.ToPlainText(markup));
    }
}
