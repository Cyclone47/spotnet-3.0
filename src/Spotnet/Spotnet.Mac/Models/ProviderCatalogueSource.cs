using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Models;

/// <summary>
/// Keeps the connect dialog's provider list up to date from the published catalogue, so a provider
/// that shuts down or changes ports can be corrected without shipping a build.
/// </summary>
public static class ProviderCatalogueSource
{
    public const string Url = "https://raw.githubusercontent.com/Cyclone47/spotnet-3.0/main/providers.json";
    public const string CacheFileName = "providers.json";
    public const string ETagFileName = "providers.etag";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8.0);
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object LockRoot = new();

    private static IReadOnlyList<ProviderItem>? _current;
    private static string? _customCacheDirectory;

    public static IAppPaths? AppPaths { get; set; }

    /// <summary>The catalogue the dialog should show: the validated cache, else the built-in list.</summary>
    public static IReadOnlyList<ProviderItem> Current
    {
        get
        {
            lock (LockRoot)
            {
                return _current ??= LoadCache() ?? UsenetProviders.BuiltIn;
            }
        }
    }

    /// <summary>Test seam: drops the loaded catalogue so the next read goes back to disk.</summary>
    public static void Reset()
    {
        lock (LockRoot)
        {
            _current = null;
            _customCacheDirectory = null;
        }
    }

    public static void SetCacheDirectory(string? path)
    {
        lock (LockRoot)
        {
            _customCacheDirectory = path;
            _current = null;
        }
    }

    /// <summary>
    /// Fetches the published catalogue. Returns true only when the visible list actually changed.
    /// </summary>
    public static Task<bool> RefreshAsync() => Task.Run(() => Refresh());

    public static bool Refresh()
    {
        try
        {
            string? folder = ResolveCacheFolder();
            if (folder == null) return false;

            string etagPath = Path.Combine(folder, ETagFileName);
            string? knownEtag = ReadTextFile(etagPath);

            string? body = Download(knownEtag, out string? newEtag);
            if (body == null) return false; // Not modified, unreachable, or refused.

            if (!ProviderCatalogue.TryParse(body, out List<ProviderItem>? providers, out string? error) || providers == null)
            {
                Log.Warn("Ignoring the published provider catalogue: " + error);
                return false;
            }

            lock (LockRoot)
            {
                bool changed = !SameAs(_current ?? UsenetProviders.BuiltIn, providers);
                WriteFileAtomic(Path.Combine(folder, CacheFileName), body);
                if (newEtag != null) WriteFileAtomic(etagPath, newEtag);
                _current = providers;
                Log.Info("Provider catalogue refreshed: {0} entries, changed={1}", providers.Count, changed);
                return changed;
            }
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Provider catalogue refresh failed; keeping the current list.");
            return false;
        }
    }

    private static string? Download(string? knownETag, out string? etag)
    {
        etag = null;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The catalogue URL must be HTTPS.");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Spotnet/3.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(knownETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", knownETag.Trim());
        }

        try
        {
            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Headers.ETag != null)
            {
                etag = response.Headers.ETag.ToString();
            }

            using var stream = response.Content.ReadAsStream();
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > ProviderCatalogue.MaxBytes)
                    throw new InvalidDataException("The published catalogue is larger than " + ProviderCatalogue.MaxBytes + " bytes.");
                buffer.Write(chunk, 0, read);
            }

            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer.ToArray());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to download provider catalogue: {0}", ex.Message);
            return null;
        }
    }

    private static List<ProviderItem>? LoadCache()
    {
        try
        {
            string? folder = ResolveCacheFolder();
            if (folder == null) return null;
            string? cached = ReadTextFile(Path.Combine(folder, CacheFileName));
            if (cached == null) return null;
            if (ProviderCatalogue.TryParse(cached, out List<ProviderItem>? providers, out string? error)) return providers;
            Log.Warn("Discarding the cached provider catalogue: " + error);
            return null;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "The cached provider catalogue could not be read.");
            return null;
        }
    }

    private static string? ResolveCacheFolder()
    {
        if (!string.IsNullOrEmpty(_customCacheDirectory))
        {
            Directory.CreateDirectory(_customCacheDirectory);
            return _customCacheDirectory;
        }

        if (AppPaths != null)
        {
            string folder = !string.IsNullOrEmpty(AppPaths.CacheFolder) ? AppPaths.CacheFolder : AppPaths.DataFolder;
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        try
        {
            var fallback = new StandardAppPaths();
            string folder = fallback.CacheFolder;
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadTextFile(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

    private static void WriteFileAtomic(string path, string contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temporary, path);
    }

    private static bool SameAs(IReadOnlyList<ProviderItem> left, IReadOnlyList<ProviderItem> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            ProviderItem a = left[index], b = right[index];
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Headers, b.Headers, StringComparison.Ordinal) ||
                !string.Equals(a.Download, b.Download, StringComparison.Ordinal) ||
                !string.Equals(a.Upload, b.Upload, StringComparison.Ordinal) ||
                a.HeadersPort != b.HeadersPort || a.DownloadPort != b.DownloadPort || a.UploadPort != b.UploadPort ||
                !string.Equals(a.Group, b.Group, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
