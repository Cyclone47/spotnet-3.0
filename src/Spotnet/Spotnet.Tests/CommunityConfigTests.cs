using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spotnet.Community;
using Xunit;

namespace Spotnet.Tests;

[CollectionDefinition("Community configuration", DisableParallelization = true)]
public class CommunityConfigCollection
{
}

/// <summary>
/// The configuration decides which community this client joins, so the cases that matter
/// are: an untouched install still points where it always did, a half-written file does not
/// take the client down with it, and nonsense is caught before it is saved.
/// </summary>
[Collection("Community configuration")]
public class CommunityConfigTests : IDisposable
{
    private readonly string previousFolder = Spotnet.Helpers.AppHelper.SettingsFolder;
    private readonly string testFolder =
        Path.Combine(Path.GetTempPath(), "CommunityConfigTests-" + Guid.NewGuid().ToString("N"));

    public CommunityConfigTests()
    {
        Directory.CreateDirectory(testFolder);
        Spotnet.Helpers.AppHelper.SettingsFolder = testFolder;
        CommunityConfig.Invalidate();
    }

    public void Dispose()
    {
        Spotnet.Helpers.AppHelper.SettingsFolder = previousFolder;
        CommunityConfig.Invalidate();
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DefaultsStillPointAtTheExistingCommunity()
    {
        CommunityConfig config = new CommunityConfig();

        Assert.Equal("free.pt", config.Newsgroups.Spots);
        Assert.Equal("free.usenet", config.Newsgroups.Comments);
        Assert.Equal("free.willey", config.Newsgroups.Reports);
        Assert.Equal("alt.binaries.ftd", config.Newsgroups.Nzb);

        Assert.True(config.Moderation.Enabled);
        Assert.Equal(120, config.Moderation.UpdateIntervalMinutes);
        Assert.Equal("http://spotcloud.spotnet.wf/spotnet/lists.new/whitelist.csv", config.Moderation.WhitelistUrl);
        Assert.Equal("http://spotcloud.spotnet.wf/spotnet/lists.new/blacklist.csv", config.Moderation.BlacklistUrl);
        Assert.Equal("https://spotcloud.spotnet.wf/spotnet/response/", config.Services.ResponseSiteUrl);

        // Signature checking is off until a community publishes signatures.
        Assert.False(config.Moderation.RequireSignedLists);
        Assert.Equal("", config.Moderation.SignaturePublicKeyXml);

        Assert.False(config.Moderation.UseClassicLists);
        Assert.Equal("https://spotlist.store/spotnet/whitelist.xml", config.Moderation.ClassicWhitelistUrl);
        Assert.Equal("https://spotlist.store/spotnet/blacklist.xml", config.Moderation.ClassicBlacklistUrl);
    }

    [Fact]
    public void DefaultConfigurationIsValid()
    {
        Assert.Empty(new CommunityConfig().Validate());
    }

    [Fact]
    public void SurvivesARoundTrip()
    {
        CommunityConfig original = new CommunityConfig
        {
            Name = "Testgemeenschap"
        };
        original.Newsgroups.Spots = "free.test";
        original.Moderation.UpdateIntervalMinutes = 45;
        original.Moderation.UseClassicLists = true;
        original.Moderation.ClassicWhitelistUrl = "https://example.org/whitelist.xml";
        original.Moderation.ClassicBlacklistUrl = "https://example.org/blacklist.xml";
        original.Integrations.NewznabApiKey = "abcdef";

        CommunityConfig restored = CommunityConfig.Deserialize(original.Serialize());

        Assert.Equal("Testgemeenschap", restored.Name);
        Assert.Equal("free.test", restored.Newsgroups.Spots);
        Assert.Equal(45, restored.Moderation.UpdateIntervalMinutes);
        Assert.True(restored.Moderation.UseClassicLists);
        Assert.Equal("https://example.org/whitelist.xml", restored.Moderation.ClassicWhitelistUrl);
        Assert.Equal("https://example.org/blacklist.xml", restored.Moderation.ClassicBlacklistUrl);
        Assert.Equal("abcdef", restored.Integrations.NewznabApiKey);
    }

    [Fact]
    public void AFileThatOverridesOneValueKeepsTheDefaultsForTheRest()
    {
        CommunityConfig config = CommunityConfig.Deserialize(
            "{ \"newsgroups\": { \"spots\": \"free.anders\" } }");

        Assert.Equal("free.anders", config.Newsgroups.Spots);
        Assert.Equal("free.usenet", config.Newsgroups.Comments);
        Assert.Equal(120, config.Moderation.UpdateIntervalMinutes);
        Assert.NotNull(config.Services);
        Assert.NotNull(config.Integrations);
    }

    [Fact]
    public void AnEmptyDocumentKeepsEveryDefault()
    {
        CommunityConfig config = CommunityConfig.Deserialize("{}");

        Assert.NotNull(config);
        Assert.Equal("free.pt", config.Newsgroups.Spots);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void NothingAtAllIsNotAConfiguration()
    {
        Assert.Null(CommunityConfig.Deserialize(""));
        Assert.Null(CommunityConfig.Deserialize("   "));
    }

    [Fact]
    public void AnUnreadableFileFallsBackToTheDefaults()
    {
        File.WriteAllText(CommunityConfig.ConfigPath, "{ this is not json");

        CommunityConfig config = CommunityConfig.Load();

        Assert.Equal("free.pt", config.Newsgroups.Spots);
    }

    [Fact]
    public void AMissingFileFallsBackToTheDefaults()
    {
        Assert.False(File.Exists(CommunityConfig.ConfigPath));

        Assert.Equal("free.pt", CommunityConfig.Load().Newsgroups.Spots);
    }

    [Fact]
    public void SavedConfigurationIsReadBack()
    {
        CommunityConfig config = new CommunityConfig { Name = "Bewaard" };
        config.Moderation.WhitelistUrl = "https://voorbeeld.nl/whitelist.csv";

        Assert.True(config.Save());
        Assert.True(File.Exists(CommunityConfig.ConfigPath));

        CommunityConfig loaded = CommunityConfig.Load();
        Assert.Equal("Bewaard", loaded.Name);
        Assert.Equal("https://voorbeeld.nl/whitelist.csv", loaded.Moderation.WhitelistUrl);
    }

    [Fact]
    public void AnEmptyNewsgroupIsRejected()
    {
        CommunityConfig config = new CommunityConfig();
        config.Newsgroups.Reports = "";

        Assert.Contains(config.Validate(), e => e.Contains("Klachten-newsgroup"));
    }

    [Fact]
    public void AUrlPastedIntoANewsgroupFieldIsRejected()
    {
        CommunityConfig config = new CommunityConfig();
        config.Newsgroups.Spots = "http://spotcloud.spotnet.wf/spots";

        Assert.Contains(config.Validate(), e => e.Contains("Spots-newsgroup"));
    }

    [Fact]
    public void AListUrlThatIsNotHttpIsRejected()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.BlacklistUrl = "ftp://voorbeeld.nl/blacklist.csv";

        Assert.Contains(config.Validate(), e => e.Contains("Blacklist-URL"));
    }

    [Fact]
    public void ListUrlsMayBeEmptyWhenModerationIsOff()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.Enabled = false;
        config.Moderation.WhitelistUrl = "";
        config.Moderation.BlacklistUrl = "";
        config.Moderation.SpotWhitelistUrl = "";
        config.Moderation.SpotBlacklistUrl = "";

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void ListUrlsAreRequiredWhenModerationIsOn()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.WhitelistUrl = "";

        Assert.Contains(config.Validate(), e => e.Contains("Whitelist-URL"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20000)]
    public void AnImpossibleIntervalIsRejected(int minutes)
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.UpdateIntervalMinutes = minutes;

        Assert.Contains(config.Validate(), e => e.Contains("bijwerkinterval"));
    }

