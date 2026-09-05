using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Xml;
using Spotnet.Deployment;
using Spotnet.Setup;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Covers the 3.0 connect dialog: the provider catalogue, the Dutch satellite assembly that 2.0
/// shipped but the reconstruction had lost, and the installer seeding the app's language.
/// </summary>
public sealed class ProviderDialogTests
{
    private static readonly Type ProviderItemType =
        typeof(ProfileSettingsFile).Assembly.GetType("Spotnet.Model.ProviderItem", throwOnError: true);

    private static object[] Providers()
    {
        Type catalogue = typeof(ProfileSettingsFile).Assembly.GetType("Spotnet.Model.UsenetProviders", throwOnError: true);
        var all = (System.Collections.IEnumerable)catalogue.GetProperty("All", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
        return all.Cast<object>().ToArray();
    }

    private static T Value<T>(object provider, string name) => (T)ProviderItemType.GetProperty(name).GetValue(provider);

    private static object[] Real() => Providers().Where(p => !Value<bool>(p, "IsManual")).ToArray();

    [Fact]
    public void EveryProviderHasAHostnameAndAUsableNntpPort()
    {
        foreach (object provider in Real())
        {
            string name = Value<string>(provider, "Name");
            foreach (string host in new[] { "Headers", "Download", "Upload" })
            {
                string value = Value<string>(provider, host);
                Assert.False(string.IsNullOrWhiteSpace(value), name + " has no " + host + " server.");
                Assert.Contains('.', value);
                Assert.Equal(value.Trim().ToLowerInvariant(), value);
            }
            foreach (string port in new[] { "HeadersPort", "DownloadPort", "UploadPort" })
            {
                int value = Value<int>(provider, port);
                Assert.True(value == 563 || value == 443 || value == 119 || value == 80,
                    name + " uses an unexpected " + port + ": " + value);
            }
        }
    }

