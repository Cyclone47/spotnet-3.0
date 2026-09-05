using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Covers the archive password a download carries with it, in the NZB's own metadata and
    /// in the spot text.
    /// </summary>
    /// <remarks>
    /// The cost of the two failure directions is not the same. Missing a password leaves the
    /// user exactly where they were - unrar reports the problem and the manual dialog opens.
    /// Inventing one sends unrar a password for an archive that has none, which fails an
    /// unpack that would otherwise have worked. So the negative cases below matter at least
    /// as much as the positive ones.
    /// </remarks>
    public class UnpackPasswordDetectorTests
    {
        // --- NZB metadata ----------------------------------------------------

        private static string Nzb(string head) =>
            "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>"
            + "<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\">"
            + "<head>" + head + "</head>"
            + "<file subject=\"a &quot;file.rar&quot; yEnc (1/1)\">"
            + "<groups><group>alt.binaries.ph</group></groups>"
            + "<segments><segment bytes=\"100\" number=\"1\">abc@def</segment></segments>"
            + "</file></nzb>";

        [Fact]
        public void ReadsThePasswordFromTheElementBody()
        {
            Assert.Equal("s3cret", UnpackPasswordDetector.FromNzbText(
                Nzb("<meta type=\"password\">s3cret</meta>")));
        }

        [Fact]
        public void ReadsThePasswordFromTheValueAttribute()
        {
            // Not what the DTD says, but several posting tools write it this way.
            Assert.Equal("s3cret", UnpackPasswordDetector.FromNzbText(
                Nzb("<meta type=\"password\" value=\"s3cret\"/>")));
        }

        [Fact]
        public void TheTypeAttributeIsMatchedWithoutRegardToCase()
        {
            Assert.Equal("s3cret", UnpackPasswordDetector.FromNzbText(
                Nzb("<meta type=\"Password\">s3cret</meta>")));
        }

        [Fact]
        public void ReadsThePasswordFromAnNzbWithNoNamespace()
        {
            string xml = "<nzb><head><meta type=\"password\">s3cret</meta></head></nzb>";

            Assert.Equal("s3cret", UnpackPasswordDetector.FromNzbText(xml));
        }

        [Fact]
        public void IgnoresMetadataThatIsNotThePassword()
        {
            Assert.Null(UnpackPasswordDetector.FromNzbText(
                Nzb("<meta type=\"category\">Movies</meta><meta type=\"name\">Something</meta>")));
        }

        [Fact]
        public void AnNzbWithoutAHeadSectionYieldsNothing()
        {
            Assert.Null(UnpackPasswordDetector.FromNzbText(Nzb("")));
        }

        [Fact]
        public void MalformedXmlIsNotAnError()
        {
            // NZBs arrive off Usenet; a truncated one must not fail the download.
            Assert.Null(UnpackPasswordDetector.FromNzbText("<nzb><head><meta type=\"password\">oops"));
        }

        [Fact]
        public void NoNzbTextYieldsNothing()
        {
            Assert.Null(UnpackPasswordDetector.FromNzbText(null));
            Assert.Null(UnpackPasswordDetector.FromNzbText("   "));
        }

        [Fact]
        public void AMissingNzbFileYieldsNothing()
        {
            Assert.Null(UnpackPasswordDetector.FromNzbFile(null));
            Assert.Null(UnpackPasswordDetector.FromNzbFile(@"X:\no\such\file.nzb"));
        }

        // --- spot title and body ---------------------------------------------

        [Theory]
        [InlineData("[b]Wachtwoord:[/b] hunter2", "hunter2")]
        [InlineData("Wachtwoord: hunter2", "hunter2")]
        [InlineData("wachtwoord:hunter2", "hunter2")]
        [InlineData("Password: hunter2", "hunter2")]
        [InlineData("PASSWORD = hunter2", "hunter2")]
        [InlineData("pwd: hunter2", "hunter2")]
        [InlineData("passwd: hunter2", "hunter2")]
        [InlineData("<b>Password:</b> hunter2", "hunter2")]
        [InlineData("Wachtwoord: \"hunter 2\"", "hunter 2")]
        [InlineData("Wachtwoord:\r\nhunter2", "hunter2")]
        [InlineData("Some.Release.2024\nWachtwoord: hunter2\nGeniet ervan!", "hunter2")]
        public void FindsTheLabelledPassword(string text, string expected)
        {
            Assert.Equal(expected, UnpackPasswordDetector.FromDescription(text));
        }

        [Fact]
        public void StripsThePunctuationAroundTheValue()
        {
            Assert.Equal("hunter2", UnpackPasswordDetector.FromDescription("Het wachtwoord: (hunter2)."));
        }

        [Fact]
        public void ResolvesHtmlEntitiesInTheValue()
        {
            Assert.Equal("a&b", UnpackPasswordDetector.FromDescription("Password: a&amp;b"));
        }

        [Theory]
        [InlineData("Wachtwoord: geen")]
        [InlineData("Password: none")]
        [InlineData("Wachtwoord: nvt")]
        [InlineData("Password: n/a")]
        [InlineData("Wachtwoord: -")]
        public void DoesNotTakeAnAbsentPasswordLiterally(string text)
        {
            Assert.Null(UnpackPasswordDetector.FromDescription(text));
        }

        [Theory]
        [InlineData("Geen wachtwoord nodig voor dit archief")]
        [InlineData("A film about a stolen password")]
        [InlineData("Some.Release.2024.1080p.WEB-DL")]
        [InlineData("")]
        [InlineData(null)]
        public void FindsNothingWhereNothingIsLabelled(string text)
        {
            Assert.Null(UnpackPasswordDetector.FromDescription(text));
        }

        [Fact]
        public void DoesNotReturnAValueTooLongToBeAPassword()
        {
            Assert.Null(UnpackPasswordDetector.FromDescription("Password: " + new string('x', 200)));
        }

        // --- combined --------------------------------------------------------

        [Fact]
        public void DetectFallsBackToTheDescriptionsInOrder()
        {
            Assert.Equal("frombody", UnpackPasswordDetector.Detect(
                nzbPath: null,
                "A title with nothing in it",
                "[b]Wachtwoord:[/b] frombody"));
        }

        [Fact]
        public void DetectYieldsNothingWhenNoSourceNamesOne()
        {
            Assert.Null(UnpackPasswordDetector.Detect(null, "Some.Release.2024", "No password here"));
            Assert.Null(UnpackPasswordDetector.Detect(null, (string[])null));
        }
    }
}
