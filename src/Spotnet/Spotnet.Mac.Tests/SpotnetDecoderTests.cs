using System.Text;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using SpotnetEnc;
using Xunit;

namespace Spotnet.Mac.Tests;

public class SpotnetDecoderTests
{
    [Fact]
    public void SpotnetDecoder_DecodesSimpleYEncData()
    {
        var decoder = new SpotnetDecoder();
        decoder.Init();

        // "=ybegin line=128 size=5 name=test\r\n" followed by encoded characters
        // In yEnc, character is byte + 42 modulo 256. For 'A' (65): 65 + 42 = 107 ('k')
        byte[] input = Encoding.Latin1.GetBytes("k");
        byte[] output = new byte[10];

        uint written = decoder.Decode(input, output, 0, (uint)input.Length);

        Assert.Equal(1u, written);
        Assert.Equal((byte)'A', output[0]);
    }

    [Fact]
    public void SpotnetDecoder_DropsLeadingDotAtLineStart_WhenDotStuffed()
    {
        var decoder = new SpotnetDecoder();
        decoder.Init();

        // In yEnc, payload byte 0x04 encodes to 0x04 + 42 = 46 (ASCII '.').
        // An NNTP server prepends an extra '.' (dot-stuffing), transmitting ".." on the wire.
        // Payload: [0x04, 'A' (65)] -> yEnc wire with stuffing: "..k\r\n..k"
        // Decoded result must be: [0x04, 'A', 0x04, 'A'] (exactly 4 bytes, NOT 6 bytes).
        byte[] wireData = Encoding.Latin1.GetBytes("..k\r\n..k");
        byte[] output = new byte[16];

        uint written = decoder.Decode(wireData, output, 0, (uint)wireData.Length);

        Assert.Equal(4u, written);
        Assert.Equal(0x04, output[0]);
        Assert.Equal((byte)'A', output[1]);
        Assert.Equal(0x04, output[2]);
        Assert.Equal((byte)'A', output[3]);
    }

    [Fact]
    public void SpotnetDecoder_PreservesDoubleDotPayload_WhenDotStuffed()
    {
        var decoder = new SpotnetDecoder();
        decoder.Init();

        // When original payload has two 0x04 bytes at line start, yEnc produces "..".
        // NNTP server dot-stuffs it into "...".
        // Decoded output must have both 0x04 bytes (2 bytes, NOT 1 and NOT 3).
        byte[] wireData = Encoding.Latin1.GetBytes("...k\r\n");
        byte[] output = new byte[16];

        uint written = decoder.Decode(wireData, output, 0, (uint)wireData.Length);

        Assert.Equal(3u, written);
        Assert.Equal(0x04, output[0]);
        Assert.Equal(0x04, output[1]);
        Assert.Equal((byte)'A', output[2]);
    }

    [Fact]
    public void SpotnetDecoder_HandlesSoftLineBreaksWithDotStuffing()
    {
        var decoder = new SpotnetDecoder();
        decoder.Init();

        // yEnc soft line break: "=\r\n" followed by dot-stuffed line "..k"
        // Wire: "k=\r\n..k" -> Decoded: ['A', 0x04, 'A']
        byte[] wireData = Encoding.Latin1.GetBytes("k=\r\n..k");
        byte[] output = new byte[16];

        uint written = decoder.Decode(wireData, output, 0, (uint)wireData.Length);

        Assert.Equal(3u, written);
        Assert.Equal((byte)'A', output[0]);
        Assert.Equal(0x04, output[1]);
        Assert.Equal((byte)'A', output[2]);
    }