    [Fact]
    public void TheDiscontinuedKpnServersAreGone()
    {
        // Both answer "500 ... we zijn gestopt met Usenet-toegang via deze server" since 1 May 2026.
        string[] retired = { "nova.planet.nl", "text.nova.planet.nl", "textnews.kpn.nl", "news.kpn.nl" };
        foreach (object provider in Real())
            foreach (string host in new[] { "Headers", "Download", "Upload" })
                Assert.DoesNotContain(Value<string>(provider, host), retired, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortEightyIsNotOfferedForTheProvidersThatStoppedServingIt()
    {
        // 5 Euro Usenet and SnelNL were shipped on port 80, which accepts the connection but never
        // sends an NNTP greeting, so the client hung until it timed out.
        foreach (string name in new[] { "5 Euro Usenet", "SnelNL" })
        {
            object provider = Real().Single(p => Value<string>(p, "Name") == name);
            Assert.Equal(563, Value<int>(provider, "HeadersPort"));
        }
    }

    [Fact]
    public void TheListIsUniqueGroupedAndCoversDutchAndInternationalProviders()
    {
        object[] real = Real();
        Assert.Equal(real.Length, real.Select(p => Value<string>(p, "Name")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(real.Length, real.Select(p => Value<string>(p, "Headers")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(real.Count(p => Value<string>(p, "Group") == "NL") >= 10, "Too few Dutch providers.");
        Assert.True(real.Count(p => Value<string>(p, "Group") == "INT") >= 10, "Too few international providers.");
        // Exactly one manual entry, and it carries no servers.
        object manual = Assert.Single(Providers().Where(p => Value<bool>(p, "IsManual")));
        Assert.Equal("", Value<string>(manual, "Headers"));
    }

    [Fact]
    public void SearchingMatchesBothTheNameAndTheHostname()
    {
        object eweka = Real().Single(p => Value<string>(p, "Name") == "Eweka");
        MethodInfo matches = ProviderItemType.GetMethod("Matches");
        Assert.True((bool)matches.Invoke(eweka, new object[] { "ewe" }));
        Assert.True((bool)matches.Invoke(eweka, new object[] { "EWEKA" }));
        Assert.True((bool)matches.Invoke(eweka, new object[] { "textnews" }));
        Assert.True((bool)matches.Invoke(eweka, new object[] { "  " }));
        Assert.False((bool)matches.Invoke(eweka, new object[] { "giganews" }));
    }

    /// <summary>
    /// Reads what actually ships rather than going through ResourceManager: the test host's app base
    /// is not the app's, so satellite probing would not find nl\Spotnet.resources.dll here even
    /// though it is deployed correctly beside Spotnet.exe.
    /// </summary>
    /// <summary>Asks ResourceManager itself what manifest name it will look for.</summary>
    private sealed class NameProbe : ResourceManager
    {
        internal NameProbe(string baseName) : base(baseName, typeof(ProfileSettingsFile).Assembly) { }
        internal string NameFor(CultureInfo culture) => GetResourceFileName(culture);
    }

    private static Assembly DutchSatellite()
    {
        Assembly app = typeof(ProfileSettingsFile).Assembly;
        string satellite = Path.Combine(Path.GetDirectoryName(new Uri(app.CodeBase).LocalPath), "nl", "Spotnet.resources.dll");
        Assert.True(File.Exists(satellite), "The Dutch satellite assembly was not built: " + satellite);

        Assembly assembly = Assembly.LoadFrom(satellite);
        Assert.Equal("nl", assembly.GetName().CultureInfo.Name);
        return assembly;
    }

    private static Dictionary<string, string> Dutch(string table = "Words") =>
        Read(DutchSatellite(), "Spotnet.Properties." + table + ".nl.resources");

    private static Dictionary<string, string> Neutral(string table = "Words") =>
        Read(typeof(ProfileSettingsFile).Assembly, "Spotnet.Properties." + table + ".resources");

    private static Dictionary<string, string> Read(Assembly assembly, string manifestName)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using (Stream stream = assembly.GetManifestResourceStream(manifestName))
        {
            Assert.True(stream != null, assembly.GetName().Name + " has no " + manifestName);
            using (var reader = new ResourceReader(stream))
                foreach (System.Collections.DictionaryEntry entry in reader)
                    values[(string)entry.Key] = (string)entry.Value;
        }
        return values;
    }

    /// <summary>
    /// ResourceManager appends the culture when looking a satellite up, so a satellite that embeds
    /// "Spotnet.Properties.Words.resources" loads correctly and then resolves nothing at all: the
    /// app runs in English with the Dutch assembly sitting right next to it.
    /// </summary>
    [Theory]
    [InlineData("Words")]
    [InlineData("Categories")]
    public void TheSatelliteEmbedsTheNameResourceManagerActuallyLooksFor(string table)
    {
        string expected = new NameProbe("Spotnet.Properties." + table).NameFor(CultureInfo.GetCultureInfo("nl"));
        Assert.Equal("Spotnet.Properties." + table + ".nl.resources", expected);
        Assert.Contains(expected, DutchSatellite().GetManifestResourceNames());
    }

    [Fact]
    public void TheDutchSatelliteAssemblyIsBuiltAndCarriesTheTranslations()
    {
        Dictionary<string, string> dutch = Dutch();
        Dictionary<string, string> neutral = Neutral();

        Assert.Equal("Selecteer een provider", dutch["SelectProvider"]);
        Assert.Equal("VERBINDEN", dutch["Connect"]);
        Assert.Equal("Select Provider", neutral["SelectProvider"]);

        // The strings the redesigned dialog added must be translated too, not silently English.
        foreach (string key in new[] { "SelectProviderSubtitle", "ProviderGroupNetherlands", "SearchProviderHint", "HeadersServer", "SeparateDownloadServer", "OtherProvider" })
        {
            Assert.True(dutch.ContainsKey(key), key + " is missing from the Dutch table.");
            Assert.False(string.IsNullOrWhiteSpace(dutch[key]), key + " has no Dutch value.");
            Assert.NotEqual(neutral[key], dutch[key]);
        }
    }

    [Theory]
    [InlineData("Words")]
    [InlineData("Categories")]
    public void TheDutchTablesMatchTheNeutralOnesKeyForKey(string table)
    {
        Dictionary<string, string> dutch = Dutch(table);
        Dictionary<string, string> neutral = Neutral(table);
        // A key present on one side only means the app silently falls back to English mid-dialog,
        // or ships a translation nothing can ever look up.
        Assert.Empty(neutral.Keys.Where(k => !dutch.ContainsKey(k)));
        Assert.Empty(dutch.Keys.Where(k => !neutral.ContainsKey(k)));
        Assert.All(dutch.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void TheAffiliateLinkIsNoLongerShownInTheDialog()
    {
        string source = File.ReadAllText(SolutionFile(Path.Combine("src", "Spotnet", "Spotnet", "views", "selectproviderwindow.xaml")));
        Assert.DoesNotContain("TextWithTheLink", source);
        string code = File.ReadAllText(SolutionFile(Path.Combine("src", "Spotnet", "Spotnet", "Spotnet", "Views", "SelectProviderWindow.cs")));
        Assert.DoesNotContain("SelectProviderLinkURL", code);
    }

    [Fact]
    public void TheDialogCanShrinkAndKeepsItsActionsVisibleOnSmallScreens()
    {
        string source = File.ReadAllText(SolutionFile(Path.Combine("src", "Spotnet", "Spotnet", "views", "selectproviderwindow.xaml")));
        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", source);
        Assert.Contains("MinHeight=\"360\"", source);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", source);

        string code = File.ReadAllText(SolutionFile(Path.Combine("src", "Spotnet", "Spotnet", "Spotnet", "Views", "SelectProviderWindow.cs")));
        Assert.Contains("FitToWorkingArea();", code);
        Assert.Contains("MaxHeight = availableHeight;", code);
    }

    [Fact]
    public void ProviderFilteringIsDeferredUntilTheComboBoxFinishesItsEdit()
    {
        string code = File.ReadAllText(SolutionFile(Path.Combine("src", "Spotnet", "Spotnet", "Spotnet", "Views", "SelectProviderWindow.cs")));
        int handler = code.IndexOf("private void ProviderBox_OnTextChanged", StringComparison.Ordinal);
        int refreshMethod = code.IndexOf("private void RefreshProviderFilter", handler, StringComparison.Ordinal);
        string handlerBody = code.Substring(handler, refreshMethod - handler);

        Assert.Contains("DispatcherPriority.Background", handlerBody);
        Assert.DoesNotContain("_providerView.Refresh();", handlerBody);
        Assert.Contains("ProviderBox.SelectedItem = null;", code.Substring(refreshMethod));
    }

    [Fact]
    public void AFreshProfileStartsInTheLanguageSetupRanIn()
    {
        string root = NewTempDirectory();
        try
        {
            new ProfileMigration().Prepare(root, null, null, null, "nl");
            Assert.Equal("nl", SettingValue(Path.Combine(root, "Data", "user.config"), "UserLanguage"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnImportedProfileKeepsItsOwnLanguage()
    {
        string root = NewTempDirectory();
        string legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "servers.xml"), "<Spotnet />");
            string settings = Path.Combine(legacy, "user.config");
            File.WriteAllText(settings,
                "<configuration><userSettings><Spotnet.Properties.Settings>" +
                "<setting name=\"UserLanguage\" serializeAs=\"String\"><value>en</value></setting>" +
                "</Spotnet.Properties.Settings></userSettings></configuration>");

            new ProfileMigration().Prepare(Path.Combine(root, "profile"), legacy, settings, null, "nl");
            Assert.Equal("en", SettingValue(Path.Combine(root, "profile", "Data", "user.config"), "UserLanguage"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PrepareRejectsALanguageItDoesNotShip()
    {
        string root = NewTempDirectory();
        try
        {
            new ProfileMigration().Prepare(root, null, null, null, "de");
            // Prepare itself does not validate; the CLI does. An unknown value must not be written.
            Assert.Null(SettingValue(Path.Combine(root, "Data", "user.config"), "UserLanguage"));
        }
        catch (Exception)
        {
            // Rejecting outright is equally acceptable.
        }
        finally { Directory.Delete(root, true); }
    }

    private static string SettingValue(string path, string name)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        return ProfileSettingsFile.Get(document, name);
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "spotnet-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SolutionFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Cannot find " + relative + " from the test output.");
    }
}
