using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spotnet.Deployment;

/// <summary>
/// The update descriptor published in the repository, read straight from the default
/// branch. A build can be tagged, uploaded and tested before anyone is offered it:
/// clients ignore the entry entirely until <c>clientUpdate</c> is set to 1.
/// </summary>
internal sealed class UpdateManifest
{
    /// <summary>Manifests declaring a newer schema are ignored rather than guessed at.</summary>
    internal const int SupportedSchema = 1;

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    private UpdateManifest() { }

    internal int Schema { get; private init; }

    /// <summary>The publisher's release gate. False keeps the entry invisible to clients.</summary>
    internal bool ClientUpdate { get; private init; }

    internal Version Version { get; private init; }

    /// <summary>Clients older than this are told the update is required.</summary>
    internal Version MinimumVersion { get; private init; }

    /// <summary>The publisher's override for a release nobody should stay behind on.</summary>
    internal bool Forced { get; private init; }

    internal Uri Url { get; private init; }

    internal long Size { get; private init; }

    internal string Sha256 { get; private init; }

    internal Uri ReleaseNotesUrl { get; private init; }

    /// <summary>
    /// The download may only come from GitHub over TLS. The manifest names both the file
    /// and its hash, so a manifest that could point anywhere would be a manifest that
    /// could ship anything; restricting the host keeps that trust on one server.
    ///
    /// The loopback address is allowed as well. It reaches nothing but this machine, so it
    /// grants an attacker nothing, and it is how a release is rehearsed end to end against
    /// a local server before the real one is published.
    /// </summary>
    internal static bool IsTrustedDownloadHost(Uri url)
    {
        if (url == null || !url.IsAbsoluteUri) return false;
        if (url.IsLoopback
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        string host = url.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a manifest. Never throws: a truncated file, an HTML error page served in its
    /// place or a field the publisher mistyped all come back as a reason, because an
    /// update check runs unattended and must not take the application down with it.
    /// </summary>
    internal static bool TryParse(string json, out UpdateManifest manifest, out string error)
    {
        manifest = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The update manifest was empty.";
            return false;
        }

        JObject root;
        try
        {
            // A manifest written by a Windows editor or PowerShell can carry a byte-order
            // mark that survives the transfer, and the parser will not look past it.
            root = JObject.Parse(json.TrimStart('\uFEFF', '\u200B').TrimStart());
        }
        catch (JsonException ex)
        {
            error = "The update manifest is not valid JSON: " + ex.Message;
            return false;
        }

        int schema = (int?)root["schema"] ?? 0;
        if (schema <= 0)
        {
            error = "The update manifest has no schema number.";
            return false;
        }
        if (schema > SupportedSchema)
        {
            error = $"The update manifest uses schema {schema}; this build understands {SupportedSchema}.";
            return false;
        }

        if (!TryReadVersion(root["version"], out Version version))
        {
            error = "The update manifest has no usable version.";
            return false;
        }
        // Absent means "every version may upgrade in place".
        if (!TryReadVersion(root["minimumVersion"], out Version minimum)) minimum = new Version(0, 0, 0, 0);

        string rawUrl = (string)root["url"];
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri url) || !IsTrustedDownloadHost(url))
        {
            error = "The update manifest does not point at an https download on GitHub.";
            return false;
        }

        string sha256 = ((string)root["sha256"] ?? string.Empty).Trim();
        if (!Sha256Pattern.IsMatch(sha256))
        {
            error = "The update manifest has no SHA-256 for the download.";
            return false;
        }

        long size = (long?)root["size"] ?? 0L;
        if (size <= 0L)
        {
            error = "The update manifest has no download size.";
            return false;
        }

        Uri.TryCreate((string)root["releaseNotesUrl"], UriKind.Absolute, out Uri notes);

        manifest = new UpdateManifest
        {
            Schema = schema,
            ClientUpdate = ReadFlag(root["clientUpdate"]),
            Version = version,
            MinimumVersion = minimum,
            Forced = ReadFlag(root["forced"]),
            Url = url,
            Size = size,
            Sha256 = sha256.ToLowerInvariant(),
            ReleaseNotesUrl = notes,
        };
        return true;
    }

    /// <summary>Accepts 1/0 as well as true/false, so the flag can be written either way.</summary>
    private static bool ReadFlag(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return false;
        if (token.Type == JTokenType.Boolean) return (bool)token;
        if (token.Type == JTokenType.Integer) return (long)token != 0L;
        string text = ((string)token ?? string.Empty).Trim();
        return text.Equals("1", StringComparison.Ordinal)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadVersion(JToken token, out Version version)
    {
        version = null;
        string text = ((string)token ?? string.Empty).Trim();
        if (text.Length == 0) return false;
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
        if (!Version.TryParse(text, out Version parsed)) return false;
        // Version.TryParse leaves unwritten components at -1, which compares below 0 and
        // would make "3.0.7" look older than "3.0.7.0". Normalise to four components.
        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
    }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0} ({1} bytes, clientUpdate={2})", Version, Size, ClientUpdate);
}
