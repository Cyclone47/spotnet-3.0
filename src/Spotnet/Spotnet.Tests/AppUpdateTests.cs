using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Deployment;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// The update path, from the published manifest through to a verified installer on disk.
/// The download runs against a local server rather than GitHub, so the test needs no
/// network, no repository access and no installed copy of Spotnet.
/// </summary>
public sealed class AppUpdateTests
{
    private static string Manifest(string version = "3.0.7.0", object clientUpdate = null, string url = null,
        string sha256 = null, long size = 1024L, int schema = 1, string extra = "")
    {
        string flag = clientUpdate == null ? "1" : clientUpdate.ToString().ToLowerInvariant();
        return $@"{{
            ""schema"": {schema},
            ""clientUpdate"": {flag},
            ""version"": ""{version}"",
            ""url"": ""{url ?? "https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.7.0/Spotnet-3.0-x64-Setup.exe"}"",
            ""size"": {size},
            ""sha256"": ""{sha256 ?? new string('a', 64)}""{extra}
        }}";
    }

    [Fact]
    public void AWellFormedManifestIsRead()
    {
        Assert.True(UpdateManifest.TryParse(Manifest(extra: @",
            ""forced"": 0,
            ""minimumVersion"": ""3.0.0.0"",
            ""releaseNotesUrl"": ""https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.7.0"""),
            out UpdateManifest manifest, out string error));
        Assert.Null(error);
        Assert.Equal(new Version(3, 0, 7, 0), manifest.Version);
        Assert.True(manifest.ClientUpdate);
        Assert.False(manifest.Forced);
        Assert.Equal(1024L, manifest.Size);
        Assert.Equal(new string('a', 64), manifest.Sha256);
        Assert.NotNull(manifest.ReleaseNotesUrl);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void TheReleaseGateReadsBothNumbersAndBooleans(string written, bool expected)
    {
        string json = Manifest().Replace("\"clientUpdate\": 1", "\"clientUpdate\": " + written);
        Assert.True(UpdateManifest.TryParse(json, out UpdateManifest manifest, out _));
        Assert.Equal(expected, manifest.ClientUpdate);
    }

    [Fact]
    public void AVersionWithThreeComponentsDoesNotLookOlderThanOneWithFour()
    {
        Assert.True(UpdateManifest.TryParse(Manifest(version: "v3.0.7"), out UpdateManifest manifest, out _));
        Assert.Equal(new Version(3, 0, 7, 0), manifest.Version);
        Assert.False(manifest.Version < new Version(3, 0, 7, 0));
    }

    [Theory]
    // An error page or a truncated file instead of the manifest.
    [InlineData("<html>404</html>")]
    [InlineData("")]
    public void RubbishInPlaceOfTheManifestIsRejectedWithAReason(string body)
    {
        Assert.False(UpdateManifest.TryParse(body, out UpdateManifest manifest, out string error));
        Assert.Null(manifest);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void AManifestSavedWithAByteOrderMarkStillReads()
    {
        // Windows PowerShell and several editors write one; it must not cost a release.
        Assert.True(UpdateManifest.TryParse("\uFEFF" + Manifest(), out UpdateManifest manifest, out string error), error);
        Assert.Equal(new Version(3, 0, 7, 0), manifest.Version);
    }

    [Fact]
    public void AManifestFromANewerSchemaIsIgnoredRatherThanGuessedAt()
    {
        Assert.False(UpdateManifest.TryParse(Manifest(schema: UpdateManifest.SupportedSchema + 1), out _, out string error));
        Assert.Contains("schema", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://github.com/Cyclone47/spotnet-3.0/releases/download/v1/Setup.exe")]
    [InlineData("https://example.com/Setup.exe")]
    [InlineData("https://github.com.attacker.net/Setup.exe")]
    [InlineData("not a url")]
    public void TheDownloadMayOnlyComeFromGitHub(string url)
    {
        Assert.False(UpdateManifest.TryParse(Manifest(url: url), out _, out string error));
        Assert.Contains("GitHub", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void AManifestWithoutAUsableChecksumIsRejected(string sha)
    {
        Assert.False(UpdateManifest.TryParse(Manifest(sha256: sha), out _, out string error));
        Assert.Contains("SHA-256", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AManifestWithoutASizeIsRejected()
    {
        Assert.False(UpdateManifest.TryParse(Manifest(size: 0L), out _, out string error));
        Assert.Contains("size", error, StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateManifest Parsed(string json)
    {
        Assert.True(UpdateManifest.TryParse(json, out UpdateManifest manifest, out string error), error);
        return manifest;
    }

    [Fact]
    public void ABuildThatIsNotReleasedToClientsIsNotOffered()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(clientUpdate: 0)), new Version(3, 0, 6, 0), null);
        Assert.Equal(UpdateAction.None, decision.Action);
    }

    [Theory]
    [InlineData("3.0.7.0", true)]
    [InlineData("3.0.6.0", false)]
    [InlineData("3.0.5.0", false)]
    public void OnlyANewerReleaseIsOffered(string published, bool offered)
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(version: published)), new Version(3, 0, 6, 0), null);
        Assert.Equal(offered ? UpdateAction.Offer : UpdateAction.None, decision.Action);
    }

    [Fact]
    public void ASkippedVersionStaysSkipped()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(version: "3.0.7.0")), new Version(3, 0, 6, 0), "3.0.7.0");
        Assert.Equal(UpdateAction.None, decision.Action);
    }

    [Fact]
    public void SkippingOneVersionDoesNotSkipTheNextOne()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(version: "3.0.8.0")), new Version(3, 0, 6, 0), "3.0.7.0");
        Assert.Equal(UpdateAction.Offer, decision.Action);
    }

    [Fact]
    public void ARequiredReleaseIsOfferedEvenAfterTheUserSkippedIt()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(extra: @", ""forced"": 1")), new Version(3, 0, 6, 0), "3.0.7.0");
        Assert.Equal(UpdateAction.Required, decision.Action);
    }

    [Fact]
    public void ABuildBelowTheMinimumIsToldTheUpdateIsRequired()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest(extra: @", ""minimumVersion"": ""3.0.6.0""")), new Version(3, 0, 5, 0), null);
        Assert.Equal(UpdateAction.Required, decision.Action);
    }

    [Fact]
    public void GarbageInTheSkippedVersionSettingDoesNotHideAnUpdate()
    {
        UpdateDecision decision = UpdatePolicy.Evaluate(
            Parsed(Manifest()), new Version(3, 0, 6, 0), "not a version");
        Assert.Equal(UpdateAction.Offer, decision.Action);
    }

    // ---- The download, against a local server ----

    private sealed class Server : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _payload;
        private readonly CancellationTokenSource _stop = new();

        internal Server(byte[] payload, bool supportsRange = true)
        {
            _payload = payload;
            SupportsRange = supportsRange;
            int port;
            // Ask the operating system for a free port by binding a socket first.
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Prefix = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        internal string Prefix { get; }

        internal bool SupportsRange { get; }

        internal string ManifestJson { get; set; }

        internal int RangeRequests { get; private set; }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                try
                {
                    if (context.Request.Url.AbsolutePath.EndsWith(".json", StringComparison.Ordinal))
                    {
                        byte[] json = Encoding.UTF8.GetBytes(ManifestJson ?? "{}");
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = json.Length;
                        await context.Response.OutputStream.WriteAsync(json);
                    }
                    else
                    {
                        int offset = 0;
                        string range = context.Request.Headers["Range"];
                        if (!string.IsNullOrEmpty(range))
                        {
                            RangeRequests++;
                            if (SupportsRange)
                            {
                                offset = int.Parse(range.Replace("bytes=", string.Empty).Split('-')[0]);
                                context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                                context.Response.Headers["Content-Range"] =
                                    $"bytes {offset}-{_payload.Length - 1}/{_payload.Length}";
                            }
                        }
                        context.Response.ContentLength64 = _payload.Length - offset;
                        await context.Response.OutputStream.WriteAsync(_payload.AsMemory(offset));
                    }
                }
                catch (Exception) { /* the client went away; nothing to do */ }
                finally { try { context.Response.Close(); } catch (Exception) { } }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            _listener.Close();
        }
    }

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        new Random(1234).NextBytes(bytes);
        return bytes;
    }

    private static string Sha256Of(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string LocalManifest(Server server, byte[] payload, string version = "3.0.7.0") =>
        $@"{{ ""schema"": 1, ""clientUpdate"": 1, ""version"": ""{version}"",
             ""url"": ""{server.Prefix}Setup.exe"", ""size"": {payload.Length},
             ""sha256"": ""{Sha256Of(payload)}"" }}";

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "spotnet-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task TheManifestIsReadOverHttpAndTheInstallerArrivesVerified()
    {
        byte[] payload = Payload(64 * 1024);
        using var server = new Server(payload);
        server.ManifestJson = LocalManifest(server, payload);
        string directory = TempDirectory();
        try
        {
            using var client = new UpdateClient(new Uri(server.Prefix + "latest.json"));
            UpdateManifest manifest = await client.FetchManifestAsync(CancellationToken.None);
            Assert.NotNull(manifest);

            var seen = new System.Collections.Generic.List<double>();
            var progress = new Progress<UpdateProgress>(p => seen.Add(p.Fraction));
            string setup = await client.DownloadAsync(manifest, directory, progress, CancellationToken.None);

            Assert.True(File.Exists(setup));
            Assert.Equal(payload.Length, new FileInfo(setup).Length);
            Assert.Equal(manifest.Sha256, UpdateClient.ComputeSha256(setup));
            // Nothing half-written is left behind for the next run to trip over.
            Assert.Empty(Directory.GetFiles(directory, "*.part"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchTheChecksumIsRefusedAndDeleted()
    {
        byte[] payload = Payload(8 * 1024);
        using var server = new Server(payload);
        // The manifest advertises a hash the served bytes do not have.
        string json = $@"{{ ""schema"": 1, ""clientUpdate"": 1, ""version"": ""3.0.7.0"",
            ""url"": ""{server.Prefix}Setup.exe"", ""size"": {payload.Length},
            ""sha256"": ""{new string('b', 64)}"" }}";
        Assert.True(UpdateManifest.TryParse(json, out UpdateManifest manifest, out _));

        string directory = TempDirectory();
        try
        {
            using var client = new UpdateClient(new Uri(server.Prefix + "latest.json"));
            await Assert.ThrowsAsync<UpdateVerificationException>(
                () => client.DownloadAsync(manifest, directory, null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AnInterruptedDownloadContinuesWhereItStopped()
    {
        byte[] payload = Payload(32 * 1024);
        using var server = new Server(payload);
        server.ManifestJson = LocalManifest(server, payload);
        string directory = TempDirectory();
        try
        {
            using var client = new UpdateClient(new Uri(server.Prefix + "latest.json"));
            UpdateManifest manifest = await client.FetchManifestAsync(CancellationToken.None);

            // Half a file from an attempt that did not finish.
            string partial = Path.Combine(directory, "Spotnet-3.0-x64-Setup-3.0.7.0.exe.part");
            File.WriteAllBytes(partial, payload[..(payload.Length / 2)]);

            string setup = await client.DownloadAsync(manifest, directory, null, CancellationToken.None);
            Assert.Equal(1, server.RangeRequests);
            Assert.Equal(payload, File.ReadAllBytes(setup));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeStillProducesTheRightFile()
    {
        byte[] payload = Payload(16 * 1024);
        using var server = new Server(payload, supportsRange: false);
        server.ManifestJson = LocalManifest(server, payload);
        string directory = TempDirectory();
        try
        {
            using var client = new UpdateClient(new Uri(server.Prefix + "latest.json"));
            UpdateManifest manifest = await client.FetchManifestAsync(CancellationToken.None);
            File.WriteAllBytes(Path.Combine(directory, "Spotnet-3.0-x64-Setup-3.0.7.0.exe.part"),
                payload[..(payload.Length / 4)]);

            string setup = await client.DownloadAsync(manifest, directory, null, CancellationToken.None);
            Assert.Equal(payload, File.ReadAllBytes(setup));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AnInstallerAlreadyDownloadedIsNotFetchedTwice()
    {
        byte[] payload = Payload(4 * 1024);
        using var server = new Server(payload);
        server.ManifestJson = LocalManifest(server, payload);
        string directory = TempDirectory();
        try
        {
            using var client = new UpdateClient(new Uri(server.Prefix + "latest.json"));
            UpdateManifest manifest = await client.FetchManifestAsync(CancellationToken.None);
            string first = await client.DownloadAsync(manifest, directory, null, CancellationToken.None);
            DateTime written = File.GetLastWriteTimeUtc(first);

            string second = await client.DownloadAsync(manifest, directory, null, CancellationToken.None);
            Assert.Equal(first, second);
            Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AnUnreachableServerReportsAReasonInsteadOfThrowing()
    {
        string prefix;
        using (var server = new Server(Payload(16))) prefix = server.Prefix;
        // The server is disposed; nothing is listening on that port any more.
        using var client = new UpdateClient(new Uri(prefix + "latest.json"));
        (UpdateManifest manifest, string error) =
            await client.FetchManifestWithReasonAsync(CancellationToken.None, TimeSpan.FromSeconds(5.0));
        Assert.Null(manifest);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
