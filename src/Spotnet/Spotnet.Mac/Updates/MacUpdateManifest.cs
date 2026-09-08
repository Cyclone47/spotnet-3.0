using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spotnet.Mac.Updates;

public enum MacUpdateAction
{
    None,
    Offer,
    Required
}

public sealed class MacUpdateManifest
{
    public const int SupportedSchema = 1;

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    private MacUpdateManifest() { }

    public int Schema { get; private init; }
    public bool ClientUpdate { get; private init; }
    public Version Version { get; private init; } = new(0, 0, 0, 0);
    public Version MinimumVersion { get; private init; } = new(0, 0, 0, 0);
    public bool Forced { get; private init; }
    public Uri? Url { get; private init; }
    public long Size { get; private init; }
    public string Sha256 { get; private init; } = "";
    public Uri? ReleaseNotesUrl { get; private init; }

    /// <summary>True when the manifest describes an installable macOS app bundle.</summary>
    public bool HasMacAsset => Url != null && Size > 0 && Sha256.Length == 64;

    public static bool TryParse(string json, out MacUpdateManifest? manifest, out string error)
    {
        manifest = null;
        error = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Het updatebestand is leeg.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json.TrimStart('\uFEFF', '\u200B').TrimStart());
            JsonElement root = document.RootElement;
            int schema = ReadInt(root, "schema");
            if (schema <= 0 || schema > SupportedSchema)
            {
                error = $"Onbekend update-schema ({schema}).";
                return false;
            }

            if (!TryReadVersion(root, "version", out Version version))
            {
                error = "De update heeft geen geldige versie.";
                return false;
            }

            Version minimum = TryReadVersion(root, "minimumVersion", out Version parsedMinimum)
                ? parsedMinimum
                : new Version(0, 0, 0, 0);

            // Windows and macOS share the base manifest. macUrl is optional so an old
            // Windows-only manifest remains valid but is never mistaken for a Mac asset.
            string? macUrl = ReadString(root, "macUrl");
            long macSize = ReadLong(root, "macSize");
            string macSha = (ReadString(root, "macSha256") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(macUrl))
            {
                string? genericUrl = ReadString(root, "url");
                if (IsMacAssetUrl(genericUrl))
                {
                    macUrl = genericUrl;
                    macSize = ReadLong(root, "size");
                    macSha = (ReadString(root, "sha256") ?? "").Trim();
                }
            }

            Uri? assetUrl = null;
            if (Uri.TryCreate(macUrl, UriKind.Absolute, out Uri? parsedUrl)
                && IsTrustedUrl(parsedUrl)
                && Sha256Pattern.IsMatch(macSha)
                && macSize > 0)
            {
                assetUrl = parsedUrl;
            }

            Uri.TryCreate(ReadString(root, "releaseNotesUrl"), UriKind.Absolute, out Uri? notesUrl);
            manifest = new MacUpdateManifest
            {
                Schema = schema,
                ClientUpdate = ReadFlag(root, "clientUpdate"),
                Version = version,
                MinimumVersion = minimum,
                Forced = ReadFlag(root, "forced"),
                Url = assetUrl,
                Size = assetUrl == null ? 0 : macSize,
                Sha256 = assetUrl == null ? "" : macSha.ToLowerInvariant(),
                ReleaseNotesUrl = notesUrl
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = "Het updatebestand is geen geldige JSON: " + ex.Message;
            return false;
        }
    }

    public static bool IsMacAssetUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
        string path = uri.AbsolutePath;
        return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && path.Contains("mac", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrustedUrl(Uri uri)
    {
        if (uri.IsLoopback && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return true;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private static long ReadLong(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result) ? result : 0L;

    private static bool ReadFlag(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number != 0;
        if (value.ValueKind != JsonValueKind.String) return false;
        string text = value.GetString()?.Trim() ?? "";
        return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadVersion(JsonElement root, string name, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        string text = ReadString(root, name)?.Trim() ?? "";
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        if (!Version.TryParse(text, out Version? parsed)) return false;
        version = new Version(Math.Max(0, parsed.Major), Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}

public readonly record struct MacUpdateDecision(MacUpdateAction Action, string Reason)
{
    public bool ShouldPrompt => Action != MacUpdateAction.None;
}

public static class MacUpdatePolicy
{
    public static MacUpdateDecision Evaluate(MacUpdateManifest? manifest, Version current, string? skippedVersion = null)
    {
        if (manifest == null) return new(MacUpdateAction.None, "Geen updatebestand.");
        if (!manifest.ClientUpdate) return new(MacUpdateAction.None, "De update is nog niet vrijgegeven.");
        if (!manifest.HasMacAsset) return new(MacUpdateAction.None, "Geen macOS-installatiebestand in het updatebestand.");
        if (manifest.Version <= current) return new(MacUpdateAction.None, "De geïnstalleerde versie is actueel.");
        if (manifest.Forced || current < manifest.MinimumVersion)
            return new(MacUpdateAction.Required, $"Spotnet {manifest.Version} is vereist.");
        if (!string.IsNullOrWhiteSpace(skippedVersion)
            && Version.TryParse(skippedVersion.Trim(), out Version? skipped)
            && skipped >= manifest.Version)
            return new(MacUpdateAction.None, "Deze versie is overgeslagen.");
        return new(MacUpdateAction.Offer, $"Spotnet {manifest.Version} is beschikbaar.");
    }
}
