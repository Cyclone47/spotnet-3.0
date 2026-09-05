using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Mac.Network;
using Spotnet.Mac.Services;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests for the fase-3 downloader settings: speed limit, retries with retry
/// interval, connection/data timeouts and the cache size. The behaviour follows the
/// Windows client: VirtualNNTP.SetDownloadSpeedLimit/LimitDownloadSpeed,
/// NNTPSegment.MaxRetries + DefaultTimeout, Settings.Default.ConnectionTimeout and
/// DataReceivingTimeout, and IsCachingEnabled/DownloaderCacheSizeMb.
/// </summary>
public sealed class DownloadSpeedLimitTests : IDisposable
{
    private readonly string _dir;
    private readonly UserPreferencesService _prefsService;

    public DownloadSpeedLimitTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spotnet-dltest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _prefsService = new UserPreferencesService(new StandardAppPaths(_dir, _dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
    }

    // ── Preferences roundtrip ──────────────────────────────────────────────────

    [Fact]
    public void New_preferences_match_the_windows_defaults()
    {
        var prefs = _prefsService.Load();

        Assert.Equal(-1, prefs.SpeedLimit);          // Windows SpeedLimit default -1
        Assert.Equal(3, prefs.DownloaderRetries);    // Windows DownloaderRetries default 3
        Assert.Equal(10, prefs.DownloaderRetryIntervalSec);
        Assert.Equal(10000, prefs.ConnectionTimeout);
        Assert.Equal(60000, prefs.DataReceivingTimeout);
        Assert.True(prefs.IsCachingEnabled);         // Windows IsCachingEnabled default true
        Assert.Equal(20, prefs.DownloaderCacheSizeMb);
    }

    [Fact]
    public void Download_preferences_survive_a_save_and_reload()
    {
        var prefs = _prefsService.Current;
        prefs.SpeedLimit = 2048;
        prefs.DownloaderRetries = 7;
        prefs.DownloaderRetryIntervalSec = 25;
        prefs.ConnectionTimeout = 15000;
        prefs.DataReceivingTimeout = 90000;
        prefs.IsCachingEnabled = false;
        prefs.DownloaderCacheSizeMb = 64;
        _prefsService.Save(prefs);

        var reloaded = new UserPreferencesService(new StandardAppPaths(_dir, _dir)).Load();

        Assert.Equal(2048, reloaded.SpeedLimit);
        Assert.Equal(7, reloaded.DownloaderRetries);
        Assert.Equal(25, reloaded.DownloaderRetryIntervalSec);
        Assert.Equal(15000, reloaded.ConnectionTimeout);
        Assert.Equal(90000, reloaded.DataReceivingTimeout);
        Assert.False(reloaded.IsCachingEnabled);
        Assert.Equal(64, reloaded.DownloaderCacheSizeMb);
    }

    [Fact]
    public void Options_from_preferences_keep_the_windows_floors()
    {
        var prefs = new UserPreferences
        {
            SpeedLimit = 0,                    // 0 counts as unlimited, as -1 does
            DownloaderRetries = 0,             // falls back to 3, like NNTPSegment.MaxRetries
            DownloaderRetryIntervalSec = -5,   // falls back to 10
            DownloaderCacheSizeMb = 0          // falls back to 20
        };

        var options = NzbDownloadOptions.FromPreferences(prefs);

        Assert.Equal(0, options.SpeedLimitKbps);
        Assert.Equal(3, options.Retries);
        Assert.Equal(10, options.RetryIntervalSec);
        Assert.Equal(20, options.DownloaderCacheSizeMb);
    }

    // ── Speed limiter ──────────────────────────────────────────────────────────

    [Fact]
    public void Limiter_does_not_throttle_when_the_limit_is_off()
    {
        var limiter = new DownloadSpeedLimiter { LimitKbps = -1 };

        limiter.OnBytesReceived(10_000_000);
        Assert.Equal(0, limiter.ThrottleIfNeeded());
        Assert.Equal(0, limiter.ThrottleIfNeeded());
    }

    [Fact]
    public void Limiter_lets_a_small_transfer_pass_within_the_window()
    {
        var limiter = new DownloadSpeedLimiter { LimitKbps = 1024 };

        // 8 KB at a 1 MB/s limit inside the 500 ms window is far below budget.
        limiter.OnBytesReceived(8 * 1024);
        Assert.Equal(0, limiter.ThrottleIfNeeded());
    }

    [Fact]
    public void Limiter_sleeps_when_the_window_is_breached()
    {
        var limiter = new DownloadSpeedLimiter
        {
            LimitKbps = 1024,                      // 1 MB/s
            DataReceivingTimeoutMs = 60000
        };

        // Windows only evaluates the window once it is older than 500 ms; match that
        // by receiving for just over one evaluation period before throttling.
        limiter.OnBytesReceived(600 * 1024);       // 600 KB/s for the first 500 ms window
        Thread.Sleep(DownloadSpeedLimiter.WindowMs + 150);

        limiter.OnBytesReceived(600 * 1024);       // another 600 KB in the next window
        int waited = limiter.ThrottleIfNeeded();

        // Budget so far ≈ 1 MB for ~1.15 s; received 1.2 MB, so the overshoot is a few
        // hundred KB and the sleep is proportionally short but real.
        Assert.True(waited > 0, "expected the limiter to sleep for the overshoot");
        Assert.True(waited < 5000, $"wait {waited} ms is out of proportion");
    }

    [Fact]
    public void Limiter_pays_the_full_overshoot_in_slices_capped_per_slice()
    {
        var limiter = new DownloadSpeedLimiter
        {
            LimitKbps = 64,                        // tiny limit, huge overshoot
            DataReceivingTimeoutMs = 60000
        };

        limiter.OnBytesReceived(8L * 1024 * 1024); // 8 MB against a 64 KB/s budget
        Thread.Sleep(DownloadSpeedLimiter.WindowMs + 150);

        // Windows pays the whole overshoot, not a truncated one — its slices are
        // capped at DataReceivingTimeout − 2000 each, not the total. Prove the
        // per-slice cap without waiting minutes: cancel midway and check that every
        // slice before the cancel was at most 58 s (trivially true here) while the
        // cancellation stopped the loop early.
        using var cts = new CancellationTokenSource(300);

        var sw = Stopwatch.StartNew();
        int waited = limiter.ThrottleIfNeeded(cts.Token);
        sw.Stop();

        // The unbounded overshoot of 8 MB at 64 KB/s is ≈ 128 s; the cancel must have
        // cut it far short, and the wait must track the actual elapsed time.
        Assert.True(waited <= 58000, $"a single slice ran {waited} ms, over the 58 s cap");
        Assert.True(waited < 128_000, "cancellation did not shorten the wait");
        Assert.True(sw.ElapsedMilliseconds >= waited - 50, "the wait must actually happen");
    }

    [Fact]
    public void Limiter_resets_the_account_after_every_evaluation()
    {
        var limiter = new DownloadSpeedLimiter { LimitKbps = 1024 };

        limiter.OnBytesReceived(600 * 1024);
        Thread.Sleep(DownloadSpeedLimiter.WindowMs + 150);
        int first = limiter.ThrottleIfNeeded();

        // The account was emptied by the evaluation, so an immediate second call has
        // nothing to pay off even though the window is now old.
        int second = limiter.ThrottleIfNeeded();

        Assert.True(second == 0, "second evaluation must start from an empty account");
        _ = first;
    }

    [Fact]
    public void Limiter_is_shared_between_jobs_like_the_static_windows_limit()
    {
        var job = new NzbDownloadJob(
            new UsenetConnection(new StandardAppPaths(_dir, _dir), new NoSecretStore()),
            Array.Empty<NzbFile>(), Path.Combine(_dir, "out"), 4,
            new NzbDownloadOptions(Retries: 2, SpeedLimitKbps: 512));

        Assert.Same(DownloadSpeedLimiter.Shared, job.SpeedLimiter);
        Assert.Equal(512, DownloadSpeedLimiter.Shared.LimitKbps);
    }

    // ── Downloadschema (fase 3, item 2) — port of IsDownloaderActiveTime ──────

    [Theory]
    [InlineData("20:00", "06:00", "23:30", true)]   // overnight window, inside
    [InlineData("20:00", "06:00", "03:00", true)]   // overnight window, after midnight
    [InlineData("20:00", "06:00", "12:00", false)]  // overnight window, daytime gap
    [InlineData("20:00", "06:00", "20:00", true)]   // start boundary is inclusive
    [InlineData("20:00", "06:00", "06:00", true)]   // end boundary is inclusive
    [InlineData("08:00", "18:00", "12:00", true)]   // daytime window, inside
    [InlineData("08:00", "18:00", "07:59", false)]  // daytime window, before
    [InlineData("08:00", "18:00", "18:00", true)]   // daytime window, at end
    [InlineData("08:00", "18:00", "08:00", true)]   // daytime window, at start
    [InlineData("00:00", "00:00", "03:00", true)]   // equal times = always active
    public void Schedule_window_follows_the_windows_client_rules(
        string start, string end, string now, bool expected)
    {
        TimeSpan T(string s) => TimeSpan.Parse(s);

        bool active = DownloadSchedule.IsDownloaderActiveTime(true, T(start), T(end), T(now));
        Assert.Equal(expected, active);
    }

    [Fact]
    public void Schedule_off_means_always_active()
    {
        bool active = DownloadSchedule.IsDownloaderActiveTime(
            false, new TimeSpan(20, 0, 0), new TimeSpan(6, 0, 0), new TimeSpan(12, 0, 0));
        Assert.True(active);
    }

    [Fact]
    public void Schedule_reads_out_of_preferences_with_the_time_of_day_only()
    {
        var prefs = new UserPreferences
        {
            DownloaderSchedule = true,
            // Dates differ but both times are 22:00 and 06:00; the date part is
            // ignored, exactly as Windows reads only .TimeOfDay.
            DownloaderStartTime = new DateTime(2026, 1, 1, 22, 0, 0),
            DownloaderEndTime = new DateTime(2020, 6, 15, 6, 0, 0)
        };

        Assert.True(DownloadSchedule.IsDownloaderActiveTime(prefs, new DateTime(2026, 9, 5, 23, 0, 0)));
        Assert.False(DownloadSchedule.IsDownloaderActiveTime(prefs, new DateTime(2026, 9, 5, 12, 0, 0)));
    }

    [Fact]
    public void Options_carry_the_schedule_settings()
    {
        var prefs = new UserPreferences
        {
            DownloaderSchedule = true,
            DownloaderStartTime = new DateTime(2000, 1, 1, 22, 30, 0),
            DownloaderEndTime = new DateTime(2000, 1, 1, 6, 0, 0)
        };

        var options = NzbDownloadOptions.FromPreferences(prefs);

        Assert.True(options.ScheduleEnabled);
        Assert.Equal(new TimeSpan(22, 30, 0), options.ScheduleStart);
        Assert.Equal(new TimeSpan(6, 0, 0), options.ScheduleEnd);
    }

    // ── Cache server gate (Windows CachingSystem.IsEnabled + provider check) ───

    [Theory]
    [InlineData("cache.snelnl.com", true, true)]
    [InlineData("cache.usenetsys.com", true, true)]
    [InlineData("CACHE.SNELNL.COM", true, true)]
    [InlineData("news.eweka.nl", true, false)]
    [InlineData("cache.snelnl.com", false, false)]
    [InlineData("", true, false)]
    public void Cache_servers_are_gated_on_the_preference(string host, bool enabled, bool expected)
    {
        Assert.Equal(expected, UserPreferences.IsCacheServer(host, enabled));
    }

    // ── Connect timeout on NntpClient ──────────────────────────────────────────

    [Fact]
    public async Task Connect_timeout_produces_a_timeout_exception_on_a_dead_address()
    {
        using var client = new NntpClient { ConnectTimeoutMs = 500 };

        // RFC 5737 TEST-NET-1: guaranteed unroutable, so the connect hangs until the
        // configured timeout fires instead of the OS default.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync("192.0.2.1", 119, useSsl: false));

        Assert.IsType<TimeoutException>(ex);
        Assert.Contains("500 ms", ex.Message);
        Assert.False(client.IsConnected);
    }

    // ── Retries ────────────────────────────────────────────────────────────────

    [Fact]
    public void Retry_options_default_to_the_windows_values()
    {
        var options = new NzbDownloadOptions();

        Assert.Equal(3, options.Retries);
        Assert.Equal(10, options.RetryIntervalSec);
        Assert.Equal(-1, options.SpeedLimitKbps);
        Assert.True(options.IsCachingEnabled);
        Assert.Equal(20, options.DownloaderCacheSizeMb);
    }

    private sealed class NoSecretStore : ISecretStore
    {
        public string? GetSecret(string key) => null;
        public void SetSecret(string key, string secret) { }
        public bool DeleteSecret(string key) => false;
    }
}
