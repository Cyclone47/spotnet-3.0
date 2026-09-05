using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Xml.Linq;
using Spotnet.Mac.Network;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>An in-memory keychain, so a test never touches the real one.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string GetSecret(string key) => _secrets.TryGetValue(key, out var v) ? v : null!;
    public void SetSecret(string key, string secret) => _secrets[key] = secret;
    public bool DeleteSecret(string key) => _secrets.Remove(key);
}

/// <summary>Points the app paths at a throwaway directory.</summary>
internal sealed class TempAppPaths : IAppPaths, IDisposable
{
    public TempAppPaths()
    {
        DataFolder = Path.Combine(Path.GetTempPath(), $"spotnet_profile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }
    public string CacheFolder => DataFolder;
    public string LogsFolder => DataFolder;
    public string FiltersFolder => DataFolder;
    public string DownloadsFolder => DataFolder;
    public string TempFolder => DataFolder;

    public string GetDatabasePath(string serverAddress) => Path.Combine(DataFolder, serverAddress + ".db");

    public string GetTempFileName(string ext = null!, string filename = null!) =>
        Path.Combine(DataFolder, (filename ?? Guid.NewGuid().ToString("N")) + (ext ?? ".tmp"));

    public void EnsureDirectoriesExist() { }

    public void Dispose()
    {
        try { Directory.Delete(DataFolder, recursive: true); } catch (IOException) { }
    }
}

/// <summary>
/// servers.xml carries one entry per role — providers give headers, downloading and
/// posting different hostnames. The client used to take whichever entry came first and
/// use it for everything.
/// </summary>
public class ServerProfileTests
{
    private static void WriteServers(TempAppPaths paths, params (string type, string host)[] entries)
    {
        var root = new XElement("Spotnet");
        foreach (var (type, host) in entries)
        {
            root.Add(new XElement("Server",
                new XAttribute("Type", type),
                new XAttribute("Server", host),
                new XAttribute("Port", 563),
                new XAttribute("SSL", "1"),
                new XAttribute("Connections", 20),
                new XAttribute("Username", "someone")));
        }
        new XDocument(root).Save(Path.Combine(paths.DataFolder, "servers.xml"));
    }

    [Fact]
    public void Each_role_gets_its_own_server()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths,
            ("Uploads", "upload.eweka.nl"),
            ("Headers", "textnews.eweka.nl"),
            ("Downloads", "newsreader1.eweka.nl"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());

        Assert.Equal("textnews.eweka.nl", profile.Get(ServerRole.Headers)!.Server);
        Assert.Equal("newsreader1.eweka.nl", profile.Get(ServerRole.Download)!.Server);
        Assert.Equal("upload.eweka.nl", profile.Get(ServerRole.Upload)!.Server);
    }

    [Fact]
    public void The_upload_entry_coming_first_no_longer_hijacks_header_sync()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths, ("Uploads", "upload.eweka.nl"), ("Headers", "textnews.eweka.nl"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());

        // The old loader read Root.Element("Server") and would have returned the
        // upload host here.
        Assert.Equal("textnews.eweka.nl", profile.Get(ServerRole.Headers)!.Server);
    }

    [Fact]
    public void A_single_server_profile_serves_every_role()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths, ("Headers", "news.newshosting.com"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());

