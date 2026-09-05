using System;
using System.Linq;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class NzbParserTests
{
    private const string SampleNzbXml = """
        <?xml version="1.0" encoding="utf-8" ?>
        <!DOCTYPE nzb PUBLIC "-//newzBin//DTD NZB 1.1//EN" "http://www.newzbin.com/DTD/nzb/nzb-1.1.dtd">
        <nzb xmlns="http://www.newzbin.com/DTD/2003/nzb">
          <head>
            <meta type="title">Test Release</meta>
          </head>
          <file poster="Poster &lt;poster@example.com&gt;" date="1700000000" subject="[01/02] - &quot;test.part01.rar&quot; yEnc (1/2)">
            <groups>
              <group>alt.binaries.test</group>
              <group>alt.binaries.backup</group>
            </groups>
            <segments>
              <segment bytes="500000" number="2">seg2@test.net</segment>
              <segment bytes="500000" number="1">seg1@test.net</segment>
            </segments>
          </file>
          <file poster="Poster &lt;poster@example.com&gt;" date="1700000001" subject="[02/02] - &quot;test.part02.rar&quot; yEnc (1/1)">
            <groups>
              <group>alt.binaries.test</group>
            </groups>
            <segments>
              <segment bytes="250000" number="1">&lt;seg3@test.net&gt;</segment>
            </segments>
          </file>
        </nzb>
        """;

    [Fact]
    public void Parses_files_and_preserves_segment_order()
    {
        var files = NzbParser.Parse(SampleNzbXml);

        Assert.Equal(2, files.Count);

        var first = files[0];
        Assert.Equal("alt.binaries.test", first.Group);
        Assert.Equal(2, first.Segments.Count);

        // Segments should be sorted by number (1 then 2) despite reverse order in XML
        Assert.Equal(1, first.Segments[0].Number);
        Assert.Equal("seg1@test.net", first.Segments[0].MessageId);
        Assert.Equal(500000, first.Segments[0].Bytes);

        Assert.Equal(2, first.Segments[1].Number);
        Assert.Equal("seg2@test.net", first.Segments[1].MessageId);

        var second = files[1];
        Assert.Single(second.Segments);
        Assert.Equal("seg3@test.net", second.Segments[0].MessageId);
    }

    [Fact]
    public void Parse_returns_empty_list_on_invalid_xml()
    {
        Assert.Empty(NzbParser.Parse("not an xml"));
        Assert.Empty(NzbParser.Parse(""));
        Assert.Empty(NzbParser.Parse(null!));
    }

    [Fact]
    public void ExtractFileName_extracts_quoted_or_unquoted_names()
    {
        Assert.Equal("test.part01.rar", NzbDownloadJob.ExtractFileName("[01/02] - \"test.part01.rar\" yEnc (1/2)"));
        Assert.Equal("movie.1080p.mkv", NzbDownloadJob.ExtractFileName("\"movie.1080p.mkv\" [1/50]"));
        Assert.Equal("linux.iso", NzbDownloadJob.ExtractFileName("[123/456] linux.iso (1/50)"));
        Assert.Equal("sample.rar", NzbDownloadJob.ExtractFileName("sample.rar [1/10]"));
        Assert.Equal("document.pdf", NzbDownloadJob.ExtractFileName("document.pdf"));
    }
}
