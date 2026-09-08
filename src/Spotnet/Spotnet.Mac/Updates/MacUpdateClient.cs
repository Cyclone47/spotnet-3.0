using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Spotnet.Mac.Updates;

public readonly record struct MacUpdateProgress(long Received, long Total, double BytesPerSecond = 0)
{
    public double Fraction => Total <= 0 ? 0 : Math.Min(1, (double)Received / Total);
}

public sealed class MacUpdateVerificationException : Exception
{
    public MacUpdateVerificationException(string message) : base(message) { }
}

public sealed class MacUpdateClient : IDisposable
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _manifestUri;

    public MacUpdateClient(Uri manifestUri, HttpMessageHandler? handler = null)
    {
        _manifestUri = manifestUri ?? throw new ArgumentNullException(nameof(manifestUri));
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotnet3-Mac-Updater");
        _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    public async Task<(MacUpdateManifest? Manifest, string Error)> FetchManifestAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var builder = new UriBuilder(_manifestUri);
            string stamp = "t=" + DateTime.UtcNow.Ticks;
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? stamp
                : builder.Query.TrimStart('?') + "&" + stamp;

            using HttpResponseMessage response = await _http.GetAsync(
                builder.Uri, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, $"De updateserver antwoordde met {(int)response.StatusCode} {response.ReasonPhrase}.");

            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return MacUpdateManifest.TryParse(json, out MacUpdateManifest? manifest, out string error)
                ? (manifest, "")
                : (null, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "De updatecontrole duurde te lang.");
        }
        catch (HttpRequestException ex)
        {
            return (null, "De updateserver is niet bereikbaar: " + ex.Message);
        }
    }

    public async Task<string> DownloadAsync(
        MacUpdateManifest manifest,
        string directory,
        IProgress<MacUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!manifest.HasMacAsset || manifest.Url == null)
            throw new MacUpdateVerificationException("Er is geen geldig macOS-installatiebestand gepubliceerd.");

        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Spotnet-macOS-" + manifest.Version + ".zip");
        string partial = target + ".part";
        if (File.Exists(target) && Verifies(target, manifest))
        {
            progress?.Report(new MacUpdateProgress(manifest.Size, manifest.Size));
            return target;
        }
        if (File.Exists(target)) File.Delete(target);

        long resumeFrom = 0;
        if (File.Exists(partial))
        {
            long length = new FileInfo(partial).Length;
            if (length > 0 && length < manifest.Size) resumeFrom = length;
            else File.Delete(partial);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.Url);
        if (resumeFrom > 0) request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"De Mac-download antwoordde met {(int)response.StatusCode} {response.ReasonPhrase}.");

        if (resumeFrom > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            resumeFrom = 0;
            File.Delete(partial);
        }

        using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (var destination = new FileStream(
            partial, resumeFrom > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            long received = resumeFrom;
            var stopwatch = Stopwatch.StartNew();
            progress?.Report(new MacUpdateProgress(received, manifest.Size));
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                double seconds = stopwatch.Elapsed.TotalSeconds;
                double speed = seconds > 0 ? (received - resumeFrom) / seconds : 0;
                progress?.Report(new MacUpdateProgress(received, manifest.Size, speed));
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        long lengthOnDisk = new FileInfo(partial).Length;
        if (lengthOnDisk != manifest.Size)
        {
            File.Delete(partial);
            throw new MacUpdateVerificationException($"De download is {lengthOnDisk} bytes; verwacht {manifest.Size}.");
        }
        if (!string.Equals(ComputeSha256(partial), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new MacUpdateVerificationException("De download komt niet overeen met de SHA-256 uit latest.json.");
        }

        File.Move(partial, target, true);
        return target;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static bool Verifies(string path, MacUpdateManifest manifest)
    {
        try
        {
            return new FileInfo(path).Length == manifest.Size
                && string.Equals(ComputeSha256(path), manifest.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
