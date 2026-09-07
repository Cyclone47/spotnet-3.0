using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Spotnet.Mac.Remote;
using Spotnet.Remote;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests voor fase 5 (Spotnet Remote): de gedeelde config/auth/discovery-laag,
/// de live Kestrel-endpoints van de gedeelde RemoteWebServer met stub-providers,
/// de platformneutrale QR-builder en de caffeinate-poort van MacSleepPreventer.
/// De contractvorm volgt Windows' SpotnetRemoteTests, zodat beide clients dezelfde
/// API beloven aan de telefoon.
/// </summary>
public sealed class RemoteHostTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Func<string>? _previousConfigPathProvider;

    public RemoteHostTests()
    {
        _tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "spotnet-remote-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_tempDir);
        _previousConfigPathProvider = RemoteConfig.ConfigPathProvider;
        RemoteConfig.ConfigPathProvider = () => System.IO.Path.Combine(_tempDir, "remote_config.json");
    }

    public void Dispose()
    {
        RemoteConfig.ConfigPathProvider = _previousConfigPathProvider;
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── RemoteConfig ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoteConfig_RoundTrip_PreservesSettings()
    {
        var config = new RemoteConfig
        {
            Enabled = true,
            Port = 8801,
            AllowLan = false,
            RequireAuth = true,
            KeepAwake = true
        };
        config.SetPassword("Test1234!");
        config.Save();

        var loaded = RemoteConfig.Load();
        Assert.True(loaded.Enabled);
        Assert.Equal(8801, loaded.Port);
        Assert.False(loaded.AllowLan);
        Assert.True(loaded.KeepAwake);
        Assert.True(loaded.VerifyPassword("Test1234!"));
        Assert.False(loaded.VerifyPassword("Fout"));
    }

    [Fact]
    public void RemoteConfig_MissingFile_ReturnsDefaults()
    {
        RemoteConfig.ConfigPathProvider = () =>
            System.IO.Path.Combine(_tempDir, "bestaat_niet.json");

        var config = RemoteConfig.Load();
        Assert.False(config.Enabled);
        Assert.Equal(8770, config.Port);
        Assert.True(config.RequireAuth);
    }

    // ── RemoteAuthManager (gedeeld) ──────────────────────────────────────────

    [Fact]
    public void RemoteAuthManager_PairingPin_SixDigitsAndExpires()
    {
        var auth = RemoteAuthManager.Instance;
        var previous = auth.Config;
        try
        {
            auth.Config = new RemoteConfig { RequireAuth = true };
            var session = auth.CreatePairingSession();
            Assert.Equal(6, session.Pin.Length);
            Assert.Matches("^\\d{6}$", session.Pin);
            Assert.False(string.IsNullOrEmpty(session.Token));
            Assert.True(session.ExpiresAt > DateTime.UtcNow.AddMinutes(4));
        }
        finally
        {
            auth.Config = previous;
        }
    }

    [Fact]
    public void RemoteAuthManager_TryPair_ValidPin_PairsDevice()
    {
        var auth = RemoteAuthManager.Instance;
        var previous = auth.Config;
        try
        {
            auth.Config = new RemoteConfig { RequireAuth = true };
            var session = auth.CreatePairingSession();

            var response = auth.TryPair(new PairRequestDto
            {
                Pin = session.Pin,
                DeviceName = "Testtelefoon"
            }, "10.0.0.99");

            Assert.True(response.Success);
            Assert.False(string.IsNullOrEmpty(response.DeviceToken));

            Assert.True(auth.ValidateToken(response.DeviceToken, "10.0.0.99", out var device));
            Assert.Equal("Testtelefoon", device.Name);
        }
        finally
        {
            auth.Config = previous;
            auth.RevokeAllDevices();
        }
    }

    [Fact]
    public void RemoteAuthManager_RevokeDevice_RemovesTokenAccess()
    {
        var auth = RemoteAuthManager.Instance;
        var previous = auth.Config;
        try
        {
            auth.Config = new RemoteConfig { RequireAuth = true };
            var session = auth.CreatePairingSession();
            var response = auth.TryPair(new PairRequestDto { Pin = session.Pin, DeviceName = "Weg" }, "10.0.0.5");
            Assert.True(response.Success);

            Assert.True(auth.RevokeDevice(response.DeviceId));
            Assert.False(auth.ValidateToken(response.DeviceToken, "10.0.0.5", out _));
        }
        finally
        {
            auth.Config = previous;
            auth.RevokeAllDevices();
        }
    }

    // ── Discovery (UDP 8771 payload) ─────────────────────────────────────────

    [Fact]
    public void DiscoveryPayload_ContainsServicePortAndAuthFlag()
    {
        string payload = RemoteDiscoveryService.BuildDiscoveryPayload(8770, requireAuth: true);
        Assert.Contains("\"service\":\"spotnet-remote\"", payload);
        Assert.Contains("\"port\":8770", payload);
        Assert.Contains("\"requireAuth\":true", payload);
    }

    // ── RemoteQrCode ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoteQrCode_GeneratesPngWithSignature()
    {
        byte[]? png = RemoteQrCode.GeneratePng("http://192.168.1.10:8770/?pairToken=abc");
        Assert.NotNull(png);
        Assert.True(png!.Length > 100);
        // PNG magic number
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]); // 'P'
        Assert.Equal(0x4E, png[2]); // 'N'
        Assert.Equal(0x47, png[3]); // 'G'
    }

    [Fact]
    public void RemoteQrCode_EmptyContent_ReturnsNull()
    {
        Assert.Null(RemoteQrCode.GeneratePng(""));
        Assert.Null(RemoteQrCode.GeneratePng("   "));
    }

    // ── RemoteWebServer: live Kestrel endpoints met stub-providers ────────────

    [Fact]
    public async Task RemoteWebServer_StatusAndSpots_ServeOverHttp()
    {
        var config = new RemoteConfig
        {
            Enabled = true,
            Port = 0, // let Kestrel pick a free ephemeral port? nee — ActivePort > 0 vereist
            RequireAuth = false
        };
        config.Port = GetFreePort();

        var auth = RemoteAuthManager.Instance;
        var previousAuth = auth.Config;
        auth.Config = config;

        var server = new RemoteWebServer
        {
            WebRootOverride = "", // geen web-shell nodig voor de API-test
            HostInfo = new StubHostInfo(),
            Catalog = new StubCatalog(),
            Queue = new StubQueue()
        };

        try
        {
            server.Start();
            Assert.True(server.IsRunning);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActivePort}") };

            var status = await client.GetFromJsonAsync<ServerStatusDto>("/api/v1/status");
            Assert.NotNull(status);
            Assert.Equal("Testprovider", status!.CurrentProvider);
            Assert.Equal(4242, status.TotalSpotsInDb);

            var spots = await client.GetFromJsonAsync<SpotDto[]>("/api/v1/spots");
            Assert.NotNull(spots);
            Assert.Single(spots!);
            Assert.Equal("Testspot", spots[0].Title);

            var filters = await client.GetFromJsonAsync<FilterDto[]>("/api/v1/filters");
            Assert.NotNull(filters);
            Assert.NotEmpty(filters!);

            var queue = await client.GetFromJsonAsync<QueueStatusDto>("/api/v1/queue");
            Assert.NotNull(queue);
            Assert.Equal(1, queue!.ActiveCount);
        }
        finally
        {
            server.Stop();
            auth.Config = previousAuth;
        }
    }

    [Fact]
    public async Task RemoteWebServer_RequireAuth_BlocksAnonymousRequests()
    {
        var config = new RemoteConfig { Enabled = true, RequireAuth = true };
        config.Port = GetFreePort();

        var auth = RemoteAuthManager.Instance;
        var previousAuth = auth.Config;
        auth.Config = config;

        var server = new RemoteWebServer
        {
            HostInfo = new StubHostInfo(),
            Catalog = new StubCatalog()
        };

        try
        {
            server.Start();

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActivePort}") };

            // /status is openbaar
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/status")).StatusCode);

            // beschermde endpoints zonder token → 401
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/spots")).StatusCode);

            // pairing geeft een geldig token
            var session = auth.CreatePairingSession();
            var pair = auth.TryPair(new PairRequestDto { Pin = session.Pin, DeviceName = "Http" }, "127.0.0.1");
            Assert.True(pair.Success);

            using var authorized = new HttpClient { BaseAddress = client.BaseAddress! };
            authorized.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.DeviceToken);
            Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync("/api/v1/spots")).StatusCode);
        }
        finally
        {
            server.Stop();
            auth.Config = previousAuth;
            auth.RevokeAllDevices();
        }
    }

    // ── MacSleepPreventer ────────────────────────────────────────────────────

    [Fact]
    public void SleepPreventer_StartAndStop_DoesNotThrow()
    {
        var preventer = new MacSleepPreventer();
        preventer.UpdateState(true);   // start /usr/bin/caffeinate
        preventer.UpdateState(true);   // idempotent
        preventer.UpdateState(false);  // stop
        preventer.UpdateState(false);  // idempotent
    }

    // ── hulpjes + stubs ──────────────────────────────────────────────────────

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class StubHostInfo : IRemoteHostInfo
    {
        public string GetVersion() => "3.0-test";
        public string GetProviderName() => "Testprovider";
        public long GetTotalSpotsInDb() => 4242;
        public bool IsSyncing => false;
        public string GetNickname() => "Spotter";
    }

    private sealed class StubCatalog : IRemoteSpotCatalog
    {
        public System.Collections.Generic.IReadOnlyList<FilterDto> GetFilters() =>
            new[] { new FilterDto { Id = "f1", Name = "Alles" } };

        public System.Collections.Generic.IReadOnlyList<SpotDto> GetSpots(string query, int? category, string filterId, int page, int pageSize, string sort) =>
            new[] { new SpotDto { Id = 1, Title = "Testspot", MessageId = "abc@spot.net" } };

        public SpotDetailDto? GetSpotDetail(long id) => null;

        public byte[]? GetSpotImage(long id, string messageId) => null;

        public System.Collections.Generic.IReadOnlyList<SpotCommentDto> GetSpotComments(long id, string messageId) =>
            Array.Empty<SpotCommentDto>();

        public (bool success, string error, SpotCommentDto comment) PostComment(long id, string messageId, string nickname, string body) =>
            (false, "stub", new SpotCommentDto());

        public void ToggleFavorite(string messageId, bool favorite) { }

        public System.Collections.Generic.IReadOnlyList<SpotDto> GetFavorites(int page, int pageSize) =>
            Array.Empty<SpotDto>();
    }

    private sealed class StubQueue : IRemoteDownloadQueue
    {
        public QueueStatusDto GetQueue() => new()
        {
            ActiveCount = 1,
            Items = new() { new DownloadItemDto { Id = "0", Title = "Testdownload" } }
        };

        public (bool success, string error) EnqueueSpot(long id, string messageId) => (true, "");
        public bool PauseItem(string id) => true;
        public bool ResumeItem(string id) => true;
        public bool CancelItem(string id) => true;
        public bool SetSpeedLimit(int kbps) => true;
    }
}
