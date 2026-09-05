using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json.Linq;
using Spotnet.Helpers;

namespace Spotnet.Browser;

/// <summary>
/// Builds the release notes page from the project's own GitHub releases, so publishing a
/// release is what updates the notes and there is no second list to keep in step. The
/// bundled notes stay as the fallback for a client that has never reached GitHub.
///
/// GitHub renders the Markdown itself: asking for the html+json media type returns
/// body_html, already sanitised at the source, which saves carrying a Markdown parser and
/// keeps the page free of any script it would have needed to render one.
/// </summary>
internal static class ReleaseNotesFeed
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string ApiUrl = "https://api.github.com/repos/Cyclone47/spotnet-3.0/releases?per_page=30";

    /// <summary>Opening the page waits no longer than this for GitHub before using the cache.</summary>
    private static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(3.0);

    /// <summary>A cache younger than this is used as it stands.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6.0);

    private static string CachePath => Path.Combine(AppHelper.SettingsFolder, "releasenotes.cache.html");

    /// <summary>
    /// The notes to put in the page. Fresh from GitHub when the cache is old and the
    /// network answers in time, the cache when it does not, and the notes built into this
    /// build when there is no cache at all. Never throws: the page must open regardless.
    /// </summary>
    internal static string GetNotesHtml(string bundledFallback)
    {
        bool isDutch = UserLanguageHelper.Language == UserLanguageHelper.Dutch;
        if (isDutch)
        {
            // For Dutch users, the bundled notes provide the complete, authoritative Dutch changelog.
            string currentVer = AppHelper.AppVersion?.ToString() ?? "3.0.7.0";
            string currentVerShort = AppHelper.AppVersion != null ? AppHelper.AppVersion.ToString(3) : "3.0.7";
            if (!bundledFallback.Contains("Spotnet " + currentVer) && !bundledFallback.Contains("Spotnet " + currentVerShort))
            {
                string currentNotes = ExtractCurrentVersionSection(bundledFallback, currentVer, currentVerShort);
                if (!string.IsNullOrWhiteSpace(currentNotes))
                {
                    bundledFallback = currentNotes + bundledFallback;
                }
            }
            return bundledFallback;
        }

        string changelog = null;
        try
        {
            changelog = ReadUsableCache();
            if (changelog == null)
            {
                Task<string> fetch = Task.Run(() => FetchAsync(CancellationToken.None));
                if (fetch.Wait(FetchBudget)) changelog = fetch.Result;
                else Log.Debug("GitHub releases did not answer within {0}; using what is on hand.", FetchBudget);
            }
            // A stale cache still beats nothing when the network is slow or gone.
            changelog ??= ReadCache();
        }
        catch (Exception ex)
        {
            Log.Debug("Could not build the release notes from GitHub: {0}", ex.Message);
        }

        if (string.IsNullOrWhiteSpace(changelog)) return bundledFallback;

        // If the current running version is not yet in the GitHub releases list,
        // extract this build's notes from bundledFallback and prepend them so the user
        // immediately sees the what's new for the version they are actually running.
        string currentVersion = AppHelper.AppVersion?.ToString() ?? "3.0.7.0";
        string currentVersionShort = AppHelper.AppVersion != null ? AppHelper.AppVersion.ToString(3) : "3.0.7";
        if (!changelog.Contains(currentVersion) && !changelog.Contains(currentVersionShort))
        {
            string currentNotes = ExtractCurrentVersionSection(bundledFallback, currentVersion, currentVersionShort);
            if (!string.IsNullOrWhiteSpace(currentNotes))
            {
                changelog = currentNotes + changelog;
            }
        }

        // Everything older than the releases on GitHub, kept but out of the way.
        return changelog + BuildArchive(bundledFallback);
    }

    private static string ReadUsableCache()
    {
        try
        {
            var file = new FileInfo(CachePath);
            if (!file.Exists || file.Length == 0L) return null;
            if (DateTime.UtcNow - file.LastWriteTimeUtc > CacheLifetime) return null;
            string cached = File.ReadAllText(CachePath, Encoding.UTF8);

            // A cache created by an older version should not prevent fetching notes for the new version.
            string currentVersion = AppHelper.AppVersion.ToString();
            string currentVersionShort = AppHelper.AppVersion.ToString(3);
            if (!cached.Contains(currentVersion) && !cached.Contains(currentVersionShort))
            {
                return null;
            }

            return cached;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ReadCache()
    {
        try
        {
            return File.Exists(CachePath) ? File.ReadAllText(CachePath, Encoding.UTF8) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<string> FetchAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20.0) };
        // The API refuses a request without one, and html+json is what makes it render
        // the Markdown into body_html for us.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotnet3-ReleaseNotes");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.html+json");

        using HttpResponseMessage response = await http.GetAsync(ApiUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log.Debug("GitHub releases answered {0}.", (int)response.StatusCode);
            return null;
        }
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string html = BuildHtml(json);
        if (html == null) return null;

        try
        {
            AppHelper.EnsureDirectoryExist(AppHelper.SettingsFolder);
            File.WriteAllText(CachePath, html, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            Log.Debug("Could not cache the release notes: {0}", ex.Message);
        }
        return html;
    }

    /// <summary>
    /// Turns the API's answer into the page's own markup. Returns null for anything that
    /// is not a usable list, so the caller falls back rather than showing an empty page.
    /// </summary>
    internal static string BuildHtml(string json)
    {
        JArray releases;
        try
        {
            releases = JArray.Parse(json);
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException)
        {
            Log.Debug("The releases feed was not a list: {0}", ex.Message);
            return null;
        }

        var html = new StringBuilder();
        int shown = 0;
        foreach (JToken token in releases)
        {
            if (token is not JObject release) continue;
            // A draft is not published to anyone and has no business in the notes.
            if ((bool?)release["draft"] == true) continue;

            string body = (string)release["body_html"];
            if (string.IsNullOrWhiteSpace(body)) continue;

            string title = FirstNonEmpty((string)release["name"], (string)release["tag_name"]);
            if (title == null) continue;

            html.Append("<section class=\"notes-section gh-release\">");
            html.Append("<h3>").Append(Escape(title));
            if ((bool?)release["prerelease"] == true)
            {
                html.Append(" <span class=\"gh-tag\">pre-release</span>");
            }
            string published = FormatDate((string)release["published_at"]);
            if (published != null) html.Append("<span class=\"gh-date\">").Append(Escape(published)).Append("</span>");
            html.Append("</h3>");
            // body_html is GitHub's own rendering of the release description, which it
            // sanitises before returning. The page it lands in has no bridge to the
            // application, so nothing there can reach past the browser control.
            html.Append("<div class=\"gh-body\">").Append(body).Append("</div>");
            html.Append("</section>");
            shown++;
        }
        return shown == 0 ? null : html.ToString();
    }

    private static string BuildArchive(string bundled)
    {
        if (string.IsNullOrWhiteSpace(bundled)) return string.Empty;
        bool dutch = UserLanguageHelper.Language == UserLanguageHelper.Dutch;
        string label = dutch ? "Oudere versies" : "Earlier versions";
        return "<section class=\"notes-section gh-archive\"><details><summary>" + Escape(label)
            + "</summary>" + bundled + "</details></section>";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static string FormatDate(string published)
    {
        if (!DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset when))
        {
            return null;
        }
        var culture = new CultureInfo(UserLanguageHelper.Language == UserLanguageHelper.Dutch ? "nl-NL" : "en-GB");
        return when.ToLocalTime().ToString("d MMMM yyyy", culture);
    }

    private static string ExtractCurrentVersionSection(string bundled, string version, string shortVersion)
    {
        if (string.IsNullOrWhiteSpace(bundled)) return null;
        try
        {
            string[] searchTags = new[] { $"<h5>{version}</h5>", $"<h5>{shortVersion}</h5>" };
            int idx = -1;
            int tagLen = 0;
            foreach (string tag in searchTags)
            {
                idx = bundled.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    tagLen = tag.Length;
                    break;
                }
            }
            if (idx < 0) return null;

            int contentStart = idx + tagLen;
            int nextIdx = bundled.IndexOf("<h5", contentStart, StringComparison.OrdinalIgnoreCase);
            if (nextIdx < 0)
            {
                nextIdx = bundled.IndexOf("</section>", contentStart, StringComparison.OrdinalIgnoreCase);
            }
            string body = (nextIdx > contentStart)
                ? bundled.Substring(contentStart, nextIdx - contentStart).Trim()
                : bundled.Substring(contentStart).Trim();

            while (body.EndsWith("<br>", StringComparison.OrdinalIgnoreCase) ||
                   body.EndsWith("<br/>", StringComparison.OrdinalIgnoreCase) ||
                   body.EndsWith("<br />", StringComparison.OrdinalIgnoreCase))
            {
                int br = body.LastIndexOf('<');
                body = body.Substring(0, br).Trim();
            }

            bool dutch = UserLanguageHelper.Language == UserLanguageHelper.Dutch;
            string tagLabel = dutch ? "Nieuw" : "New";
            string dateLabel = dutch ? "Huidige versie" : "Current version";

            return $"<section class=\"notes-section gh-release\">" +
                   $"<h3>Spotnet {Escape(version)} <span class=\"gh-tag\">{tagLabel}</span><span class=\"gh-date\">{dateLabel}</span></h3>" +
                   $"<div class=\"gh-body\">{body}</div>" +
                   $"</section>";
        }
        catch (Exception ex)
        {
            Log.Debug("Could not extract current version notes: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>Escapes the parts this class writes itself; body_html arrives ready.</summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
