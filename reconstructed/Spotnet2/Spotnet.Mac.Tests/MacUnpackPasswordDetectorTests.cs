using System.IO;
using System.Text;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacUnpackPasswordDetectorTests
{
    [Theory]
    [InlineData("<nzb><head><meta type=\"password\">SuperSecret123</meta></head></nzb>", "SuperSecret123")]
    [InlineData("<nzb><head><meta type=\"password\" value=\"AttrPass456\" /></head></nzb>", "AttrPass456")]
    [InlineData("<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><head><meta type=\"password\">NamespacePass</meta></head></nzb>", "NamespacePass")]
    public void FromNzbText_ExtractsPassword(string xml, string expected)
    {
        string? result = UnpackPasswordDetector.FromNzbText(xml);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Download info\nWachtwoord: geheim123\nVeel plezier", "geheim123")]
    [InlineData("[b]Wachtwoord:[/b] secretPass", "secretPass")]
    [InlineData("<b>Password:</b> \"QuotedSecret\"", "QuotedSecret")]
    [InlineData("pwd = my_pass_word", "my_pass_word")]
    [InlineData("Passwoord: 'SingleQuoted'", "SingleQuoted")]
    public void FromDescription_ExtractsPassword(string description, string expected)
    {
        string? result = UnpackPasswordDetector.FromDescription(description);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Wachtwoord: geen")]
    [InlineData("Password: none")]
    [InlineData("Wachtwoord: n.v.t.")]
    [InlineData("pwd: -")]
    [InlineData("Wachtwoord: ?")]
    [InlineData("Wachtwoord: x")]
    [InlineData("Geen wachtwoord vermeld")]
    public void FromDescription_IgnoresFalsePositives(string text)
    {
        string? result = UnpackPasswordDetector.FromDescription(text);
        Assert.Null(result);
    }

    [Fact]
    public void Detect_PrefersNzbOverDescription()
    {
        string nzbXml = "<nzb><head><meta type=\"password\">NzbPass</meta></head></nzb>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(nzbXml));
        string? fromStream = UnpackPasswordDetector.FromNzbStream(stream);

        string? detected = UnpackPasswordDetector.Detect(null, "Wachtwoord: DescPass");
        Assert.Equal("DescPass", detected);
        Assert.Equal("NzbPass", fromStream);
    }
}
