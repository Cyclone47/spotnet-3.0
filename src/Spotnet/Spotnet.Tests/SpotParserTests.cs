using System;
using System.IO;
using System.Xml.Linq;
using Spotnet.Phuse;
using Xunit;

namespace Spotnet.Tests
{
    public class SpotParserTests
    {
        [Fact]
        public void SpotParser_ParsesValidSpotXml()
        {
            string spotXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Spot>
    <Posting>
        <Key>1</Key>
        <Created>1700000000</Created>
        <Poster>TestPoster</Poster>
        <Title>Test Release 2026</Title>
        <Description>This is a test description of a spot</Description>
        <Tag>Movie</Tag>
        <Category>01</Category>
        <SubCat>01a04|01b04|01c01|01d09</SubCat>
        <Size>4500000000</Size>
        <NZB>
            <Segment>testnzbsegment1@spotnet</Segment>
            <Segment>testnzbsegment2@spotnet</Segment>
        </NZB>
        <Image>
            <Segment>testimgsegment1@spotnet</Segment>
        </Image>
    </Posting>
</Spot>";

            var doc = XDocument.Parse(spotXml);
            var posting = doc.Element("Spot")?.Element("Posting");

            Assert.NotNull(posting);
            Assert.Equal("Test Release 2026", posting.Element("Title")?.Value);
            Assert.Equal("TestPoster", posting.Element("Poster")?.Value);
            Assert.Equal("01", posting.Element("Category")?.Value);
            Assert.Equal("4500000000", posting.Element("Size")?.Value);
        }
    }
}
