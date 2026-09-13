using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Drives <see cref="NzbDownloadJob"/> against a fake NNTP server on localhost, so the
/// whole production path is under test: UsenetConnection → NntpClient → yEnc decode →
/// the bytes that actually land on disk.
///
/// These are the cases that used to pass silently. A segment the server did not have
/// was logged at Warn and counted towards progress anyway, so the job produced a
/// truncated file and reported success; a worker that could not connect was
/// indistinguishable from a finished download.
/// </summary>
[Collection("Shared speed limiter")]
public sealed class NzbDownloadJobTests
{
    private const int TimeoutMs = 60_000;

    /// <summary>
    /// Payload bytes stay inside 0x30..0x6B so the yEnc output (byte + 42) lands in
    /// 0x5A..0x91: nothing needs escaping and no encoded line can start with a dot.
    /// </summary>
    private static byte[] Payload(int seed, int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(0x30 + ((seed + i) % 0x3C));
        }
        return data;
    }

    /// <summary>Wraps payload in the =ybegin/=yend frame NzbDownloadJob.DecodeYEnc expects.</summary>
    private static string EncodeYEnc(byte[] data, int part, int lineLength = 128)
    {
        var sb = new StringBuilder();
        sb.Append("=ybegin part=").Append(part).Append(" line=").Append(lineLength)
          .Append(" size=").Append(data.Length).Append(" name=test.bin\r\n");

        int column = 0;
        foreach (byte b in data)
        {
            int encoded = (b + 42) % 256;
            if (encoded is 0 or 10 or 13 or 61)
            {
                sb.Append('=');
                encoded = (encoded + 64) % 256;
                column++;
            }
            sb.Append((char)encoded);
            if (++column >= lineLength)
            {
                sb.Append("\r\n");
                column = 0;
            }
        }
        if (column > 0) sb.Append("\r\n");

        sb.Append("=yend size=").Append(data.Length).Append(" part=").Append(part).Append("\r\n");
        return sb.ToString();
    }

    private static void WriteDownloadServer(TempAppPaths paths, int port)
    {
        var root = new XElement("Spotnet",
            new XElement("Server",
                new XAttribute("Type", "Downloads"),
                new XAttribute("Server", "127.0.0.1"),
                new XAttribute("Port", port),
                new XAttribute("SSL", "0"),
                new XAttribute("Connections", 4)));
        new XDocument(root).Save(Path.Combine(paths.DataFolder, "servers.xml"));
    }

    private static NzbDownloadJob CreateJob(TempAppPaths paths, IReadOnlyList<NzbFile> files,
                                            string outputDir, int maxConnections,
                                            int cacheSizeMb = 20)
    {
        var connection = new UsenetConnection(paths, new FakeSecretStore());
        return new NzbDownloadJob(connection, files, outputDir, maxConnections,
            new NzbDownloadOptions(Retries: 1, RetryIntervalSec: 1,
                                   SpeedLimitKbps: -1, DownloaderCacheSizeMb: cacheSizeMb));
    }

    /// <summary>Runs the job under a timeout so a deadlock fails instead of hanging CI.</summary>
    private static async Task RunWithTimeoutAsync(NzbDownloadJob job)
    {
        using var cts = new CancellationTokenSource(TimeoutMs);
        await job.RunAsync(cancellationToken: cts.Token);
    }

    [Fact]
    public async Task Every_segment_present_writes_the_exact_file()
    {
        using var paths = new TempAppPaths();
        byte[][] parts = { Payload(1, 5000), Payload(2, 5000), Payload(3, 5000), Payload(4, 5000) };

        var articles = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = new List<NzbSegment>();
        for (int i = 0; i < parts.Length; i++)
        {
            string id = $"seg{i}@test";
            articles[id] = EncodeYEnc(parts[i], i + 1);
            segments.Add(new NzbSegment(i + 1, id, parts[i].Length));
        }

        using var server = new FakeNntpServer(id => articles.TryGetValue(id, out var body) ? body : null);
        WriteDownloadServer(paths, server.Port);

        string outDir = Path.Combine(paths.DataFolder, "out");
        var job = CreateJob(paths,
            new[] { new NzbFile("test.bin yEnc (1/4)", "poster", "alt.binaries.test", segments) },
            outDir, maxConnections: 3);

        await RunWithTimeoutAsync(job);

        string file = Path.Combine(outDir, "test.bin");
        Assert.True(File.Exists(file), "the assembled file should exist");
        Assert.Equal(parts.SelectMany(p => p).ToArray(), await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task A_segment_the_server_lacks_fails_the_download_instead_of_truncating_it()
    {
        using var paths = new TempAppPaths();
        byte[][] parts = { Payload(1, 4000), Payload(2, 4000), Payload(3, 4000) };

        var articles = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = new List<NzbSegment>();
        for (int i = 0; i < parts.Length; i++)
        {
            string id = $"seg{i}@test";
            // The middle segment is simply not on the server any more.
            if (i != 1) articles[id] = EncodeYEnc(parts[i], i + 1);
            segments.Add(new NzbSegment(i + 1, id, parts[i].Length));
        }

        using var server = new FakeNntpServer(id => articles.TryGetValue(id, out var body) ? body : null);
        WriteDownloadServer(paths, server.Port);

        string outDir = Path.Combine(paths.DataFolder, "out");
        string file = Path.Combine(outDir, "test.bin");
        var job = CreateJob(paths,
            new[] { new NzbFile("test.bin yEnc (1/3)", "poster", "alt.binaries.test", segments) },
            outDir, maxConnections: 1);

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () => await RunWithTimeoutAsync(job));
        Assert.False(failure is OperationCanceledException,
            "the job should fail on the missing segment, not run into the test timeout");

        Assert.False(File.Exists(file),
            "a short file must not survive: post-processing treats whatever is in the directory as the finished download");
    }

    [Fact]
    public async Task A_server_that_refuses_the_connection_fails_the_download()
    {
        using var paths = new TempAppPaths();

        // Bind a listener only to learn a port that is guaranteed to be free, then
        // close it, so the connection attempt is refused rather than hanging.
        int deadPort;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        WriteDownloadServer(paths, deadPort);

        string outDir = Path.Combine(paths.DataFolder, "out");
        var job = CreateJob(paths,
            new[]
            {
                new NzbFile("test.bin yEnc (1/1)", "poster", "alt.binaries.test",
                    new[] { new NzbSegment(1, "seg0@test", 100) })
            },
            outDir, maxConnections: 2);

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () => await RunWithTimeoutAsync(job));
        Assert.False(failure is OperationCanceledException,
            "the job should fail on the refused connection, not run into the test timeout");
    }

    [Fact]
    public async Task A_cache_budget_below_the_release_size_still_assembles_the_file()
    {
        using var paths = new TempAppPaths();

        // 8 MB across eight segments against a 1 MB reorder budget, so the workers
        // have to wait on the writer rather than buffering the whole release.
        const int partSize = 1024 * 1024;
        byte[][] parts = Enumerable.Range(0, 8).Select(i => Payload(i * 7, partSize)).ToArray();

        var articles = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = new List<NzbSegment>();
        for (int i = 0; i < parts.Length; i++)
        {
            string id = $"seg{i}@test";
            articles[id] = EncodeYEnc(parts[i], i + 1);
            segments.Add(new NzbSegment(i + 1, id, parts[i].Length));
        }

        using var server = new FakeNntpServer(id => articles.TryGetValue(id, out var body) ? body : null);
        WriteDownloadServer(paths, server.Port);

        string outDir = Path.Combine(paths.DataFolder, "out");
        var job = CreateJob(paths,
            new[] { new NzbFile("test.bin yEnc (1/8)", "poster", "alt.binaries.test", segments) },
            outDir, maxConnections: 4, cacheSizeMb: 1);

        await RunWithTimeoutAsync(job);

        byte[] written = await File.ReadAllBytesAsync(Path.Combine(outDir, "test.bin"));
        Assert.Equal(parts.Sum(p => p.Length), written.Length);
        Assert.Equal(parts.SelectMany(p => p).ToArray(), written);
    }

    [Fact]
    public async Task Two_files_with_the_same_subject_do_not_overwrite_each_other()
    {
        using var paths = new TempAppPaths();
        byte[] first = Payload(1, 3000);
        byte[] second = Payload(2, 3000);

        var articles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a@test"] = EncodeYEnc(first, 1),
            ["b@test"] = EncodeYEnc(second, 1)
        };

        using var server = new FakeNntpServer(id => articles.TryGetValue(id, out var body) ? body : null);
        WriteDownloadServer(paths, server.Port);

        string outDir = Path.Combine(paths.DataFolder, "out");
        var job = CreateJob(paths,
            new[]
            {
                // Both subjects sanitise to "duplicate.bin".
                new NzbFile("duplicate.bin yEnc (1/1)", "poster", "alt.binaries.test",
                    new[] { new NzbSegment(1, "a@test", first.Length) }),
                new NzbFile("duplicate.bin yEnc (1/1)", "poster", "alt.binaries.test",
                    new[] { new NzbSegment(1, "b@test", second.Length) })
            },
            outDir, maxConnections: 1);

        await RunWithTimeoutAsync(job);

        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(outDir, "duplicate.bin")));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(outDir, "duplicate (2).bin")));
    }

    /// <summary>
    /// The smallest NNTP server that satisfies NntpClient: a 200 greeting, GROUP and
    /// BODY. No Username is written into servers.xml, so AUTHINFO is never attempted.
    /// </summary>
    private sealed class FakeNntpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<string, string?> _bodyFor;
        private readonly CancellationTokenSource _cts = new();

        public FakeNntpServer(Func<string, string?> bodyFor)
        {
            _bodyFor = bodyFor;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    return;   // listener stopped
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var reader = new StreamReader(stream, Encoding.Latin1,
                    detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var writer = new StreamWriter(stream, Encoding.Latin1, 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n"
                };

                await writer.WriteLineAsync("200 fake NNTP ready");

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("GROUP ", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync($"211 100 1 100 {line[6..]}");
                    }
                    else if (line.StartsWith("BODY ", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = line[5..].Trim().Trim('<', '>');
                        string? body = _bodyFor(id);
                        if (body == null)
                        {
                            await writer.WriteLineAsync("430 no such article");
                        }
                        else
                        {
                            await writer.WriteLineAsync($"222 0 <{id}> body follows");
                            await writer.WriteAsync(body);
                            await writer.WriteLineAsync(".");
                        }
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("205 closing");
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("500 unknown command");
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
