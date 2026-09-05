using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Model;

/// <summary>
/// Keeps the connect dialog's provider list up to date from the published catalogue, so a provider
/// that shuts down or changes ports can be corrected without shipping a build.
/// </summary>
/// <remarks>
/// The built-in list in <see cref="UsenetProviders"/> stays authoritative until a fetched copy has
/// passed <see cref="ProviderCatalogue"/> in full, so the dialog never depends on the network and
/// never degrades because a download was truncated, redirected, or edited badly. The cached copy
/// lives beside the other profile data and is revalidated on every load, not trusted because it is
/// local. Nothing here throws: a failed refresh leaves the previous list in place.
/// </remarks>
internal static class ProviderCatalogueSource
{
    internal const string Url = "https://raw.githubusercontent.com/Cyclone47/spotnet-3.0/main/providers.json";

    internal const string CacheFileName = "providers.json";

    private const string ETagFileName = "providers.etag";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8.0);

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly object LockRoot = new object();

    private static IReadOnlyList<ProviderItem> _current;

    /// <summary>The catalogue the dialog should show: the validated cache, else the built-in list.</summary>
    internal static IReadOnlyList<ProviderItem> Current
    {
        get
        {
            lock (LockRoot)
            {
                return _current ?? (_current = LoadCache() ?? UsenetProviders.BuiltIn);
            }
        }
    }

    /// <summary>Test seam: drops the loaded catalogue so the next read goes back to disk.</summary>
    internal static void Reset()
    {
        lock (LockRoot) _current = null;
    }

    /// <summary>
    /// Fetches the published catalogue. Returns true only when the visible list actually changed,
    /// so callers can avoid rebuilding a dropdown the user may already be interacting with.
    /// </summary>
    internal static Task<bool> RefreshAsync() => Task.Run(() => Refresh());

    private static bool Refresh()
    {
        try
        {
            string folder = CacheFolder();
            if (folder == null) return false;

            string body = Download(ReadTextFile(Path.Combine(folder, ETagFileName)), out string etag);
            if (body == null) return false; // Not modified, unreachable, or refused.

            if (!ProviderCatalogue.TryParse(body, out List<ProviderItem> providers, out string error))
            {
                // Keep serving the previous list; a bad publish must not empty the dialog.
                Log.Warn("Ignoring the published provider catalogue: " + error);
                return false;
            }

            lock (LockRoot)
            {
                bool changed = !SameAs(_current ?? UsenetProviders.BuiltIn, providers);
                WriteFileAtomic(Path.Combine(folder, CacheFileName), body);
                if (etag != null) WriteFileAtomic(Path.Combine(folder, ETagFileName), etag);
                _current = providers;
                Log.Info("Provider catalogue refreshed: " + providers.Count + " entries, changed=" + changed);
                return changed;
            }
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Provider catalogue refresh failed; keeping the current list.");
            return false;
        }
    }

    /// <summary>Returns the body, or null when there is nothing new to apply.</summary>
    private static string Download(string knownETag, out string etag)
    {
        etag = null;
        var request = (HttpWebRequest)WebRequest.Create(Url);
        // HTTPS only: this list decides where credentials get typed, so no plaintext transport and
        // no downgrade via a redirect to http.
        if (request.RequestUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("The catalogue URL must be HTTPS.");
        request.Method = "GET";
        request.Timeout = (int)Timeout.TotalMilliseconds;
        request.ReadWriteTimeout = (int)Timeout.TotalMilliseconds;
        request.AllowAutoRedirect = false;
        request.UserAgent = "Spotnet/" + AppHelper.AppVersion;
        request.Accept = "application/json";
        if (!string.IsNullOrEmpty(knownETag)) request.Headers[HttpRequestHeader.IfNoneMatch] = knownETag;

        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) return null;
                etag = response.Headers[HttpResponseHeader.ETag];
                return ReadCapped(response);
            }
        }
        catch (WebException exception) when ((exception.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotModified)
        {
            return null; // The cache is already current.
        }
    }

    /// <summary>Reads at most <see cref="ProviderCatalogue.MaxBytes"/>, refusing anything longer.</summary>
    private static string ReadCapped(HttpWebResponse response)
    {
        if (response.ContentLength > ProviderCatalogue.MaxBytes)
            throw new InvalidDataException("The published catalogue is larger than " + ProviderCatalogue.MaxBytes + " bytes.");
        using (Stream stream = response.GetResponseStream())
        {
            if (stream == null) return null;
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > ProviderCatalogue.MaxBytes)
                    throw new InvalidDataException("The published catalogue is larger than " + ProviderCatalogue.MaxBytes + " bytes.");
                buffer.Write(chunk, 0, read);
            }
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer.ToArray());
        }
    }

    private static List<ProviderItem> LoadCache()
    {
        try
        {
            string folder = CacheFolder();
            if (folder == null) return null;
            string cached = ReadTextFile(Path.Combine(folder, CacheFileName));
            if (cached == null) return null;
            // Revalidated on every load: being on disk earns no trust.
            if (ProviderCatalogue.TryParse(cached, out List<ProviderItem> providers, out string error)) return providers;
            Log.Warn("Discarding the cached provider catalogue: " + error);
            return null;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "The cached provider catalogue could not be read.");
            return null;
        }
    }

    private static string CacheFolder()
    {
        string folder = AppHelper.SettingsFolder;
        return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? null : folder;
    }

    private static string ReadTextFile(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

    private static void WriteFileAtomic(string path, string contents)
    {
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
