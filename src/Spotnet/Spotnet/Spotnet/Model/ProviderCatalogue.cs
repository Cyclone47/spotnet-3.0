using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spotnet.Model;

/// <summary>
/// Parses the provider list published at <see cref="ProviderCatalogueSource.Url"/>.
/// </summary>
/// <remarks>
/// This is a trust boundary, not a config reader. The list decides which servers the connect
/// dialog offers, and the user types their Usenet credentials into whatever they pick, so a
/// hostile entry means credentials sent to a hostile server. Everything here is therefore
/// allow-listed rather than sanitised, and a single bad field rejects the whole document: a
/// partially-applied list is how you end up with one attacker-controlled row among two dozen
/// real ones. Callers keep the built-in catalogue when this returns false.
/// </remarks>
internal static class ProviderCatalogue
{
    /// <summary>Bumped only for a breaking change; clients refuse anything they do not know.</summary>
    internal const int SupportedSchema = 1;

    internal const int MaxProviders = 200;

    /// <summary>The real file is a few KB. This bounds what a redirected or hostile URL can feed us.</summary>
    internal const int MaxBytes = 128 * 1024;

    private const int MaxNameLength = 60;
    private const int MaxHostLength = 253;

    /// <summary>Only the ports Usenet actually uses; anything else is a redirect to somewhere odd.</summary>
    private static readonly HashSet<int> AllowedPorts = new HashSet<int> { 563, 443, 119, 80 };

    private static readonly HashSet<string> AllowedGroups = new HashSet<string>(StringComparer.Ordinal)
        { UsenetProviders.Netherlands, UsenetProviders.International };

    /// <summary>A conservative DNS name: labels of letters, digits and hyphens, at least one dot.</summary>
    private static readonly Regex HostPattern = new Regex(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool TryParse(string json, out List<ProviderItem> providers, out string error)
    {
        providers = null;
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return Fail("The catalogue is empty.", out error);

            JObject root;
            try
            {
                // No type resolution, no date munging: this is data, and nothing in it names a type.
                root = JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 8
                });
            }
            catch (JsonException exception)
            {
                return Fail("The catalogue is not valid JSON: " + exception.Message, out error);
            }
            if (root == null) return Fail("The catalogue is not a JSON object.", out error);

            if (!(root["schema"] is JValue schema) || schema.Type != JTokenType.Integer)
                return Fail("The catalogue has no numeric schema.", out error);
            if ((int)schema != SupportedSchema)
                return Fail("Unsupported catalogue schema " + (int)schema + "; this build understands " + SupportedSchema + ".", out error);

            if (!(root["providers"] is JArray entries))
                return Fail("The catalogue has no providers array.", out error);
            if (entries.Count == 0) return Fail("The catalogue lists no providers.", out error);
            if (entries.Count > MaxProviders)
                return Fail("The catalogue lists " + entries.Count + " providers; the limit is " + MaxProviders + ".", out error);

            var result = new List<ProviderItem>(entries.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken entry in entries)
            {
                if (!(entry is JObject row)) return Fail("A provider entry is not an object.", out error);
                if (!TryReadProvider(row, out ProviderItem provider, out error)) return false;
                if (!names.Add(provider.Name)) return Fail("Duplicate provider name: " + provider.Name, out error);
                if (!headers.Add(provider.Headers)) return Fail("Duplicate headers server: " + provider.Headers, out error);
                result.Add(provider);
            }

            // The manual entry is the client's own; a published list must never supply or replace it.
            result.Add(UsenetProviders.ManualEntry());
            providers = result;
            return true;
        }
        catch (Exception exception)
        {
            // Never let a malformed catalogue take the dialog down; the built-in list is right there.
            return Fail("The catalogue could not be read: " + exception.Message, out error);
        }
    }

    private static bool TryReadProvider(JObject row, out ProviderItem provider, out string error)
    {
        provider = null;
        error = null;

        if (!TryReadString(row, "name", MaxNameLength, out string name, out error)) return false;
        if (!TryReadString(row, "group", 8, out string group, out error)) return false;
        if (!AllowedGroups.Contains(group))
            return Fail("Provider '" + name + "' has group '" + group + "'; expected NL or INT.", out error);

        if (!TryReadHost(row, "host", required: true, name, out string host, out error)) return false;
        if (!TryReadHost(row, "upload", required: false, name, out string upload, out error)) return false;
        if (!TryReadHost(row, "headers", required: false, name, out string headerHost, out error)) return false;

        if (!TryReadPort(row, "port", required: true, name, 0, out int port, out error)) return false;
        if (!TryReadPort(row, "uploadPort", required: false, name, port, out int uploadPort, out error)) return false;
        if (!TryReadPort(row, "headersPort", required: false, name, port, out int headersPort, out error)) return false;

        provider = new ProviderItem
        {
            Name = name,
            Group = group,
            Download = host,
            Upload = upload ?? host,
            Headers = headerHost ?? host,
            DownloadPort = port,
            UploadPort = uploadPort,
            HeadersPort = headersPort
        };
        return true;
    }

    private static bool TryReadString(JObject row, string field, int maxLength, out string value, out string error)
    {
        value = null;
        error = null;
        if (!(row[field] is JValue token) || token.Type != JTokenType.String)
            return Fail("A provider entry has no string '" + field + "'.", out error);
        value = ((string)token ?? string.Empty).Trim();
        if (value.Length == 0) return Fail("A provider entry has an empty '" + field + "'.", out error);
        if (value.Length > maxLength) return Fail("A provider '" + field + "' exceeds " + maxLength + " characters.", out error);
        foreach (char character in value)
        {
            // Control characters and the Unicode direction overrides used to disguise names.
            if (char.IsControl(character) || (character >= '‪' && character <= '‮') || character == '‏' || character == '‎')
                return Fail("A provider '" + field + "' contains a control character.", out error);
        }
        return true;
    }

    private static bool TryReadHost(JObject row, string field, bool required, string name, out string host, out string error)
    {
        host = null;
        error = null;
        if (row[field] == null || row[field].Type == JTokenType.Null)
        {
            if (!required) return true;
            return Fail("Provider '" + name + "' has no '" + field + "'.", out error);
        }
        if (!TryReadString(row, field, MaxHostLength, out string value, out error)) return false;
        // Compared and connected to in lower case, so normalise before validating.
        value = value.ToLowerInvariant();
        if (!HostPattern.IsMatch(value))
            return Fail("Provider '" + name + "' has an invalid " + field + " server: " + value, out error);
        host = value;
        return true;
    }

    private static bool TryReadPort(JObject row, string field, bool required, string name, int fallback, out int port, out string error)
    {
        port = fallback;
        error = null;
        if (row[field] == null || row[field].Type == JTokenType.Null)
        {
            if (!required) return true;
            return Fail("Provider '" + name + "' has no '" + field + "'.", out error);
        }
        if (!(row[field] is JValue token) || token.Type != JTokenType.Integer)
            return Fail("Provider '" + name + "' has a non-numeric " + field + ".", out error);
        long raw = (long)token;
        if (!AllowedPorts.Contains((int)Math.Min(raw, int.MaxValue)))
            return Fail("Provider '" + name + "' uses port " + raw.ToString(CultureInfo.InvariantCulture) + "; allowed: 563, 443, 119, 80.", out error);
        port = (int)raw;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
