using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Spotnet.Deployment;

/// <summary>How far a download has come, for the progress window.</summary>
internal readonly struct UpdateProgress
{
    internal UpdateProgress(long received, long total, double bytesPerSecond)
    {
        Received = received;
        Total = total;
        BytesPerSecond = bytesPerSecond;
    }

    internal long Received { get; }

    internal long Total { get; }

    internal double BytesPerSecond { get; }

    internal double Fraction => Total > 0L ? Math.Min(1.0, (double)Received / Total) : 0.0;
}

/// <summary>Raised for a download that arrived but is not the file the manifest described.</summary>
internal sealed class UpdateVerificationException : Exception
{
    internal UpdateVerificationException(string message) : base(message) { }
}

/// <summary>
/// Reads the manifest and fetches the installer it names. Holds no user state and touches
/// no settings, so a test can point it at a local server and drive the whole path.
/// </summary>
internal sealed class UpdateClient : IDisposable
{
    private const int BufferSize = 128 * 1024;

    private readonly Uri _manifestUrl;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    internal UpdateClient(Uri manifestUrl, HttpMessageHandler handler = null)
    {
        _manifestUrl = manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl));
        _ownsHttp = true;
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // The download sets its own deadline through the cancellation token; a fixed
        // timeout would abort a large installer on a slow line.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotnet3-Updater");
        _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    /// <summary>
    /// Reads the manifest. A missing file, a private repository, no network, or anything
    /// else the server says comes back as null with the reason in <paramref name="error"/>;
    /// the periodic check has nowhere to report an exception to.
    /// </summary>
    internal async Task<UpdateManifest> FetchManifestAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        (UpdateManifest manifest, _) = await FetchManifestWithReasonAsync(cancellationToken, timeout).ConfigureAwait(false);
        return manifest;
    }

    internal async Task<(UpdateManifest Manifest, string Error)> FetchManifestWithReasonAsync(
        CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30.0));
        try
        {
            // raw.githubusercontent.com serves the manifest through a CDN with a five
            // minute lifetime, and neither this parameter nor the no-cache headers above
            // shorten it: a release can take that long to become visible. Harmless for a
            // check that runs at startup and every four hours, and worth knowing before
            // anyone concludes a published update was missed.
            var url = new UriBuilder(_manifestUrl);
            string stamp = "t=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            url.Query = string.IsNullOrEmpty(url.Query) ? stamp : url.Query.TrimStart('?') + "&" + stamp;

            using HttpResponseMessage response = await _http
                .GetAsync(url.Uri, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"The update server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            string json = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return UpdateManifest.TryParse(json, out UpdateManifest manifest, out string error)
                ? (manifest, null)
                : (null, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            return (null, "The update server could not be reached: " + ex.Message);
        }
        catch (IOException ex)
        {
            return (null, "The update check failed while reading the response: " + ex.Message);
        }
    }

    /// <summary>
    /// Downloads the installer into <paramref name="directory"/> and returns its path once
    /// the bytes match the manifest. A partial file from an earlier attempt is continued
    /// rather than restarted, and a finished file that still verifies is reused outright.
    /// </summary>
    internal async Task<string> DownloadAsync(UpdateManifest manifest, string directory,
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Spotnet-3.0-x64-Setup-" + manifest.Version + ".exe");
        string partial = target + ".part";

        if (File.Exists(target))
        {
            if (Verifies(target, manifest))
            {
                progress?.Report(new UpdateProgress(manifest.Size, manifest.Size, 0.0));
                return target;
            }
            File.Delete(target);
        }

        long resumeFrom = 0L;
        if (File.Exists(partial))
        {
            long length = new FileInfo(partial).Length;
            // A part file at or past the full size is from another release or a bad write.
            resumeFrom = length > 0L && length < manifest.Size ? length : 0L;
            if (resumeFrom == 0L) File.Delete(partial);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, manifest.Url))
        {
            if (resumeFrom > 0L) request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The download answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            // A server that ignores the range answers 200 with the whole file, so the
            // partial copy has to go rather than be prepended to a second full body.
            if (resumeFrom > 0L && response.StatusCode != HttpStatusCode.PartialContent)
            {
                resumeFrom = 0L;
                File.Delete(partial);
            }

            using Stream network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var file = new FileStream(partial, resumeFrom > 0L ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

            var buffer = new byte[BufferSize];
            long received = resumeFrom;
            var clock = Stopwatch.StartNew();
            long windowStart = resumeFrom;
            TimeSpan lastReport = TimeSpan.Zero;
            progress?.Report(new UpdateProgress(received, manifest.Size, 0.0));

            int read;
            while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                TimeSpan now = clock.Elapsed;
                if (progress != null && (now - lastReport) >= TimeSpan.FromMilliseconds(200.0))
                {
                    double seconds = (now - lastReport).TotalSeconds;
                    double rate = seconds > 0.0 ? (received - windowStart) / seconds : 0.0;
                    progress.Report(new UpdateProgress(received, manifest.Size, rate));
                    lastReport = now;
                    windowStart = received;
                }
            }
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        long finalLength = new FileInfo(partial).Length;
        if (finalLength != manifest.Size)
        {
            File.Delete(partial);
            throw new UpdateVerificationException(
                $"The download is {finalLength} bytes; the manifest says {manifest.Size}.");
        }
        if (!string.Equals(ComputeSha256(partial), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new UpdateVerificationException("The download does not match the SHA-256 in the manifest.");
        }

        File.Move(partial, target, overwrite: true);
        progress?.Report(new UpdateProgress(manifest.Size, manifest.Size, 0.0));
        return target;
    }

    private static bool Verifies(string path, UpdateManifest manifest)
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

    internal static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
