using System;
using Spotnet.Browser;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Spotnet.Tests
{
    /// <summary>
    /// Guards the parts of the WebView2 page that can be exercised without a window.
    /// </summary>
    /// <remarks>
    /// The page itself needs a WPF visual tree and the Evergreen Runtime, so rendering is
    /// not testable here. What is testable is that the runtime probe answers rather than
    /// throwing and that the shipping assembly is genuinely marked AMD64.
    /// </remarks>
    public class WebView2PageTests
    {
        private readonly ITestOutputHelper _output;

        public WebView2PageTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void IsRuntimeAvailable_AnswersWithoutThrowing()
        {
            bool available = WebView2Page.IsRuntimeAvailable();

            // Either answer is valid - the point is that it returns one.
            _output.WriteLine("WebView2 Evergreen Runtime available on this machine: " + available);
            Assert.True(available || !available);
        }

        [Fact]
        public void IsRuntimeAvailable_IsStableAcrossCalls()
        {
            // PagesFactory caches the result, so a probe that flipped between calls would
            // make the engine choice depend on call order.
            Assert.Equal(WebView2Page.IsRuntimeAvailable(), WebView2Page.IsRuntimeAvailable());
        }

        // Constructing the page is deliberately not tested here. Its
        // parameterless base constructor runs InitializeComponent before the url is
        // validated, so a construction test outside a running Application fails on
        // missing XAML resources rather than on the thing being asserted.

        [Fact]
        public void SpotnetAssemblyTargetsAmd64()
        {
            Assert.Equal(ProcessorArchitecture.Amd64, typeof(WebView2Page).Assembly.GetName().ProcessorArchitecture);
        }
    }
}
