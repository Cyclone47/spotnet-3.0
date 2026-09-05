using System;
using System.Globalization;
using System.Linq;
using Spotnet.Helpers;
using Spotnet.Properties;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Help &gt; About summarises what changed against Spotnet 2. It is written once and then
/// forgotten, so it drifts: it claimed .NET 8 well after the move to .NET 10. These tests
/// hold it to the platform the application actually ships on.
/// </summary>
[Collection("UserLanguage")]
public sealed class AboutWindowContentTests
{
    private static string[] Headlines() => new[]
    {
        Words.AboutChangeRuntime,
        Words.AboutChangeBrowser,
        Words.AboutChangeSearch,
        Words.AboutChangeRemote,
        Words.AboutChangeNotifications,
        Words.AboutChangeUpdates,
        Words.AboutChangeStyles,
        Words.AboutChangeVpn,
        Words.AboutChangeSetup,
    };

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void EveryHeadlineIsTranslated(string language)
    {
        UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture(language);
        Assert.All(Headlines(), text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.False(string.IsNullOrWhiteSpace(Words.AboutChangesHeader));
        Assert.False(string.IsNullOrWhiteSpace(Words.AboutOriginBody));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void TheRuntimeHeadlineNamesTheFrameworkThatIsActuallyShipped(string language)
    {
        UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture(language);
        string runtime = Words.AboutChangeRuntime;
        Assert.Contains(".NET 10", runtime, StringComparison.Ordinal);
        Assert.Contains("64", runtime, StringComparison.Ordinal);
        // The runtime ships inside the installation folder; nothing is installed for it.
        Assert.DoesNotContain(".NET 8", runtime, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void RemoteAndNotificationsAreListedAsHeadlineChanges(string language)
    {
        UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture(language);
        Assert.Contains("Spotnet Remote", Words.AboutChangeRemote, StringComparison.Ordinal);
        Assert.Contains("QR", Words.AboutChangeRemote, StringComparison.Ordinal);
        Assert.Contains("Android", Words.AboutChangeRemote, StringComparison.Ordinal);
        Assert.True(Headlines().Any(h => h.Contains("download", StringComparison.OrdinalIgnoreCase)),
            "No headline mentions download notifications.");
    }
}
