using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public class ProviderCatalogueTests : IDisposable
{
    private readonly TempAppPaths _paths;
    private readonly FakeSecretStore _secretStore;
    private readonly UserPreferencesService _prefsService;

    public ProviderCatalogueTests()
    {
        _paths = new TempAppPaths();
        _secretStore = new FakeSecretStore();
        _prefsService = new UserPreferencesService(_paths);
        ProviderCatalogueSource.SetCacheDirectory(_paths.CacheFolder);
    }

    public void Dispose()
    {
        ProviderCatalogueSource.Reset();
        _paths.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryBuiltInProviderHasValidHostAndPort()
    {
        var providers = UsenetProviders.BuiltIn.Where(p => !p.IsManual).ToList();
        Assert.NotEmpty(providers);

        foreach (var p in providers)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Headers));
            Assert.False(string.IsNullOrWhiteSpace(p.Download));
            Assert.False(string.IsNullOrWhiteSpace(p.Upload));

            Assert.Contains(".", p.Headers);
            Assert.Contains(".", p.Download);
            Assert.Contains(".", p.Upload);

            Assert.True(p.HeadersPort == 563 || p.HeadersPort == 443 || p.HeadersPort == 119 || p.HeadersPort == 80);
            Assert.True(p.DownloadPort == 563 || p.DownloadPort == 443 || p.DownloadPort == 119 || p.DownloadPort == 80);
            Assert.True(p.UploadPort == 563 || p.UploadPort == 443 || p.UploadPort == 119 || p.UploadPort == 80);

            Assert.True(p.Group == "NL" || p.Group == "INT");
        }
    }

    [Fact]
    public void RetiredKpnServersAreNotPresent()
    {
        string[] retired = { "nova.planet.nl", "text.nova.planet.nl", "textnews.kpn.nl", "news.kpn.nl" };
        foreach (var provider in UsenetProviders.BuiltIn.Where(p => !p.IsManual))
        {
            Assert.DoesNotContain(provider.Headers, retired, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(provider.Download, retired, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(provider.Upload, retired, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PortEightyIsNotOfferedFor5EuroUsenetAndSnelNL()
    {
        foreach (string name in new[] { "5 Euro Usenet", "SnelNL" })
        {
            var provider = UsenetProviders.BuiltIn.Single(p => p.Name == name);
            Assert.Equal(563, provider.HeadersPort);
            Assert.Equal(563, provider.DownloadPort);
        }
    }

    [Fact]
    public void BuiltInProviderListIsUniqueAndGrouped()
    {
        var real = UsenetProviders.BuiltIn.Where(p => !p.IsManual).ToList();
        Assert.Equal(real.Count, real.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(real.Count, real.Select(p => p.Headers).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(real.Count(p => p.Group == "NL") >= 10);
        Assert.True(real.Count(p => p.Group == "INT") >= 10);

        var manual = Assert.Single(UsenetProviders.BuiltIn.Where(p => p.IsManual));
        Assert.Equal("", manual.Headers);
        Assert.Equal("Handmatig", manual.GroupDisplayName);
        Assert.Equal("Voer de servergegevens zelf in", manual.Subtitle);
    }

    [Fact]
    public void SearchingMatchesNameAndHostname()
    {
        var eweka = UsenetProviders.BuiltIn.Single(p => p.Name == "Eweka");
        Assert.True(eweka.Matches("ewe"));
        Assert.True(eweka.Matches("EWEKA"));
        Assert.True(eweka.Matches("textnews"));
        Assert.True(eweka.Matches("  "));
        Assert.False(eweka.Matches("giganews"));
    }

    [Fact]
    public void UsenetProvidersMatchFindsProviderByHeaderServer()
    {
        var matched = UsenetProviders.Match(UsenetProviders.BuiltIn, "textnews.eweka.nl");
        Assert.NotNull(matched);
        Assert.Equal("Eweka", matched.Name);

        var snelnlMatch = UsenetProviders.Match(UsenetProviders.BuiltIn, "myuser123.snelnl.com");
        Assert.NotNull(snelnlMatch);
        Assert.Equal("SnelNL", snelnlMatch.Name);

        Assert.Null(UsenetProviders.Match(UsenetProviders.BuiltIn, "unknown.server.com"));
        Assert.Null(UsenetProviders.Match(UsenetProviders.BuiltIn, ""));
    }

    [Fact]
    public void ProviderCatalogueParsesValidJson()
    {
        string json = @"
{
  ""schema"": 1,
  ""providers"": [
    { ""name"": ""Test NL"", ""group"": ""NL"", ""host"": ""reader.test.nl"", ""port"": 563 },
    { ""name"": ""Test INT"", ""group"": ""INT"", ""host"": ""reader.test.com"", ""port"": 443, ""upload"": ""up.test.com"", ""headers"": ""headers.test.com"" }
  ]
}";
        bool success = ProviderCatalogue.TryParse(json, out var providers, out string? error);
        Assert.True(success, error);
        Assert.NotNull(providers);
        Assert.Equal(3, providers.Count); // 2 + Manual

        var nl = providers[0];
        Assert.Equal("Test NL", nl.Name);
        Assert.Equal("NL", nl.Group);
        Assert.Equal("reader.test.nl", nl.Download);
        Assert.Equal("reader.test.nl", nl.Upload);
        Assert.Equal("reader.test.nl", nl.Headers);
        Assert.Equal(563, nl.DownloadPort);

        var intl = providers[1];
        Assert.Equal("Test INT", intl.Name);
        Assert.Equal("INT", intl.Group);
        Assert.Equal("reader.test.com", intl.Download);
        Assert.Equal("up.test.com", intl.Upload);
        Assert.Equal("headers.test.com", intl.Headers);
        Assert.Equal(443, intl.HeadersPort);

        var manual = providers[2];
        Assert.True(manual.IsManual);
    }

    [Fact]
    public void ProviderCatalogueRejectsInvalidJsonAndMalformedEntries()
    {
        // Bad schema
        Assert.False(ProviderCatalogue.TryParse(@"{""schema"": 2, ""providers"": []}", out _, out _));

        // Invalid port
        string badPort = @"{""schema"": 1, ""providers"": [{""name"":""Bad"", ""group"":""NL"", ""host"":""test.nl"", ""port"": 22}]}";
        Assert.False(ProviderCatalogue.TryParse(badPort, out _, out _));

        // Duplicate name
        string dupName = @"{""schema"": 1, ""providers"": [
            {""name"":""A"", ""group"":""NL"", ""host"":""a.nl"", ""port"": 563},
            {""name"":""A"", ""group"":""NL"", ""host"":""b.nl"", ""port"": 563}
        ]}";
        Assert.False(ProviderCatalogue.TryParse(dupName, out _, out _));

        // Duplicate headers
        string dupHeaders = @"{""schema"": 1, ""providers"": [
            {""name"":""A"", ""group"":""NL"", ""host"":""a.nl"", ""port"": 563},
            {""name"":""B"", ""group"":""NL"", ""host"":""a.nl"", ""port"": 563}
        ]}";
        Assert.False(ProviderCatalogue.TryParse(dupHeaders, out _, out _));

        // Invalid group
        string badGroup = @"{""schema"": 1, ""providers"": [{""name"":""A"", ""group"":""XYZ"", ""host"":""a.nl"", ""port"": 563}]}";
        Assert.False(ProviderCatalogue.TryParse(badGroup, out _, out _));

        // Control characters in name
        string ctrlChar = @"{""schema"": 1, ""providers"": [{""name"":""Bad\u0007Name"", ""group"":""NL"", ""host"":""a.nl"", ""port"": 563}]}";
        Assert.False(ProviderCatalogue.TryParse(ctrlChar, out _, out _));
    }

    [Fact]
    public void ProviderCatalogueSourceLoadsCachedFileAndFallsBackOnInvalid()
    {
        string cacheFile = Path.Combine(_paths.CacheFolder, ProviderCatalogueSource.CacheFileName);

        // Write valid cache
        string json = @"
{
  ""schema"": 1,
  ""providers"": [
    { ""name"": ""Cached Provider"", ""group"": ""NL"", ""host"": ""cached.example.com"", ""port"": 563 }
  ]
}";
        File.WriteAllText(cacheFile, json);
        ProviderCatalogueSource.SetCacheDirectory(_paths.CacheFolder);

        var current = ProviderCatalogueSource.Current;
        Assert.Contains(current, p => p.Name == "Cached Provider");

        // Write invalid cache - should fall back to BuiltIn
        File.WriteAllText(cacheFile, "{ invalid json }");
        ProviderCatalogueSource.SetCacheDirectory(_paths.CacheFolder);

        var fallback = ProviderCatalogueSource.Current;
        Assert.Contains(fallback, p => p.Name == "Eweka");
        Assert.DoesNotContain(fallback, p => p.Name == "Cached Provider");
    }

    [Fact]
    public void SettingsViewModelSavesRoleBasedServersFromProviderItem()
    {
        var vm = new SettingsViewModel(_secretStore, _paths, _prefsService);
        vm.SelectedProvider = "Eweka";
        Assert.Equal("textnews.eweka.nl", vm.Server);
        Assert.Equal(443, vm.Port);
        Assert.True(vm.Ssl);

        vm.Username = "user_test";
        vm.Password = "pass_test";
        vm.SaveSettings();

        // Check servers.xml
        string configPath = Path.Combine(_paths.DataFolder, "servers.xml");
        Assert.True(File.Exists(configPath));
        var doc = XDocument.Load(configPath);

        var servers = doc.Root?.Elements("Server").ToList();
        Assert.NotNull(servers);
        Assert.Equal(3, servers.Count);

        var headers = servers.FirstOrDefault(s => (string?)s.Attribute("Type") == "Headers");
        var downloads = servers.FirstOrDefault(s => (string?)s.Attribute("Type") == "Downloads");
        var uploads = servers.FirstOrDefault(s => (string?)s.Attribute("Type") == "Uploads");

        Assert.NotNull(headers);
        Assert.NotNull(downloads);
        Assert.NotNull(uploads);

        Assert.Equal("textnews.eweka.nl", (string?)headers.Attribute("Server"));
        Assert.Equal("newsreader1.eweka.nl", (string?)downloads.Attribute("Server"));
        Assert.Equal("upload.eweka.nl", (string?)uploads.Attribute("Server"));

        // Keychain secret
        Assert.Equal("pass_test", _secretStore.GetSecret(ServerProfile.SecretKey("textnews.eweka.nl", "user_test")));
        Assert.Equal("pass_test", _secretStore.GetSecret(ServerProfile.SecretKey("newsreader1.eweka.nl", "user_test")));
        Assert.Equal("pass_test", _secretStore.GetSecret(ServerProfile.SecretKey("upload.eweka.nl", "user_test")));
    }
}
