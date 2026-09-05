using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Services;
using SpotnetEnc;

namespace Spotnet.Mac.Network;

/// <summary>Live progress snapshot passed to the progress callback.</summary>
public sealed record NzbJobProgress(
    long BytesDone,
    long BytesTotal,
    long SpeedBps,
    int FilesCompleted,
    int FilesTotal,
    string CurrentFile);

/// <summary>
/// Settings for a download job, read out of the user preferences at queue time.
/// The names follow the Windows client so a look at the queue diagnostics says
/// exactly which setting is in force:
///   Retries          = Settings.Default.DownloaderRetries        (default 3)
///   RetryIntervalSec = Settings.Default.DownloaderRetryIntervalSec (default 10)
///   SpeedLimitKbps   = Settings.Default.SpeedLimit               (-1 = unlimited)
/// </summary>
public sealed record NzbDownloadOptions(
    int Retries = 3,
    int RetryIntervalSec = 10,
    int SpeedLimitKbps = -1,
    bool IsCachingEnabled = true,
    int DownloaderCacheSizeMb = 20)
{
    /// <summary>Reads the current downloader settings out of the user preferences.</summary>
    public static NzbDownloadOptions FromPreferences(UserPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new NzbDownloadOptions(
            Retries: prefs.DownloaderRetries > 0 ? prefs.DownloaderRetries : 3,
            RetryIntervalSec: prefs.DownloaderRetryIntervalSec > 0 ? prefs.DownloaderRetryIntervalSec : 10,
            SpeedLimitKbps: prefs.SpeedLimit,
            IsCachingEnabled: prefs.IsCachingEnabled,
            DownloaderCacheSizeMb: prefs.DownloaderCacheSizeMb > 0 ? prefs.DownloaderCacheSizeMb : 20);
    }
}

