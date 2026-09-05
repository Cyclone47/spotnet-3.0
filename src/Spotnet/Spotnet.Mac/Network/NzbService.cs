using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Model;
using Spotnet.Platform;
using SpotnetEnc;

namespace Spotnet.Mac.Network;

/// <summary>
/// Handles the Download button action for a spot. The behaviour depends on the
/// user's "Downloadknop" preference (mirrors Windows Bewerken › Downloadknop):
///
///   Integrated — download the actual binary files from Usenet directly inside Spotnet
///   OpenNzb    — save .nzb and open it with the OS default handler
///   SaveNzb    — only save the .nzb file
/// </summary>
public sealed class NzbService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly SpotnetDecoder Decoder = new();

    private readonly IAppPaths _appPaths;
    private readonly UsenetConnection _connection;
    private readonly UserPreferencesService _prefsService;

    public NzbService(IAppPaths appPaths, ISecretStore secretStore,
                      UserPreferencesService? prefsService = null)
    {
        _appPaths = appPaths;
        _connection = new UsenetConnection(appPaths, secretStore);
        _prefsService = prefsService ?? new UserPreferencesService(appPaths);
    }

    // ── Public entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Executes the current download mode for <paramref name="spot"/>.
    /// Returns (success, nzbPath, message, job).
    /// <c>job</c> is non-null only when mode is <see cref="DownloadMode.Integrated"/>;
    /// it represents the still-running background binary download.
    /// </summary>
    public async Task<(bool success, string? filePath, string message, NzbDownloadJob? job)>
        DownloadAsync(SpotItem spot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spot);

        try
        {
            // Step 1 – always: fetch the NZB metadata from Usenet and save it.
            var (nzbXml, nzbPath, fetchMsg) = await FetchAndSaveNzbAsync(spot, cancellationToken);
            if (nzbXml == null)
                return (false, null, fetchMsg, null);

            var prefs = _prefsService.Current;

            switch (prefs.DownloadMode)
            {
                case DownloadMode.SaveNzb:
                    return (true, nzbPath, $"✓ NZB opgeslagen: {Path.GetFileName(nzbPath)}", null);

                case DownloadMode.OpenNzb:
                    OpenWithDefaultApp(nzbPath!);
                    return (true, nzbPath, $"✓ NZB geopend met standaard-app: {Path.GetFileName(nzbPath)}", null);

                case DownloadMode.Integrated:
                default:
                    // Parse the NZB and start the binary download.
                    var nzbFiles = NzbParser.Parse(nzbXml);
                    if (nzbFiles.Count == 0)
                    {
                        return (false, nzbPath, "NZB bevat geen downloadbare bestanden.", null);
                    }

                    string downloadDir = ResolveDownloadDir(spot.Subject, prefs);
                    int maxConn = prefs.MaxDownloadConnections > 0
                        ? prefs.MaxDownloadConnections
                        : 4;

                    var job = new NzbDownloadJob(_connection, nzbFiles, downloadDir, maxConn);
                    return (true, nzbPath,
                        $"⬇ Downloaden gestart ({nzbFiles.Count} bestanden) → {downloadDir}", job);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fout bij downloaden van spot {0}: {1}", spot.MsgId, ex.Message);
            return (false, null, $"Fout: {ex.Message}", null);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Fetches the NZB from Usenet and saves it; returns the XML and path.</summary>
    private async Task<(string? xml, string? path, string message)>
        FetchAndSaveNzbAsync(SpotItem spot, CancellationToken ct)
    {
        using var client = await _connection.OpenAsync(ct);
        if (client == null)
            return (null, null, "Geen Usenet-server geconfigureerd.");

        await client.SelectGroupAsync("free.pt", ct);

        string? article = await client.ReadArticleAsync(spot.MsgId, ct);
        if (string.IsNullOrWhiteSpace(article))
            return (null, null, "Kon spot-bericht niet ophalen van Usenet.");

        var (headers, _) = SpotArticle.Split(article);
        var posting = SpotArticle.ParsePosting(SpotArticle.ExtractXml(headers));
        if (posting == null)
            return (null, null, "Kon de spot-XML niet lezen.");
        if (!posting.HasNzb)
            return (null, null, "Geen NZB-segment gevonden in deze spot.");

        // Fetch and join all NZB segments in order
        var payload = new System.Collections.Generic.List<byte>();
        foreach (string segment in posting.NzbSegments)
        {
            string? raw = await client.ReadArticleBodyAsync(segment, ct);
            if (string.IsNullOrWhiteSpace(raw))
                return (null, null, $"NZB-segment {segment} is niet meer op de server.");
            payload.AddRange(SpotArticle.DecodeBinary(raw));
        }

        string? nzbXml = SpotArticle.InflateNzb(payload.ToArray());
        if (nzbXml == null)
            return (null, null, "NZB kon niet worden uitgepakt (deflate-fout).");
        if (!nzbXml.Contains("<nzb", StringComparison.OrdinalIgnoreCase))
            return (null, null, "Gedownload bestand is geen geldig NZB XML-document.");

        // Save to the .nzb folder
        string nzbDir  = _appPaths.DownloadsFolder;
        Directory.CreateDirectory(nzbDir);

        string cleanName = SanitizeFileName(spot.Subject);
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "spotnet_download";
        string filePath = Path.Combine(nzbDir, $"{cleanName}.nzb");

        await File.WriteAllTextAsync(filePath, nzbXml, Encoding.Latin1, ct);
        Log.Info("Saved NZB to {0}", filePath);

        return (nzbXml, filePath, "");
    }

    private string ResolveDownloadDir(string spotSubject, UserPreferences prefs)
    {
        string baseDir = string.IsNullOrWhiteSpace(prefs.DownloadFolder)
            ? Path.Combine(_appPaths.DownloadsFolder, "Spotnet")
            : prefs.DownloadFolder;

        string safeName = SanitizeFileName(spotSubject);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "download";

        return Path.Combine(baseDir, safeName);
    }

    private static void OpenWithDefaultApp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not open {0} with default app", path);
        }
    }

    // ── Static helpers (kept for tests and NzbService.SanitizeFileName callers) ─

    private static readonly char[] ExtraInvalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    public static string SanitizeFileName(string name)
    {
        var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (char c in ExtraInvalidChars) invalid.Add(c);

        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString().Trim().TrimEnd('.');
    }

    public static string DecodeYEncString(string rawBody)
    {
        if (rawBody.Contains("=ybegin"))
        {
            try
            {
                byte[] raw = Encoding.Latin1.GetBytes(rawBody);
                byte[] decoded = new byte[raw.Length];
                uint written = Decoder.Decode(raw, decoded, 0, (uint)raw.Length);
                if (written > 0)
                {
                    return Encoding.UTF8.GetString(decoded, 0, (int)written);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "yEnc decoding error");
            }
        }
        return rawBody;
    }
}
