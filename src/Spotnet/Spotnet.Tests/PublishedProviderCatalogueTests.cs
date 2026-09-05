using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// The published catalogue is a trust boundary: it decides which servers the connect dialog offers,
/// and the user types their Usenet credentials into whichever one they pick. These tests pin the
/// rejection behaviour, because the failure that matters is a bad document being partly accepted.
/// </summary>
public sealed class PublishedProviderCatalogueTests
{
    private static readonly Type CatalogueType =
        typeof(Spotnet.Deployment.ProfileSettingsFile).Assembly.GetType("Spotnet.Model.ProviderCatalogue", throwOnError: true);

    private static readonly Type ProviderItemType =
        typeof(Spotnet.Deployment.ProfileSettingsFile).Assembly.GetType("Spotnet.Model.ProviderItem", throwOnError: true);

    private static bool TryParse(string json, out List<object> providers, out string error)
    {
        object[] arguments = { json, null, null };
        bool ok = (bool)CatalogueType.GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, arguments);
        providers = arguments[1] == null ? null : ((IEnumerable)arguments[1]).Cast<object>().ToList();
        error = (string)arguments[2];
        return ok;
    }

    private static T Value<T>(object provider, string name) => (T)ProviderItemType.GetProperty(name).GetValue(provider);

    private static string Wrap(string providers) => "{\"schema\":1,\"providers\":[" + providers + "]}";

    private const string Valid = "{\"name\":\"Eweka\",\"group\":\"NL\",\"host\":\"newsreader1.eweka.nl\",\"port\":443," +
                                 "\"upload\":\"upload.eweka.nl\",\"headers\":\"textnews.eweka.nl\"}";

    [Fact]
    public void AValidCatalogueParsesAndKeepsPerRoleServers()
    {
        Assert.True(TryParse(Wrap(Valid), out List<object> providers, out string error), error);
        // The client's own "Other..." row is appended; a published list never supplies it.
        Assert.Equal(2, providers.Count);
        object eweka = providers[0];
        Assert.Equal("Eweka", Value<string>(eweka, "Name"));
        Assert.Equal("newsreader1.eweka.nl", Value<string>(eweka, "Download"));
        Assert.Equal("upload.eweka.nl", Value<string>(eweka, "Upload"));
        Assert.Equal("textnews.eweka.nl", Value<string>(eweka, "Headers"));
        Assert.Equal(443, Value<int>(eweka, "HeadersPort"));
        Assert.True(Value<bool>(providers[1], "IsManual"));
    }

    [Fact]
    public void UploadAndHeadersDefaultToTheDownloadServer()
    {
        Assert.True(TryParse(Wrap("{\"name\":\"Hitnews\",\"group\":\"NL\",\"host\":\"news.hitnews.com\",\"port\":563}"),
            out List<object> providers, out string error), error);
        object provider = providers[0];
        Assert.Equal("news.hitnews.com", Value<string>(provider, "Upload"));
        Assert.Equal("news.hitnews.com", Value<string>(provider, "Headers"));
        Assert.Equal(563, Value<int>(provider, "UploadPort"));
    }

    [Theory]
    // A newer schema must not be guessed at.
    [InlineData("{\"schema\":2,\"providers\":[]}")]
    [InlineData("{\"providers\":[]}")]
    [InlineData("{\"schema\":\"1\",\"providers\":[]}")]
    // Structural nonsense.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{\"schema\":1}")]
    [InlineData("{\"schema\":1,\"providers\":[]}")]
    [InlineData("{\"schema\":1,\"providers\":\"nope\"}")]
    [InlineData("{\"schema\":1,\"providers\":[\"nope\"]}")]
    public void MalformedDocumentsAreRejectedWhole(string json)
    {
        Assert.False(TryParse(json, out List<object> providers, out string error));
        Assert.Null(providers);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    // Ports that are not Usenet: a published list must not redirect users to arbitrary services.
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":25}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":8080}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":0}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":-1}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":99999999999}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":\"563\"}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":563,\"uploadPort\":25}")]
    [InlineData("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":563,\"headersPort\":22}")]
    public void PortsOutsideTheUsenetAllowListAreRejected(string provider)
    {
        Assert.False(TryParse(Wrap(provider), out _, out string error));
        Assert.Contains("port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Anything that is not a plain hostname: schemes, paths, ports, credentials, wildcards.
    [InlineData("https://evil.example.com")]
    [InlineData("a.example.com/path")]
    [InlineData("a.example.com:563")]
    [InlineData("user:pass@a.example.com")]
    [InlineData("localhost")]
    [InlineData("*.example.com")]
    [InlineData("a..example.com")]
    [InlineData("-bad.example.com")]
    [InlineData("")]
    public void HostsThatAreNotPlainDnsNamesAreRejected(string host)
    {
        string json = Wrap("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"" + host + "\",\"port\":563}");
        Assert.False(TryParse(json, out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmedThenValidated()
    {
        // The file is hand-edited, so stray whitespace is tolerated - but only around a value that
        // still has to pass the hostname rules on its own.
        Assert.True(TryParse(Wrap("{\"name\":\" X \",\"group\":\"NL\",\"host\":\" a.example.com \",\"port\":563}"),
            out List<object> providers, out string error), error);
        Assert.Equal("a.example.com", Value<string>(providers[0], "Download"));
        Assert.Equal("X", Value<string>(providers[0], "Name"));
        Assert.False(TryParse(Wrap("{\"name\":\"X\",\"group\":\"NL\",\"host\":\" not a host \",\"port\":563}"), out _, out _));
    }

    [Fact]
    public void HostsAreNormalisedToLowerCase()
    {
        Assert.True(TryParse(Wrap("{\"name\":\"X\",\"group\":\"NL\",\"host\":\"News.Example.COM\",\"port\":563}"),
            out List<object> providers, out string error), error);
        Assert.Equal("news.example.com", Value<string>(providers[0], "Download"));
    }

    [Theory]
    [InlineData("{\"name\":\"X\",\"group\":\"XX\",\"host\":\"a.example.com\",\"port\":563}")]
    [InlineData("{\"name\":\"X\",\"group\":\"MANUAL\",\"host\":\"a.example.com\",\"port\":563}")]
    [InlineData("{\"name\":\"X\",\"host\":\"a.example.com\",\"port\":563}")]
    public void UnknownGroupsAreRejected(string provider)
    {
        Assert.False(TryParse(Wrap(provider), out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void APublishedCatalogueCannotSupplyItsOwnManualEntry()
    {
        // "MANUAL" is refused as a group, so no published row can pose as the client's own entry.
        Assert.False(TryParse(Wrap("{\"name\":\"Other\",\"group\":\"MANUAL\",\"host\":\"a.example.com\",\"port\":563}"), out _, out _));
    }

    [Theory]
    // A name carrying control characters or bidi overrides can disguise which provider a row is.
    [InlineData("Ewe\\u0000ka")]
    [InlineData("Ewe\\u202eka")]
    [InlineData("Ewe\\nka")]
    [InlineData("")]
    [InlineData("   ")]
    public void NamesWithControlCharactersAreRejected(string name)
    {
        string json = Wrap("{\"name\":\"" + name + "\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":563}");
        Assert.False(TryParse(json, out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DuplicateNamesOrServersAreRejected()
    {
        string one = "{\"name\":\"A\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":563}";
        string sameName = "{\"name\":\"a\",\"group\":\"NL\",\"host\":\"b.example.com\",\"port\":563}";
        string sameHost = "{\"name\":\"B\",\"group\":\"NL\",\"host\":\"A.example.com\",\"port\":563}";
        Assert.False(TryParse(Wrap(one + "," + sameName), out _, out _));
        Assert.False(TryParse(Wrap(one + "," + sameHost), out _, out _));
    }

    [Fact]
    public void OneBadEntryRejectsTheWholeDocument()
    {
        // A partially applied list is how one attacker-controlled row hides among two dozen real ones.
        string bad = "{\"name\":\"Bad\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":25}";
        Assert.False(TryParse(Wrap(Valid + "," + bad), out List<object> providers, out _));
        Assert.Null(providers);
    }

    [Fact]
    public void AnOversizedListIsRejected()
    {
        int limit = (int)CatalogueType.GetField("MaxProviders", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        var rows = new List<string>();
        for (int index = 0; index <= limit; index++)
            rows.Add("{\"name\":\"P" + index + "\",\"group\":\"NL\",\"host\":\"h" + index + ".example.com\",\"port\":563}");
        Assert.False(TryParse(Wrap(string.Join(",", rows)), out _, out string error));
        Assert.Contains("limit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCatalogueUrlIsHttpsAndPointsAtTheProjectRepository()
    {
        Type source = typeof(Spotnet.Deployment.ProfileSettingsFile).Assembly
            .GetType("Spotnet.Model.ProviderCatalogueSource", throwOnError: true);
        string url = (string)source.GetField("Url", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        Assert.StartsWith("https://", url, StringComparison.Ordinal);
        Assert.Contains("Cyclone47/spotnet-3.0", url, StringComparison.Ordinal);
    }

    /// <summary>The file the repository actually publishes must satisfy the client's own validator.</summary>
    [Fact]
    public void TheShippedProvidersJsonIsAcceptedAndMatchesTheBuiltInList()
    {
        string path = RepositoryFile("providers.json");
        string json = File.ReadAllText(path);

        int maxBytes = (int)CatalogueType.GetField("MaxBytes", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        Assert.True(new FileInfo(path).Length <= maxBytes, "providers.json exceeds the client's size cap.");
        Assert.True(TryParse(json, out List<object> published, out string error), error);

        Type providers = typeof(Spotnet.Deployment.ProfileSettingsFile).Assembly
            .GetType("Spotnet.Model.UsenetProviders", throwOnError: true);
        var builtIn = ((IEnumerable)providers.GetProperty("BuiltIn", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).Cast<object>().ToList();

        // Drift here means a fresh install and an updated install disagree about what exists.
        Assert.Equal(
            builtIn.Select(p => Value<string>(p, "Name") + "|" + Value<string>(p, "Headers") + ":" + Value<int>(p, "HeadersPort")),
            published.Select(p => Value<string>(p, "Name") + "|" + Value<string>(p, "Headers") + ":" + Value<int>(p, "HeadersPort")));
    }

    private static string RepositoryFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Cannot find " + relative + " from the test output.");
    }
}

/// <summary>
/// The cache is what makes an edit to providers.json reach users, so these cover the disk path:
/// a good cache is used, a bad one is discarded rather than shrinking the list to nothing.
/// </summary>
public sealed class ProviderCatalogueCacheTests : IDisposable
{
    private static readonly Assembly App = typeof(Spotnet.Deployment.ProfileSettingsFile).Assembly;
    private static readonly Type SourceType = App.GetType("Spotnet.Model.ProviderCatalogueSource", throwOnError: true);
    private static readonly Type ProvidersType = App.GetType("Spotnet.Model.UsenetProviders", throwOnError: true);
    private static readonly FieldInfo SettingsFolder =
        App.GetType("Spotnet.Helpers.AppHelper", throwOnError: true).GetField("SettingsFolder", BindingFlags.Static | BindingFlags.NonPublic);

    private readonly string _folder;
    private readonly string _previousFolder;

    public ProviderCatalogueCacheTests()
    {
        _previousFolder = (string)SettingsFolder.GetValue(null);
        _folder = Path.Combine(Path.GetTempPath(), "spotnet-catalogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        SettingsFolder.SetValue(null, _folder + Path.DirectorySeparatorChar);
        Reset();
    }

    public void Dispose()
    {
        SettingsFolder.SetValue(null, _previousFolder);
        Reset();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static void Reset() => SourceType.GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    private static List<object> Current() =>
        ((IEnumerable)SourceType.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>().ToList();

    private static int BuiltInCount() =>
        ((IEnumerable)ProvidersType.GetProperty("BuiltIn", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>().Count();

    private void WriteCache(string json) => File.WriteAllText(Path.Combine(_folder, "providers.json"), json);

    private static string Name(object provider) =>
        (string)App.GetType("Spotnet.Model.ProviderItem", throwOnError: true).GetProperty("Name").GetValue(provider);

    [Fact]
    public void WithNoCacheTheBuiltInListIsUsed()
    {
        Assert.Equal(BuiltInCount(), Current().Count);
    }

    [Fact]
    public void AValidCacheReplacesTheBuiltInList()
    {
        WriteCache("{\"schema\":1,\"providers\":[" +
            "{\"name\":\"Eweka\",\"group\":\"NL\",\"host\":\"newsreader1.eweka.nl\",\"port\":443}," +
            "{\"name\":\"Brand New Provider\",\"group\":\"NL\",\"host\":\"news.example-new.nl\",\"port\":563}]}");
        Reset();

        List<object> current = Current();
        Assert.Equal(3, current.Count); // two published plus the client's own manual row
        Assert.Contains("Brand New Provider", current.Select(Name));
    }

    [Theory]
    [InlineData("{\"schema\":1,\"providers\":[{\"name\":\"Bad\",\"group\":\"NL\",\"host\":\"a.example.com\",\"port\":25}]}")]
    [InlineData("{\"schema\":9,\"providers\":[]}")]
    [InlineData("truncated {")]
    [InlineData("")]
    public void AnInvalidCacheFallsBackToTheBuiltInListRatherThanEmptying(string json)
    {
        WriteCache(json);
        Reset();
        Assert.Equal(BuiltInCount(), Current().Count);
    }
}
