using Spotnet.Browser;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Covers which links a spot page is willing to open.
    /// </summary>
    /// <remarks>
    /// The href comes out of a Usenet posting, so this is the boundary between the spot body
    /// and the shell. Where the link then goes - the user's browser or an internal tab -
    /// follows the ExternalBrowser setting and needs a live window, so it is not asserted
    /// here; what is asserted is that only a real web link ever gets that far.
    /// </remarks>
    public class SpotWebLinkTests
    {
        [Theory]
        [InlineData("http://www.imdb.com/title/tt0111161/")]
        [InlineData("https://www.youtube.com/watch?v=abc&t=30s")]
        [InlineData("HTTPS://Example.COM/Path")]
        [InlineData("https://example.com/a b")]
        public void AWebLinkIsOpened(string url)
        {
            Assert.True(SpotWebView2Page.TryResolveWebLink(url, out string target));
            Assert.Equal(url, target);
        }

        [Fact]
        public void SurroundingWhitespaceIsTrimmedOff()
        {
            Assert.True(SpotWebView2Page.TryResolveWebLink("  https://example.com/x \r\n", out string target));
            Assert.Equal("https://example.com/x", target);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("ftp://example.com/payload.exe")]
        [InlineData("about:blank")]
        [InlineData("spotnet://MSGID")]
        public void ANonWebSchemeIsNeverHandedToTheShell(string url)
        {
            Assert.False(SpotWebView2Page.TryResolveWebLink(url, out string target));
            Assert.Null(target);
        }

        [Theory]
        [InlineData("undefined")]
        [InlineData("UNDEFINED")]
        [InlineData("  ")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("www.example.com")]
        [InlineData("/relative/path")]
        public void AnUnusableHrefIsIgnored(string url)
        {
            // "undefined" is what the theme's script produces for an anchor it cannot read.
            // A relative href has no scheme and cannot be launched.
            Assert.False(SpotWebView2Page.TryResolveWebLink(url, out string target));
            Assert.Null(target);
        }
    }
}