        // This is what the macOS client has written until now, so downloading and
        // posting have to keep working against it.
        Assert.Equal("news.newshosting.com", profile.Get(ServerRole.Headers)!.Server);
        Assert.Equal("news.newshosting.com", profile.Get(ServerRole.Download)!.Server);
        Assert.Equal("news.newshosting.com", profile.Get(ServerRole.Upload)!.Server);
    }

    [Fact]
    public void The_download_server_is_preferred_as_a_stand_in()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths, ("Uploads", "upload.example.com"), ("Downloads", "reader.example.com"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());

        Assert.Equal("reader.example.com", profile.Get(ServerRole.Headers)!.Server);
    }

    [Theory]
    [InlineData("Headers", ServerRole.Headers)]
    [InlineData("header", ServerRole.Headers)]
    [InlineData("  HEADERS  ", ServerRole.Headers)]
    [InlineData("", ServerRole.Headers)]
    [InlineData("Downloads", ServerRole.Download)]
    [InlineData("download", ServerRole.Download)]
    [InlineData("Uploads", ServerRole.Upload)]
    [InlineData("UPLOAD", ServerRole.Upload)]
    public void Type_is_read_the_way_the_Windows_loader_reads_it(string type, ServerRole expected)
    {
        Assert.Equal(expected, ServerProfile.ParseRole(type));
    }

    [Fact]
    public void An_unknown_type_is_skipped_rather_than_guessed()
    {
        Assert.Null(ServerProfile.ParseRole("MasterCache"));

        using var paths = new TempAppPaths();
        WriteServers(paths, ("MasterCache", "cache.example.com"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());
        Assert.False(profile.IsConfigured);
        Assert.Null(profile.Get(ServerRole.Headers));
    }

    [Fact]
    public void Each_server_gets_its_own_keychain_entry()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths, ("Headers", "textnews.eweka.nl"), ("Uploads", "upload.eweka.nl"));

        var store = new FakeSecretStore();
        store.SetSecret(ServerProfile.SecretKey("textnews.eweka.nl", "someone"), "reader-pass");
        store.SetSecret(ServerProfile.SecretKey("upload.eweka.nl", "someone"), "poster-pass");

        var profile = ServerProfile.Load(paths, store);

        Assert.Equal("reader-pass", profile.Get(ServerRole.Headers)!.Password);
        Assert.Equal("poster-pass", profile.Get(ServerRole.Upload)!.Password);
    }

    [Fact]
    public void An_entry_without_a_hostname_is_ignored()
    {
        using var paths = new TempAppPaths();
        WriteServers(paths, ("Headers", "   "), ("Downloads", "reader.example.com"));

        var profile = ServerProfile.Load(paths, new FakeSecretStore());

        Assert.Equal(new[] { ServerRole.Download }, profile.ConfiguredRoles.ToArray());
    }

    [Fact]
    public void A_missing_or_broken_file_gives_an_empty_profile_rather_than_throwing()
    {
        using var paths = new TempAppPaths();
        var store = new FakeSecretStore();

        Assert.False(ServerProfile.Load(paths, store).IsConfigured);

        File.WriteAllText(Path.Combine(paths.DataFolder, "servers.xml"), "<Spotnet><Server");
        Assert.False(ServerProfile.Load(paths, store).IsConfigured);
    }
}

/// <summary>
/// Certificate handling. The client accepted every certificate unconditionally, which
/// let anything able to intercept the connection read the AUTHINFO credentials.
/// </summary>
public class CertificateValidationTests
{
    [Fact]
    public void A_valid_certificate_is_accepted()
    {
        Assert.True(NntpClient.ValidateCertificate("news.example.com", SslPolicyErrors.None, allowInvalid: false));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void An_invalid_certificate_is_refused_by_default(SslPolicyErrors errors)
    {
        Assert.False(NntpClient.ValidateCertificate("news.example.com", errors, allowInvalid: false));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    public void An_invalid_certificate_is_accepted_only_when_the_user_asked_for_it(SslPolicyErrors errors)
    {
        Assert.True(NntpClient.ValidateCertificate("news.example.com", errors, allowInvalid: true));
    }
}

/// <summary>
/// The SOCKS5 proxy settings. Spotnet.Core already carried a working Socks5Client; it
/// was simply never wired to anything on macOS.
/// </summary>
public class ProxySettingsTests
{
    [Fact]
    public void No_proxy_when_the_switch_is_off()
    {
        Assert.Null(ProxySettings.Create(enabled: false, "127.0.0.1", 1080, "", ""));
    }

    [Fact]
    public void An_enabled_proxy_with_a_host_is_usable()
    {
        var proxy = ProxySettings.Create(enabled: true, " 127.0.0.1 ", 1080, " tor ", "secret");

        Assert.NotNull(proxy);
        Assert.Equal("127.0.0.1", proxy!.Host);
        Assert.Equal("tor", proxy.Username);
        Assert.Equal(1080, proxy.Port);
    }

    [Theory]
    [InlineData("", 1080)]
    [InlineData("   ", 1080)]
    [InlineData("127.0.0.1", 0)]
    [InlineData("127.0.0.1", 70000)]
    public void An_incomplete_proxy_reads_as_no_proxy_rather_than_failing_the_connection(string host, int port)
    {
        Assert.Null(ProxySettings.Create(enabled: true, host, port, "", ""));
    }

    [Fact]
    public void The_password_never_appears_in_a_log_line()
    {
        var proxy = ProxySettings.Create(enabled: true, "proxy.example.com", 1080, "user", "hunter2");

        string rendered = proxy!.ToString();
        Assert.DoesNotContain("hunter2", rendered, StringComparison.Ordinal);
        Assert.Equal("socks5://user@proxy.example.com:1080", rendered);
    }

    [Fact]
    public void The_password_comes_from_the_keychain_not_from_preferences()
    {
        var store = new FakeSecretStore();
        store.SetSecret(ProxySettings.SecretKey, "from-keychain");

        var prefs = new Spotnet.Mac.Services.UserPreferences
        {
            UseSocksProxy = true,
            SocksProxyHost = "proxy.example.com",
            SocksProxyPort = 9050,
            SocksProxyUsername = "user",
        };

        var proxy = ProxySettings.FromPreferences(prefs, store);

        Assert.Equal("from-keychain", proxy!.Password);
    }
}