    [Fact]
    public void SpotnetDecoder_DecodesSyntheticDotStuffedArticle_MatchesPayloadByteForByte()
    {
        var decoder = new SpotnetDecoder();
        decoder.Init();

        // Build a synthetic 1000-byte payload where bytes with value 0x04 are placed at:
        // - segment start (line 1 start)
        // - line starts of various lines
        // - middle and end of lines
        byte[] expectedPayload = new byte[1000];
        for (int i = 0; i < expectedPayload.Length; i++)
        {
            expectedPayload[i] = (byte)((i * 37 + 13) % 256);
        }

        // Force byte 0x04 at line start positions (every 100 bytes)
        for (int i = 0; i < expectedPayload.Length; i += 100)
        {
            expectedPayload[i] = 0x04;
        }
        // Force two 0x04 bytes at line start at offset 200
        expectedPayload[200] = 0x04;
        expectedPayload[201] = 0x04;

        // Simulate yEnc encoding + NNTP wire transmission (dot-stuffing)
        var wireSb = new StringBuilder();
        for (int i = 0; i < expectedPayload.Length; i += 100)
        {
            int lineLen = Math.Min(100, expectedPayload.Length - i);
            var lineBytes = new byte[lineLen];
            Array.Copy(expectedPayload, i, lineBytes, 0, lineLen);

            var lineSb = new StringBuilder();
            foreach (byte b in lineBytes)
            {
                byte enc = (byte)((b + 42) & 0xFF);
                // Critical characters in yEnc: 0x00, 0x0A, 0x0D, '=' (0x3D)
                if (enc == 0 || enc == 10 || enc == 13 || enc == 61)
                {
                    lineSb.Append('=').Append((char)((enc + 64) & 0xFF));
                }
                else
                {
                    lineSb.Append((char)enc);
                }
            }

            string encodedLine = lineSb.ToString();
            // RFC 3977 NNTP dot-stuffing: if line starts with '.', prepend '.'
            if (encodedLine.StartsWith('.'))
            {
                encodedLine = "." + encodedLine;
            }

            wireSb.Append(encodedLine).Append("\r\n");
        }

        byte[] rawWireBytes = Encoding.Latin1.GetBytes(wireSb.ToString());
        byte[] decodedResult = new byte[expectedPayload.Length + 512];

        uint written = decoder.Decode(rawWireBytes, decodedResult, 0, (uint)rawWireBytes.Length);

        Assert.Equal((uint)expectedPayload.Length, written);
        for (int i = 0; i < expectedPayload.Length; i++)
        {
            Assert.Equal(expectedPayload[i], decodedResult[i]);
        }
    }

    [Fact]
    public void SpotnetHeaderParser_ParsesSpotXmlCorrectly()
    {
        string spotXml = @"
            <Spot>
                <Poster>TestPoster</Poster>
                <Title>Ubuntu Desktop 24.04</Title>
                <Description>The latest long-term support release of Ubuntu.</Description>
                <Image><Segment>imgseg1@spot.net</Segment></Image>
                <NZB><Segment>nzbseg1@spot.net</Segment></NZB>
            </Spot>";

        var (title, description, img, nzb) = SpotnetHeaderParser.ParseSpotBody(spotXml);

        Assert.Equal("Ubuntu Desktop 24.04", title);
        Assert.Contains("long-term support", description);
        Assert.Equal("imgseg1@spot.net", img);
        Assert.Equal("nzbseg1@spot.net", nzb);
    }

    [Fact]
    public void SpotnetHeaderParser_ParsesHeaderSubjectAndCategory()
    {
        string subject = "[1a03] Big Buck Bunny 1080p";
        string from = "Creator <creator@blender.org>";
        string date = "Thu, 01 Jan 2026 12:00:00 +0000";
        string msgId = "<bunny@spot.net>";

        var spot = SpotnetHeaderParser.ParseHeader(subject, from, date, msgId, 850_000_000);

        Assert.Equal(1, spot.Category);
        Assert.Equal("1a03", spot.Cats);
        Assert.Equal("Big Buck Bunny 1080p", spot.Subject);
        Assert.Equal("bunny@spot.net", spot.MsgId);
        Assert.Equal(850_000_000, spot.Filesize);
    }
}