    [Fact]
    public void AnIntervalOfZeroIsAllowed()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.UpdateIntervalMinutes = 0;

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void RequiringSignaturesWithoutAKeyIsRejected()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.RequireSignedLists = true;

        Assert.Contains(config.Validate(), e => e.Contains("publieke sleutel"));
    }

    [Fact]
    public void RequiringSignaturesWithAKeyIsAllowed()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.RequireSignedLists = true;
        config.Moderation.SignaturePublicKeyXml = "<RSAKeyValue><Modulus>AA==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void OptionalUrlsMayBeLeftEmpty()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.ModeratorKeysUrl = "";
        config.Services.PromoFolderUrl = "";
        config.Integrations.NewznabBaseUrl = "";

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void AnIndexerIsOnlyUsableWithBothAUrlAndAKey()
    {
        // Standaard leeg: een integratie doet pas iets als de gebruiker hem invult.
        Assert.False(new CommunityIntegrations().IsNewznabConfigured);
        Assert.False(new CommunityIntegrations { NewznabBaseUrl = "https://idx.example" }.IsNewznabConfigured);
        Assert.False(new CommunityIntegrations { NewznabApiKey = "abcdef" }.IsNewznabConfigured);
        Assert.True(new CommunityIntegrations
        {
            NewznabBaseUrl = "https://idx.example",
            NewznabApiKey = "abcdef"
        }.IsNewznabConfigured);
    }

    [Fact]
    public void OmdbIsOffUntilAKeyIsEntered()
    {
        Assert.False(new CommunityIntegrations().IsOmdbConfigured);
        Assert.False(new CommunityIntegrations { OmdbApiKey = "   " }.IsOmdbConfigured);
        Assert.True(new CommunityIntegrations { OmdbApiKey = "abc123" }.IsOmdbConfigured);
    }

    [Fact]
    public void TheRetiredIndexerDefaultsAreNotCarriedOverFromAnOldConfigFile()
    {
        // De oude sectie heette "Indexer" en droeg een onbereikbaar IP plus een sleutel die
        // in de broncode stond. Die mogen niet meeverhuizen naar Integrations.
        // De sleutel staat alleen hier nog letterlijk: de productiecode herkent hem via een
        // SHA-256 en draagt hem niet meer mee. Deze test bewaakt precies dat gedrag, en
        // testcode wordt niet meegeleverd met de applicatie.
        CommunityConfig migrated = CommunityConfig.Deserialize(
            "{\"Indexer\":{\"NewznabBaseUrl\":\"http://51.15.59.166\"," +
            "\"NewznabApiKey\":\"dc08a7bb0371bee90a767a822e68cb07\"}}");

        Assert.Equal("", migrated.Integrations.NewznabBaseUrl);
        Assert.Equal("", migrated.Integrations.NewznabApiKey);
        Assert.False(migrated.Integrations.IsNewznabConfigured);
    }

    [Fact]
    public void AnOwnIndexerFromAnOldConfigFileIsCarriedOver()
    {
        CommunityConfig migrated = CommunityConfig.Deserialize(
            "{\"Indexer\":{\"NewznabBaseUrl\":\"https://eigen.example\",\"NewznabApiKey\":\"mijnsleutel\"}}");

        Assert.Equal("https://eigen.example", migrated.Integrations.NewznabBaseUrl);
        Assert.Equal("mijnsleutel", migrated.Integrations.NewznabApiKey);
        Assert.True(migrated.Integrations.IsNewznabConfigured);
    }

    [Fact]
    public void TheLegacyIndexerSectionIsNotWrittenBackOut()
    {
        CommunityConfig migrated = CommunityConfig.Deserialize(
            "{\"Indexer\":{\"NewznabBaseUrl\":\"https://eigen.example\",\"NewznabApiKey\":\"mijnsleutel\"}}");

        Assert.DoesNotContain("\"Indexer\"", migrated.Serialize());
        Assert.Contains("\"Integrations\"", migrated.Serialize());
    }

    [Fact]
    public void EveryValidationMessageNamesTheFieldItIsAbout()
    {
        CommunityConfig config = new CommunityConfig();
        config.Newsgroups.Spots = "";
        config.Newsgroups.Comments = "";
        config.Moderation.BlacklistUrl = "niet eens een url met spaties";

        List<string> errors = config.Validate().ToList();

        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e)));
    }

    [Fact]
    public void ClassicListUrlsValidationWorks()
    {
        CommunityConfig config = new CommunityConfig();
        config.Moderation.UseClassicLists = true;

        // Valid defaults pass
        Assert.Empty(config.Validate());

        // Empty whitelist URL is caught when UseClassicLists is true
        config.Moderation.ClassicWhitelistUrl = "";
        Assert.Contains(config.Validate(), e => e.Contains("Classic-whitelist-URL"));

        // Invalid URL scheme is caught
        config.Moderation.ClassicWhitelistUrl = "ftp://example.com/whitelist.xml";
        Assert.Contains(config.Validate(), e => e.Contains("Classic-whitelist-URL"));

        config.Moderation.ClassicWhitelistUrl = "https://spotlist.store/spotnet/whitelist.xml";
        config.Moderation.ClassicBlacklistUrl = "not a valid url";
        Assert.Contains(config.Validate(), e => e.Contains("Classic-blacklist-URL"));

        // When UseClassicLists is false, invalid or empty classic URLs are ignored
        config.Moderation.UseClassicLists = false;
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void XmlListFormatCanBeParsedSuccessfully()
    {
        string tempXml = Path.Combine(testFolder, "test_classic.xml");
        string sampleXml = @"<Keys>
    <Key Name=""SpotPoster1"">AAAAB3NzaC1yc2EAAAADAQABAAABAQC12345</Key>
    <Key Name=""SpotPoster2"">AAAAB3NzaC1yc2EAAAADAQABAAABAQC67890</Key>
</Keys>";
        File.WriteAllText(tempXml, sampleXml);

        Assert.True(Spotnet.Model.BlackAndWhite.IsXmlList(tempXml));

        HashSet<string> keys = new HashSet<string>();
        Assert.True(Spotnet.Model.BlackAndWhite.LoadToList(tempXml, keys));
        Assert.Equal(2, keys.Count);
        Assert.Contains("AAAAB3NzaC1yc2EAAAADAQABAAABAQC12345", keys);
        Assert.Contains("AAAAB3NzaC1yc2EAAAADAQABAAABAQC67890", keys);

        List<Spotnet.Model.BlackAndWhite.UserModulusPair> pairs = new List<Spotnet.Model.BlackAndWhite.UserModulusPair>();
        Assert.True(Spotnet.Model.BlackAndWhite.LoadToFakeUsernamesList(tempXml, pairs));
        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, p => p.User == "SpotPoster1" && p.Modulus == "AAAAB3NzaC1yc2EAAAADAQABAAABAQC12345");
        Assert.Contains(pairs, p => p.User == "SpotPoster2" && p.Modulus == "AAAAB3NzaC1yc2EAAAADAQABAAABAQC67890");
    }
}

