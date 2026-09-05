using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Spotnet.Browser;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Covers the parts of the WebView2 spot page that can be checked without a browser:
    /// the contract between the host and its injected script, and the pure text handling
    /// on either side of it.
    /// </summary>
    /// <remarks>
    /// The page itself needs a WebView2 runtime, a window and a live spot, so it belongs
    /// to the desktop gate rather than here. What can be caught here is the failure mode
    /// that is otherwise silent: the host calling a bridge function that does not exist.
    /// ExecuteScriptAsync reports nothing when that happens - the panel simply never
    /// updates - so the two sides are pinned against each other below.
    /// </remarks>
    public class SpotPageBridgeTests
    {
        /// <summary>Every window.spotnet function the host page calls.</summary>
        private static readonly string[] FunctionsTheHostCalls =
        {
            "setHtml", "getHtml", "setOuterHtml", "setText", "setStyle", "prependStyle",
            "setAttr", "setValue", "setClass", "setButtonEnabled", "focusElement",
            "appendComment", "insertIntoComment", "applyUbb", "callSmiley",
            "toggleImageSize", "updateCommentAuthor", "scrollToComment", "clearSelection"
        };

        /// <summary>The link schemes the host dispatches on, which the script must forward.</summary>
        private static readonly string[] HostSchemes =
        {
            "link:", "query:", "menu:", "spotnet:", "loadimg:", "quote:", "reply:",
            "smiley:", "ubb:", "show:", "addtoblack:", "spamreports:"
        };

        /// <summary>Names of the functions the bridge script actually defines.</summary>
        private static IEnumerable<string> DefinedFunctions()
        {
            return Regex.Matches(SpotPageBridge.Script, @"^\s{8}(\w+): function", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value);
        }

        [Fact]
        public void TheBridgeDefinesEveryFunctionTheHostCalls()
        {
            List<string> defined = DefinedFunctions().ToList();

            // Both directions: a function the host lost is dead weight in the script, and
            // one the script lost is a host call that silently does nothing.
            Assert.Equal(FunctionsTheHostCalls.OrderBy(n => n), defined.OrderBy(n => n));
        }

        [Fact]
        public void TheBridgeForwardsEveryLinkSchemeTheHostHandles()
        {
            foreach (string scheme in HostSchemes)
            {
                Assert.Contains("'" + scheme + "'", SpotPageBridge.Script);
            }
        }

        [Fact]
        public void TheBridgeIsInstalledUnderTheNameTheHostUses()
        {
            Assert.Contains("window.spotnet = api;", SpotPageBridge.Script);
            Assert.Contains("window.chrome.webview.postMessage", SpotPageBridge.Script);
        }

        // --- crossing into the page ---------------------------------------------

        [Theory]
        [InlineData("plain")]
        [InlineData("a 'quoted' word")]
        [InlineData("a \"double quoted\" word")]
        [InlineData("back\\slash")]
        [InlineData("line\r\nbreak")]
        [InlineData("</script><img src=x onerror=alert(1)>")]
        [InlineData("');window.spotnet.clearSelection();('")]
        [InlineData("tab\tand\0null")]
        [InlineData("unicode \u2028 \u2029 separators")]
        [InlineData("")]
        [InlineData(null)]
        public void EverythingCrossingIntoThePageSurvivesAsAStringLiteral(string value)
        {
            string literal = SpotWebView2Page.Quoted(value);

            // The result has to be a single self-contained literal - the whole point is
            // that comment bodies off Usenet cannot end it early and add statements.
            Assert.StartsWith("\"", literal);
            Assert.EndsWith("\"", literal);
            Assert.Equal(value ?? "", JsonConvert.DeserializeObject<string>(literal));
        }

        [Fact]
        public void AQuotedValueCannotTerminateTheScriptItIsPlacedIn()
        {
            string literal = SpotWebView2Page.Quoted("x\"; window.evil(); \"y");

            // Strip the delimiters, then every escape pair. A quote surviving that would
            // be one the page could use to close the literal and start a statement.
            string inner = literal.Substring(1, literal.Length - 2);
            Assert.DoesNotContain("\"", Regex.Replace(inner, @"\\.", ""));
        }

        // --- author links --------------------------------------------------------

        [Theory]
        [InlineData("MODULUS_Poster", "MODULUS", "Poster")]
        [InlineData("MODULUS_middle_Poster", "MODULUS", "Poster")]
        public void AnAuthorLinkYieldsItsModulusAndName(string href, string modulus, string sender)
        {
            Assert.True(SpotWebView2Page.GetMenuSenderInfo(href, out string senderName, out string parsedModulus));

            Assert.Equal(modulus, parsedModulus);
            Assert.Equal(sender, senderName);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("nounderscore")]
        [InlineData("has'quote_Poster")]
        [InlineData("a_b_c_d")]
        public void AMalformedAuthorLinkIsRejected(string href)
        {
            Assert.False(SpotWebView2Page.GetMenuSenderInfo(href, out _, out _));
        }

        // --- quoting -------------------------------------------------------------

        [Fact]
        public void QuotingACommentTurnsItsMarkupBackIntoUbb()
        {
            string quote = SpotWebView2Page.GenerateQuote("<b>bold</b> and <i>italic</i><br>second line", "Someone");

            Assert.Equal("[quote=\"Someone\"][b]bold[/b] and [i]italic[/i]\r\nsecond line[/quote]\r\n", quote);
        }

        [Fact]
        public void QuotingUnwrapsLinksAndImagesTheWayTheyWereWritten()
        {
            string quote = SpotWebView2Page.GenerateQuote(
                "see <a href=\"link:http://example.com\">this</a> and <img title=smile src=x>", "Someone");

            Assert.Contains("see this and [img=smile]", quote);
            Assert.DoesNotContain("<a ", quote);
        }

        [Fact]
        public void QuotingANestedQuoteKeepsTheInnerAuthor()
        {
            string quote = SpotWebView2Page.GenerateQuote(
                "<blockquote><cite style='display: block;'>Alice wrote:</cite>hello</blockquote>", "Bob");

            Assert.Equal("[quote=\"Bob\"][quote=\"Alice\"]hello[/quote][/quote]\r\n", quote);
        }
    }
}