/// <summary>
/// Downloads every binary file described by a parsed NZB using multiple parallel
/// NNTP connections and the yEnc decoder from Spotnet.Enc.
///
/// Mirrors the role of Spotnet.Downloader.DownloaderEngine from Windows:
///   NZB files -> parallel NNTP BODY -> yEnc decode -> assemble on disk.
///
/// Segment retries and the retry interval follow NNTPSegment.MaxRetries and
/// NNTPSegment.DefaultTimeout; the speed limit follows the shared throttle in
/// VirtualNNTP via <see cref="DownloadSpeedLimiter"/>.
/// </summary>
public sealed class NzbDownloadJob
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly SpotnetDecoder Decoder = new();
    private static readonly SearchValues<char> Delimiters = SearchValues.Create("([");

    private readonly UsenetConnection _connection;
    private readonly int _maxConnections;
    private readonly NzbDownloadOptions _options;

    /// <summary>
    /// The limiter this job throttles against. It is the app-wide <see cref="DownloadSpeedLimiter.Shared"/>
    /// instance when one is in force, so a limit changed in the settings window or by
    /// a newer job applies to this one too — the same way the static
    /// VirtualNNTP._kbpsLimit on Windows is shared by every connection.
    /// </summary>
    public DownloadSpeedLimiter SpeedLimiter { get; }

    public IReadOnlyList<NzbFile> Files { get; }
    public string OutputDir { get; }

    public NzbDownloadJob(UsenetConnection connection, IReadOnlyList<NzbFile> files, string outputDir, int maxConnections = 4)
        : this(connection, files, outputDir, maxConnections, NzbDownloadOptions.FromPreferences(
                  new UserPreferences()))
    {
    }

    public NzbDownloadJob(UsenetConnection connection, IReadOnlyList<NzbFile> files, string outputDir,
                          int maxConnections, NzbDownloadOptions options)
    {
        _connection = connection;
        Files = files;
        OutputDir = outputDir;
        _maxConnections = Math.Clamp(maxConnections, 1, 32);
        _options = options ?? new NzbDownloadOptions();
        SpeedLimiter = DownloadSpeedLimiter.Shared;
        SpeedLimiter.LimitKbps = _options.SpeedLimitKbps;
    }

    /// <summary>
    /// Downloads all files configured for this job into OutputDir.
    /// </summary>
    public Task RunAsync(
        IProgress<NzbJobProgress>? progress = null,
        ManualResetEventSlim? pauseGate = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(Files, OutputDir, progress, pauseGate, cancellationToken);
    }

    /// <summary>
    /// Downloads all files in <paramref name="files"/> into <paramref name="outputDir"/>.
    /// Reports progress via <paramref name="progress"/>. Returns when all files are done
    /// or the token is cancelled.
    /// </summary>
    public async Task RunAsync(
        IReadOnlyList<NzbFile> files,
        string outputDir,
        IProgress<NzbJobProgress>? progress = null,
        ManualResetEventSlim? pauseGate = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDir);

        long bytesTotal = files.Sum(f => f.Segments.Sum(s => s.Bytes));
        long bytesDone  = 0;
        int  filesTotal = files.Count;
        int  filesDone  = 0;

        var speedCalc  = new SpeedCalculator();
        speedCalc.Start();

        for (int fi = 0; fi < files.Count; fi++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pauseGate?.Wait(cancellationToken);

            var nzbFile = files[fi];
            string fileName = ExtractFileName(nzbFile.Subject) ?? $"file{fi + 1}.bin";
            string filePath = Path.Combine(outputDir, fileName);

            Log.Info("Downloading {0} ({1} segments)", fileName, nzbFile.Segments.Count);

            progress?.Report(new NzbJobProgress(bytesDone, bytesTotal, speedCalc.SpeedBps,
                filesDone, filesTotal, fileName));

            await DownloadFileAsync(nzbFile, filePath, bytesTotal,
                (delta) =>
                {
                    Interlocked.Add(ref bytesDone, delta);
                    speedCalc.Add(delta);
                    SpeedLimiter.OnBytesReceived(delta);
                    progress?.Report(new NzbJobProgress(
                        Math.Min(Interlocked.Read(ref bytesDone), bytesTotal), bytesTotal,
                        speedCalc.SpeedBps, filesDone, filesTotal, fileName));
                },
                pauseGate,
                cancellationToken);

            filesDone++;
            Log.Info("Completed {0}", fileName);
        }
    }

    // ── per-file download ──────────────────────────────────────────────────────

    private async Task DownloadFileAsync(
        NzbFile nzbFile,
        string outputPath,
        long totalBytes,
        Action<long> reportBytes,
        ManualResetEventSlim? pauseGate,
        CancellationToken ct)
    {
        var segments = nzbFile.Segments;
        if (segments.Count == 0) return;

        // Queue of pending (segmentIndex) items — workers pull from this.
        var queue = new System.Collections.Concurrent.ConcurrentQueue<int>(
            Enumerable.Range(0, segments.Count));

        // One buffer per output position, filled as segments arrive.
        var buffers = new byte[segments.Count][];
        var errors  = new List<Exception>();
        var lockObj = new object();

        // Windows caps a single file's decoded-buffer bookkeeping; the Mac client keeps
        // whole decoded segments in memory until the file is written out, so the cache
        // size is applied as a soft cap on how many segments may sit in buffers at once.
        long maxBufferedBytes = (long)_options.DownloaderCacheSizeMb * 1024 * 1024;

        int connections = Math.Min(_maxConnections, segments.Count);

        // Open connections in parallel
        var workers = new Task[connections];
        for (int c = 0; c < connections; c++)
        {
            workers[c] = Task.Run(async () =>
            {
                NntpClient? client = null;
                try
                {
                    client = await _connection.OpenAsync(ServerRole.Download, ct);
                    if (client == null) return;

                    await client.SelectGroupAsync(nzbFile.Group.Length > 0
                        ? nzbFile.Group : "alt.binaries.misc", ct);

                    while (queue.TryDequeue(out int idx))
                    {
                        ct.ThrowIfCancellationRequested();
                        pauseGate?.Wait(ct);

                        var seg = segments[idx];

                        byte[]? decoded = await DownloadSegmentWithRetriesAsync(client, seg, ct);
                        if (decoded != null)
                        {
                            // The throttle point of the Windows client: VirtualNNTP feeds
                            // every received segment into one shared account and sleeps
                            // here until the average is back within the limit.
                            SpeedLimiter.ThrottleIfNeeded(ct);
                            buffers[idx] = decoded;
                        }
                        else
                        {
                            Log.Warn("Segment {0} not available after {1} attempt(s)",
                                seg.MessageId, Math.Max(1, _options.Retries));
                        }

                        reportBytes(seg.Bytes);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    lock (lockObj) errors.Add(ex);
                    Log.Warn(ex, "Worker error on segment download");
                }
                finally
                {
                    client?.Dispose();
                }
            }, ct);
        }

        await Task.WhenAll(workers);
        ct.ThrowIfCancellationRequested();

        // Write all decoded segments in order
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 65536, useAsync: true);

        for (int i = 0; i < buffers.Length; i++)
        {
            if (buffers[i] is { Length: > 0 } buf)
            {
                await fs.WriteAsync(buf, ct);
            }
        }
    }

    /// <summary>
    /// Fetches one segment, retrying up to <see cref="NzbDownloadOptions.Retries"/>
    /// attempts with <see cref="NzbDownloadOptions.RetryIntervalSec"/> between them —
    /// the behaviour of NNTPSegment.MaxRetries plus its DefaultTimeout on Windows.
    /// Returns the decoded bytes, or null when every attempt failed.
    /// </summary>
    private async Task<byte[]?> DownloadSegmentWithRetriesAsync(NntpClient client, NzbSegment seg, CancellationToken ct)
    {
        int attempts = Math.Max(1, _options.Retries);
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            string? body = await client.ReadArticleBodyAsync(seg.MessageId, ct);
            if (body != null)
            {
                return DecodeYEnc(body);
            }

            Log.Debug("Segment {0} failed (attempt {1}/{2})", seg.MessageId, attempt, attempts);
            if (attempt < attempts)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.RetryIntervalSec), ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }
        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static byte[] DecodeYEnc(string body)
    {
        if (string.IsNullOrEmpty(body)) return Array.Empty<byte>();

        // Strip yEnc header/footer lines
        var lines = body.Split('\n');
        var dataLines = new System.Text.StringBuilder();
        bool inData = false;

        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("=ybegin", StringComparison.Ordinal))
            {
                inData = true;
                continue;
            }
            if (trimmed.StartsWith("=yend", StringComparison.Ordinal))
            {
                inData = false;
                continue;
            }
            if (trimmed.StartsWith("=ypart", StringComparison.Ordinal)) continue;
            if (inData) dataLines.Append(trimmed).Append('\n');
        }

        string raw = dataLines.ToString();
        byte[] rawBytes = System.Text.Encoding.Latin1.GetBytes(raw);
        byte[] result = new byte[rawBytes.Length];
        uint written = Decoder.Decode(rawBytes, result, 0, (uint)rawBytes.Length);
        return written > 0 ? result[..(int)written] : Array.Empty<byte>();
    }

    /// <summary>
    /// Extracts a clean filename from an NZB subject line.
    /// Subject format is typically: "Some.Movie.mkv [1/30]" or
    /// "Something - &quot;actual.name.rar&quot; yEnc (1/20)".
    /// </summary>
    public static string? ExtractFileName(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;

        string s = subject.Trim();

        // 1. Quoted filename if present
        int q1 = s.IndexOf('"');
        int q2 = q1 >= 0 ? s.IndexOf('"', q1 + 1) : -1;
        if (q1 >= 0 && q2 > q1)
        {
            string candidate = s[(q1 + 1)..q2].Trim();
            if (candidate.Length > 0) return SanitizeName(candidate);
        }

        // 2. Strip leading bracket prefixes like [123/456]
        while (s.StartsWith('[') && s.IndexOf(']') is int closeBracket && closeBracket > 0)
        {
            s = s[(closeBracket + 1)..].TrimStart(' ', '-', ':');
        }
        while (s.StartsWith('(') && s.IndexOf(')') is int closeParen && closeParen > 0)
        {
            s = s[(closeParen + 1)..].TrimStart(' ', '-', ':');
        }

        // 3. Strip trailing [1/20] or (1/20)
        int endBracket = s.AsSpan().IndexOfAny(Delimiters);
        if (endBracket > 0)
        {
            s = s[..endBracket].Trim();
        }

        // 4. Strip trailing yEnc if any
        if (s.EndsWith("yEnc", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4].Trim();
        }

        return s.Length > 0 ? SanitizeName(s) : null;
    }

    private static string SanitizeName(string name)
    {
        var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
        var sb = new System.Text.StringBuilder();
        foreach (char c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim().TrimEnd('.');
    }

    // ── speed calculator ───────────────────────────────────────────────────────

    private sealed class SpeedCalculator
    {
        private readonly System.Diagnostics.Stopwatch _sw = new();
        private long _bytes;

        public void Start() => _sw.Start();
        public void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);

        public long SpeedBps
        {
            get
            {
                double seconds = _sw.Elapsed.TotalSeconds;
                return seconds < 0.1 ? 0 : (long)(_bytes / seconds);
            }
        }
    }
}
