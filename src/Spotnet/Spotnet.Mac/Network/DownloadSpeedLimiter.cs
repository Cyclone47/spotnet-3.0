using System;
using System.Diagnostics;
using System.Threading;

namespace Spotnet.Mac.Network;

/// <summary>
/// Shared download-speed limiter across all NNTP connections of the downloader —
/// the Mac counterpart of <c>Spotnet.Phuse.NNTP.Net.VirtualNNTP</c>'s static
/// <c>_kbpsLimit</c> throttle.
///
/// The Windows client throttles in <c>VirtualNNTP.LimitDownloadSpeed</c>: it counts
/// received bytes into one shared account, looks at a 500 ms window, and once the
/// account holds more than limit × elapsed KB, sleeps the time it takes at limit
/// speed to pay the overshoot off. That total wait is the full overshoot — it can be
/// minutes — but it is split into slices of at most DataReceivingTimeout − 2000 ms,
/// stamping LastDataReceivedTime between slices so the connection is not reaped for
/// idling while the throttle holds it. That arithmetic is mirrored here byte for byte:
///
///   allowed = limitKbps × windowElapsed / 1000      (bytes)
///   overshootKb = account/1024 − allowed/1024
///   sleepMs = overshootKb / limitKbps × 1000
///
/// Each download worker feeds its received bytes into <see cref="OnBytesReceived"/>
/// and then calls <see cref="ThrottleIfNeeded"/>, which sleeps the worker when the
/// shared account is over budget. The window starts at the first byte of a download
/// and restarts after every evaluation, the way <c>SpeedCalcWatch.Restart()</c> does
/// on Windows.
/// </summary>
public sealed class DownloadSpeedLimiter
{
    /// <summary>Window in which the average speed is evaluated. Windows uses 500 ms.</summary>
    public const int WindowMs = 500;

    /// <summary>Default data-receiving timeout used to cap a throttle slice.</summary>
    public const int DefaultDataReceivingTimeoutMs = 60000;

    /// <summary>The shared byte account, in bytes.</summary>
    private long _speedCalcBytes;

    private long _limitKbps;

    /// <summary>
    /// Start of the evaluation window in Stopwatch milliseconds, stored offset by +1
    /// so zero means "no window open yet". The offset also protects the elapsed-time
    /// calculation: Stopwatch starts at 0, so a stored raw 0 would be indistinguishable
    /// from "never started".
    /// </summary>
    private long _windowStartMs;

    /// <summary>The Stopwatch that measures the evaluation window.</summary>
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    /// <summary>
    /// Application-wide limiter. Windows keeps the limit in a static field on
    /// VirtualNNTP, so a limit set anywhere applies to every connection; this instance
    /// plays that role on the Mac and is what the settings window updates.
    /// </summary>
    public static readonly DownloadSpeedLimiter Shared = new();

    /// <summary>
    /// The limit in KB/s, across all connections together. 0 or negative means
    /// unlimited, exactly like <c>VirtualNNTP.SetDownloadSpeedLimit</c>.
    /// </summary>
    public long LimitKbps
    {
        get => Interlocked.Read(ref _limitKbps);
        set => Interlocked.Exchange(ref _limitKbps, value);
    }

    /// <summary>Data-receiving timeout in ms used to cap each sleep slice; configurable for tests.</summary>
    public int DataReceivingTimeoutMs { get; set; } = DefaultDataReceivingTimeoutMs;

    /// <summary>Called by a download worker after it received <paramref name="bytes"/> bytes.</summary>
    public void OnBytesReceived(long bytes)
    {
        if (bytes <= 0) return;
        if (Volatile.Read(ref _windowStartMs) == 0)
        {
            // First bytes of a download: open a fresh window before counting them, or
            // bytes arriving long after construction would count against an
            // over-elapsed window and produce a wildly oversized sleep.
            Volatile.Write(ref _windowStartMs, _watch.ElapsedMilliseconds + 1);
        }
        Interlocked.Add(ref _speedCalcBytes, bytes);
    }

    /// <summary>
    /// Puts the calling worker to sleep for as long as needed to stay within the
    /// limit, the way <c>VirtualNNTP.LimitDownloadSpeed</c> does. Returns immediately
    /// when the limit is off or the window is still young.
    /// </summary>
    /// <returns>How long was actually waited, in milliseconds. 0 when no wait was due.</returns>
    public int ThrottleIfNeeded(CancellationToken cancellationToken = default)
    {
        long limit = Interlocked.Read(ref _limitKbps);
        if (limit <= 0) return 0;

        long windowStart = Volatile.Read(ref _windowStartMs) - 1;
        if (windowStart < 0) return 0;                       // no bytes counted yet
        long windowElapsed = _watch.ElapsedMilliseconds - windowStart;
        if (windowElapsed <= WindowMs) return 0;

        // Windows: allowed in this window is limit × elapsed / 1000, in KB — num in
        // LimitDownloadSpeed is _kbpsLimit * elapsedMilliseconds / 1000 and is
        // compared against _speedCalcBytes / 1024, so the budget is in KB, not bytes.
        double allowedKb = limit * windowElapsed / 1000.0;
        double overshootKb = Interlocked.Read(ref _speedCalcBytes) / 1024.0 - allowedKb;
        if (overshootKb <= 1.0)
        {
            ResetWindow();
            return 0;
        }

        // Windows: sleepMs = overshootKb / limit × 1000 — the time it takes at limit
        // speed to pay off the overshoot. (num3 in LimitDownloadSpeed.)
        long sleepMs = (long)(overshootKb / limit * 1000.0);

        // Windows caps each Thread.Sleep at DataReceivingTimeout − 2000 and loops until
        // the full overshoot is paid, stamping LastDataReceivedTime after every slice
        // so the connection is not reaped for idling while the throttle holds it.
        long sliceCap = Math.Max(1000, DataReceivingTimeoutMs - 2000);
        int waited = 0;
        while (sleepMs > 0 && !cancellationToken.IsCancellationRequested)
        {
            int slice = (int)Math.Min(sleepMs, sliceCap);
            Thread.Sleep(slice);
            waited += slice;
            sleepMs -= slice;
        }

        ResetWindow();
        return waited;
    }

    /// <summary>Empties the account and restarts the window, as SpeedCalcWatch.Restart() does.</summary>
    private void ResetWindow()
    {
        Interlocked.Exchange(ref _speedCalcBytes, 0);
        Volatile.Write(ref _windowStartMs, _watch.ElapsedMilliseconds + 1);
    }
}
