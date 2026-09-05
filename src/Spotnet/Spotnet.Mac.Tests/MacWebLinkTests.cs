using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacWebLinkTests
{
    [Theory]
    [InlineData("http://www.imdb.com/title/tt0111161/")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://example.com/test?a=1&b=2")]
    public void TryResolveWebLink_AcceptsValidHttpAndHttps(string url)
    {
        Assert.True(WebLinkHelper.TryResolveWebLink(url, out string? target));
        Assert.Equal(url, target);
    }

    [Theory]
    [InlineData("   https://example.com/path   \r\n", "https://example.com/path")]
    [InlineData("http://example.com ", "http://example.com")]
    public void TryResolveWebLink_TrimsWhitespace(string input, string expected)
    {
        Assert.True(WebLinkHelper.TryResolveWebLink(input, out string? target));
        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://ftp.example.com")]
    [InlineData("about:blank")]
    [InlineData("spotnet://msgid")]
    [InlineData("undefined")]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolveWebLink_RejectsDangerousOrNonWebSchemes(string? url)
    {
        Assert.False(WebLinkHelper.TryResolveWebLink(url, out string? target));
        Assert.Null(target);
    }
}
