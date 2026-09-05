using System;
using System.Configuration;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Xml;
using Spotnet.Deployment;
using Spotnet.Setup;
using Xunit;

namespace Spotnet.Tests;

public sealed class InstallerMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spotnet-setup-tests-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "Legacy");
    private string Target => Path.Combine(_root, "NewProfile");

    public InstallerMigrationTests() { Directory.CreateDirectory(Source); }
    public void Dispose()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void Seed()
    {
        File.WriteAllText(Path.Combine(Source, "servers.xml"), "<Servers><Server Type='Download' Server='example.invalid'/><Server Type='Header'/><Server Type='Upload'/></Servers>");
        File.WriteAllText(Path.Combine(Source, "keys.xml"), "<keys>test-fixture</keys>");
    }

    [Fact]
    public void MeasureCountsExactlyWhatTheCopyWillWrite()
    {
        Seed();
        File.WriteAllBytes(Path.Combine(Source, "spots.dbs"), new byte[3 * 1024 * 1024]);
        File.WriteAllBytes(Path.Combine(Source, "spots.dbs-wal"), new byte[1024 * 1024]);
        // Neither of these is migrated, so neither may count towards the space needed.
        File.WriteAllBytes(Path.Combine(Source, "thumbnail.cache-blob"), new byte[8 * 1024 * 1024]);
        Directory.CreateDirectory(Path.Combine(Source, "Logs"));
        File.WriteAllBytes(Path.Combine(Source, "Logs", "spotnet.txt"), new byte[8 * 1024 * 1024]);

        var estimate = ProfileMigration.Measure(Target, Source, null);
        Assert.True(estimate.Measured);
        Assert.Equal("import", estimate.Kind);
        Assert.Equal(4, estimate.Files); // servers.xml, keys.xml, the database and its WAL
        Assert.InRange(estimate.Bytes, 4L * 1024 * 1024, 4L * 1024 * 1024 + 4096);
        Assert.Equal(estimate.Bytes + ProfileMigration.SafetyMargin, estimate.Required);
        Assert.False(string.IsNullOrEmpty(estimate.Drive));
    }

    [Fact]
    public void MeasurePreservesExistingProfileWithoutCopy()
    {
        new ProfileMigration().Prepare(Target, null, null);
        File.WriteAllBytes(Path.Combine(Target, "Data", "spots.dbs"), new byte[2 * 1024 * 1024]);
        var estimate = ProfileMigration.Measure(Target, null, null);
        Assert.Equal("upgrade", estimate.Kind);
        Assert.Equal(0, estimate.Bytes);
        Assert.Equal(0, estimate.Required);
    }

    [Fact]
    public void MeasureOfAFreshInstallAsksOnlyForTheSafetyMargin()
    {
        var estimate = ProfileMigration.Measure(Target, null, null);
        Assert.Equal("fresh", estimate.Kind);
        Assert.Equal(0, estimate.Files);
        Assert.Equal(ProfileMigration.SafetyMargin, estimate.Required);
    }

    [Fact]
    public void FreshInstallCreatesAnIsolatedMarkedProfile()
    {
        new ProfileMigration().Prepare(Target, null, null);
        Assert.True(File.Exists(Path.Combine(Target, "Data", ProfileMigration.ProfileMarker)));
        var config = ProfileSettingsFile.Load(Path.Combine(Target, "Data", "user.config"));
        Assert.Equal("False", config.SelectSingleNode("//setting[@name='AllowInvalidServerCertificate']/value").InnerText);
        Assert.Empty(Directory.GetFileSystemEntries(Source));
    }

    [Fact]
    public void CopiesDataIncludingWalAndShmWithoutChangingOriginals()
    {
        Seed();
        File.WriteAllBytes(Path.Combine(Source, "provider.dbs"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(Source, "provider.dbs-wal"), new byte[] { 4, 5 });
        File.WriteAllBytes(Path.Combine(Source, "provider.dbs-shm"), new byte[] { 6 });
        Directory.CreateDirectory(Path.Combine(Source, "Filters.v2", "Custom"));
        File.WriteAllText(Path.Combine(Source, "Filters.v2", "Custom", "filters.xml"), "<filters />");
        new ProfileMigration().Prepare(Target, Source, null);
        foreach (string file in Directory.GetFiles(Source, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(Target, "Data", file.Substring(Source.Length + 1))));
    }

    [Fact]
    public void MoveDeletesOnlyFilesThatWereCopiedAndVerified()
    {
        Seed();
        File.WriteAllBytes(Path.Combine(Source, "spots.dbs"), new byte[] { 1, 2, 3 });
        Directory.CreateDirectory(Path.Combine(Source, "cache"));
        File.WriteAllText(Path.Combine(Source, "cache", "thumbnail.bin"), "keep-classic-cache");
        Directory.CreateDirectory(Path.Combine(Source, "Downloader"));
        File.WriteAllText(Path.Combine(Source, "Downloader", "queue"), "keep-active-queue");

        new ProfileMigration().Prepare(Target, Source, null, moveSource: true);
        // A cancelled/failed payload install must still leave Classic intact.
        Assert.True(File.Exists(Path.Combine(Source, "servers.xml")));
        ProfileMigration.CompleteMove(Target, Source, null);
        Assert.True(File.Exists(Path.Combine(Target, "Data", "servers.xml")));
        Assert.True(File.Exists(Path.Combine(Target, "Data", "spots.dbs")));
        Assert.False(File.Exists(Path.Combine(Source, "servers.xml")));
        Assert.False(File.Exists(Path.Combine(Source, "spots.dbs")));
        Assert.True(File.Exists(Path.Combine(Source, "cache", "thumbnail.bin")));
        Assert.True(File.Exists(Path.Combine(Source, "Downloader", "queue")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveRefusesToDeleteAnySourceIfEitherProfileChanged(bool changeTarget)
    {
        Seed();
        new ProfileMigration().Prepare(Target, Source, null, moveSource: true);
        File.AppendAllText(Path.Combine(changeTarget ? Path.Combine(Target, "Data") : Source, "keys.xml"), "changed");
        Assert.Throws<IOException>(() => ProfileMigration.CompleteMove(Target, Source, null));
        Assert.True(File.Exists(Path.Combine(Source, "servers.xml")));
        Assert.True(File.Exists(Path.Combine(Source, "keys.xml")));
    }

    [Fact]
    public void MoveRefusesPathsOutsideSelectedProfile()
    {
        Seed();
        new ProfileMigration().Prepare(Target, Source, null, moveSource: true);
        string planPath = Path.Combine(Target, "classic-move.xml");
        var plan = ProfileSettingsFile.Load(planPath);
        ((XmlElement)plan.DocumentElement.FirstChild).SetAttribute("target", "..\\..\\outside.xml");
        plan.Save(planPath);
        Assert.Throws<IOException>(() => ProfileMigration.CompleteMove(Target, Source, null));
        Assert.True(File.Exists(Path.Combine(Source, "servers.xml")));
    }

    [Fact]
    public void AmbiguousProfilesAreNeverChosenByLastWriteTime()
    {
        var discovery = new LegacyDiscovery();
        discovery.DataPaths.Add(Source);
        discovery.DataPaths.Add(Path.Combine(_root, "AnotherProfile"));
        discovery.SettingsPaths.Add(Path.Combine(_root, "Unrelated", "user.config"));
        Assert.Equal("", discovery.PreferredDataPath);
        Assert.Equal("", discovery.PreferredSettingsPath);
        Assert.Equal("", discovery.SettingsFor(Source));
    }

    [Fact]
    public void RealSqliteDatabaseRemainsReadableAfterCopy()
    {
        Seed();
        string database = Path.Combine(Source, "test.dbs");
        using (var connection = new SQLiteConnection("Data Source=" + database + ";Pooling=False"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE spots(id INTEGER PRIMARY KEY, title TEXT); INSERT INTO spots VALUES(1, 'Preserved');";
                command.ExecuteNonQuery();
            }
        }
        new ProfileMigration().Prepare(Target, Source, null);
        using (var connection = new SQLiteConnection("Data Source=" + Path.Combine(Target, "Data", "test.dbs") + ";Pooling=False"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT title FROM spots WHERE id=1";
                Assert.Equal("Preserved", command.ExecuteScalar());
                command.CommandText = "PRAGMA quick_check";
                Assert.Equal("ok", command.ExecuteScalar());
            }
        }
    }

    [Fact]
    public void ImportedPreferencesResetCertificateBypassButPreserveOtherValues()
    {
        Seed();
        string settings = Path.Combine(_root, "user.config");
        File.WriteAllText(settings, "<configuration><userSettings><Spotnet.Properties.Settings><setting name='UserLanguage' serializeAs='String'><value>nl</value></setting><setting name='AllowInvalidServerCertificate'><value>True</value></setting></Spotnet.Properties.Settings></userSettings></configuration>");
        new ProfileMigration().Prepare(Target, Source, settings);
        var result = ProfileSettingsFile.Load(Path.Combine(Target, "Data", "user.config"));
        Assert.Equal("nl", result.SelectSingleNode("//setting[@name='UserLanguage']/value").InnerText);
        Assert.Equal("False", result.SelectSingleNode("//setting[@name='AllowInvalidServerCertificate']/value").InnerText);
        Assert.Contains("True", File.ReadAllText(settings));
    }

    [Fact]
    public void ExistingThreeProfileIsPreservedNotReimported()
    {
        new ProfileMigration().Prepare(Target, null, null);
        string file = Path.Combine(Target, "Data", "keys.xml");
        File.WriteAllText(file, "personal-key-fixture");
        new ProfileMigration().Prepare(Target, null, null);
        Assert.Equal("personal-key-fixture", File.ReadAllText(file));
        Assert.False(Directory.Exists(Path.Combine(Target, "Backups")));
        Seed();
        Assert.Throws<IOException>(() => new ProfileMigration().Prepare(Target, Source, null));
        Assert.Equal("personal-key-fixture", File.ReadAllText(file));
    }

    [Fact]
    public void RejectsUnknownExistingDestination()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Data"));
        File.WriteAllText(Path.Combine(Target, "Data", "important.txt"), "keep");
        Assert.Throws<IOException>(() => new ProfileMigration().Prepare(Target, null, null));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Target, "Data", "important.txt")));
    }

    [Fact]
    public void LockedSourceFailsBeforePublishingDestination()
    {
        Seed();
        using (var file = new FileStream(Path.Combine(Source, "servers.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Assert.Throws<IOException>(() => new ProfileMigration().Prepare(Target, Source, null));
        Assert.False(Directory.Exists(Path.Combine(Target, "Data")));
        Assert.True(File.Exists(Path.Combine(Source, "servers.xml")));
    }

    [Fact]
    public void MalformedSettingsNeverPublishPartialProfile()
    {
        Seed();
        string file = Path.Combine(_root, "broken.config");
        File.WriteAllText(file, "<invalid>");
        Assert.Throws<XmlException>(() => new ProfileMigration().Prepare(Target, Source, file));
        Assert.False(Directory.Exists(Path.Combine(Target, "Data")));
        Assert.Equal("<invalid>", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(Source, "keys.xml")));
    }

    [Fact]
    public void RejectsOverlappingSourceAndDestination()
    {
        Seed();
        Assert.Throws<IOException>(() => new ProfileMigration().Prepare(Source, Source, null));
        Assert.Throws<IOException>(() => new ProfileMigration().Prepare(Path.Combine(Source, "Nested"), Source, null));
        Assert.Throws<IOException>(() => new ProfileMigration().Prepare(_root, Source, null));
    }

    [Fact]
    public void DoesNotImportLegacyExecutablesCachesOrDownloadQueue()
    {
        Seed();
        File.WriteAllText(Path.Combine(Source, "Spotnet.exe"), "old-code");
        foreach (string folder in new[] { "cache", "Logs", "Downloader" })
        {
            Directory.CreateDirectory(Path.Combine(Source, folder));
            File.WriteAllText(Path.Combine(Source, folder, "state.xml"), "do-not-import");
        }
        new ProfileMigration().Prepare(Target, Source, null);
        Assert.False(File.Exists(Path.Combine(Target, "Data", "Spotnet.exe")));
        foreach (string folder in new[] { "cache", "Logs", "Downloader" })
            Assert.False(Directory.Exists(Path.Combine(Target, "Data", folder)));
    }

    [Fact]
    public void RejectsUnsupportedLegacyServerShape()
    {
        File.WriteAllText(Path.Combine(Source, "servers.xml"), "<Servers><Server Type='Unknown' /></Servers>");
        Assert.Throws<InvalidDataException>(() => new ProfileMigration().Prepare(Target, Source, null));
        Assert.False(Directory.Exists(Path.Combine(Target, "Data")));
    }

    [Fact]
    public void SettingsParserRejectsDtds()
    {
        string file = Path.Combine(_root, "dtd.config");
        File.WriteAllText(file, "<!DOCTYPE configuration [<!ENTITY secret SYSTEM 'file:///not-to-be-read'>]><configuration>&secret;</configuration>");
        Assert.Throws<XmlException>(() => ProfileSettingsFile.Load(file));
    }

    [Fact]
    public void ImportsPortableSettingsAndDropsConfigTypeDeclarations()
    {
        var portable = new XmlDocument();
        portable.LoadXml("<Settings><UserLanguage>nl</UserLanguage></Settings>");
        Assert.Equal("nl", ProfileSettingsFile.Normalize(portable).SelectSingleNode("//value").InnerText);
        var standard = new XmlDocument();
        standard.LoadXml("<configuration><configSections><section name='evil' type='Untrusted.Type'/></configSections><userSettings><Spotnet.Properties.Settings /></userSettings></configuration>");
        Assert.Null(ProfileSettingsFile.Normalize(standard).SelectSingleNode("//configSections"));
    }

    [Fact]
    public void StableSettingsProviderRoundTripsAndKeepsPreviousFile()
    {
        string file = Path.Combine(_root, "stable", "user.config");
        var provider = new InstalledSettingsProvider(file);
        provider.Initialize(null, null);
        var property = new SettingsProperty("UserLanguage") { PropertyType = typeof(string), DefaultValue = "en", SerializeAs = SettingsSerializeAs.String, Provider = provider };
        var properties = new SettingsPropertyCollection { property };
        Assert.Equal("en", provider.GetPropertyValues(new SettingsContext(), properties)["UserLanguage"].PropertyValue);
        var values = new SettingsPropertyValueCollection { new SettingsPropertyValue(property) { PropertyValue = "nl" } };
        provider.SetPropertyValues(new SettingsContext(), values);
        Assert.Equal("nl", provider.GetPropertyValues(new SettingsContext(), properties)["UserLanguage"].PropertyValue);
        values["UserLanguage"].PropertyValue = "en";
        provider.SetPropertyValues(new SettingsContext(), values);
        Assert.True(File.Exists(file + ".previous"));
        Assert.Contains("nl", File.ReadAllText(file + ".previous"));
    }

    [Fact]
    public void AllCurrentApplicationSettingsUseSupportedSerialization()
    {
        var settings = new Spotnet.Properties.Settings();
        foreach (SettingsProperty property in settings.Properties)
            Assert.Equal(SettingsSerializeAs.String, property.SerializeAs);
    }

    [Fact]
    public void ApplicationSettingsActuallyUseTheStableProvider()
    {
        string file = Path.Combine(_root, "installed", "user.config");
        var settings = Spotnet.Properties.Settings.CreateInstalled(file);
        settings.UserLanguage = "en";
        settings.DownloadFolder = Path.Combine(_root, "Downloads");
        settings.DownloaderRetries = 7;
        settings.Save();
        var restored = Spotnet.Properties.Settings.CreateInstalled(file);
        Assert.Equal("en", restored.UserLanguage);
        Assert.Equal(settings.DownloadFolder, restored.DownloadFolder);
        Assert.Equal(7, restored.DownloaderRetries);
    }

    [Fact]
    public void DetectsMultipleProfilesWithoutReadingCredentialsIntoReport()
    {
        string local = Path.Combine(_root, "Local");
        string common = Path.Combine(_root, "Common");
        foreach (string folder in new[] { Path.Combine(local, "Spotnet", "Data"), Path.Combine(common, "Spotnet") })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "servers.xml"), "<Servers Password='must-not-appear'/>");
        }
        string config = Path.Combine(local, "Spotnet", "Spotnet.exe_Url_fixture", "2.0.0.284");
        Directory.CreateDirectory(config);
        ProfileSettingsFile.Empty().Save(Path.Combine(config, "user.config"));
        var discovery = LegacyDiscovery.Detect(local, Path.Combine(_root, "Roaming"), common, false);
        Assert.Equal(2, discovery.DataPaths.Count);
        Assert.False(discovery.ClassicAvailable); // Orphaned data/configuration is not an installed application.
        Assert.Single(discovery.SettingsPaths);
        string report = Path.Combine(_root, "detection.ini");
        discovery.SaveIni(report);
        Assert.DoesNotContain("must-not-appear", File.ReadAllText(report));
    }

    [Theory]
    [InlineData("Spotnet", "2.0.0.284", true)]
    [InlineData("Spotnet 1.8", "1.8.6", true)]
    [InlineData("Spotnet 2.0", "2.0.0.284", true)]
    [InlineData("Spotnet 3.0 (64-bit)", "3.0.6", false)]
    [InlineData("Spotnet", "3.0.6", false)]
    [InlineData("Another program", "2.0", false)]
    public void DistinguishesClassicFromThree(string displayName, string version, bool expected)
    {
        Assert.Equal(expected, LegacyDiscovery.IsClassicInstallation(displayName, version));
    }

    [Fact]
    public void LeftoverDataAloneDoesNotEnableClassicMigrationChoices()
    {
        Seed();
        var discovery = new LegacyDiscovery();
        discovery.DataPaths.Add(Source);
        Assert.False(discovery.ClassicAvailable);
        discovery.Installations.Add("Spotnet 2.0 2.0.0.284");
        Assert.True(discovery.ClassicAvailable);
        string report = Path.Combine(_root, "classic-detection.ini");
        discovery.SaveIni(report);
        string text = File.ReadAllText(report);
        Assert.Contains("ClassicAvailable=1", text);
        Assert.Contains("ClassicData=" + Source, text);
    }

    [Fact]
    public void ShutdownWaitReturnsWhenProcessExits()
    {
        int polls = 0;
        GracefulShutdown.WaitUntilClosed(() => ++polls < 2, TimeSpan.FromSeconds(2));
        Assert.Equal(2, polls);
    }

    [Fact]
    public void ShutdownTimeoutStopsInsteadOfForcingTermination()
    {
        Assert.Throws<IOException>(() => GracefulShutdown.WaitUntilClosed(() => true, TimeSpan.Zero));
    }
}
