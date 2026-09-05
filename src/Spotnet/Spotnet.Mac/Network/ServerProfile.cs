using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NLog;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// What a news server is used for. Providers routinely give these different hostnames —
/// Eweka reads headers from textnews.eweka.nl, downloads from newsreader1.eweka.nl and
/// posts to upload.eweka.nl — which is why servers.xml carries a Type on every entry.
/// </summary>
public enum ServerRole
{
    Headers,
    Download,
    Upload,
}

/// <summary>
/// The news servers from servers.xml, one per role.
///
/// The file is the same one the Windows client reads and writes, so a profile copied
/// across works unchanged. Before this existed the macOS client took
/// <c>Root.Element("Server")</c> — whichever entry happened to come first — and used it
/// for everything, so a Windows profile listing Upload first would have had header
/// synchronisation talking to the posting server.
///
/// Passwords are never in the XML on macOS. Each server has its own keychain entry under
/// <c>Spotnet_{host}_{user}</c>, so a profile with three different hostnames keeps three
/// separate credentials.
/// </summary>
public sealed class ServerProfile
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<ServerRole, ServerInfo> _servers = new();

    private ServerProfile() { }

    /// <summary>True when at least one usable server is configured.</summary>
    public bool IsConfigured => _servers.Count > 0;

    /// <summary>The roles that servers.xml named explicitly.</summary>
    public IReadOnlyCollection<ServerRole> ConfiguredRoles => _servers.Keys;

    /// <summary>
    /// The server for a role, or the best stand-in when that role is not configured.
    ///
    /// Windows has no fallback: an unset role is simply empty and the feature using it
    /// does nothing. That is safe there because its setup always writes all three
    /// entries. A macOS profile written by this client before now holds a single
    /// Headers entry, so being equally strict would have left downloading with no
    /// server at all. Preference order is Download, Headers, Upload — the download
    /// server is a provider's general-purpose reader.
    /// </summary>
    public ServerInfo? Get(ServerRole role)
    {
        if (_servers.TryGetValue(role, out var exact)) return exact;

        foreach (var fallback in new[] { ServerRole.Download, ServerRole.Headers, ServerRole.Upload })
        {
            if (_servers.TryGetValue(fallback, out var candidate))
            {
                Log.Debug("No {0} server configured; using the {1} server instead.", role, fallback);
                return candidate;
            }
        }
        return null;
    }

    public static ServerProfile Load(IAppPaths appPaths, ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(secretStore);

        string path = Path.Combine(appPaths.DataFolder, "servers.xml");
        var profile = new ServerProfile();
        if (!File.Exists(path)) return profile;

        try
        {
            var root = XDocument.Load(path).Root;
            if (root == null) return profile;

            foreach (var element in root.Elements("Server"))
            {
                var role = ParseRole((string?)element.Attribute("Type"));
                if (role == null) continue;

                string host = ((string?)element.Attribute("Server") ?? "").Trim();
                if (host.Length == 0) continue;

                string user = ((string?)element.Attribute("Username") ?? "").Trim();

                var server = new ServerInfo
                {
                    Server = host,
                    Port = int.TryParse((string?)element.Attribute("Port"), out int port) ? port : 563,
                    SSL = ((string?)element.Attribute("SSL") ?? "1") == "1",
                    Username = user,
                    Password = secretStore.GetSecret(SecretKey(host, user)) ?? "",
                    Connections = int.TryParse((string?)element.Attribute("Connections"), out int c) && c > 0 ? c : 2,
                };

                // Last entry wins, the way the Windows loader assigns its role slots.
                profile._servers[role.Value] = server;
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not read servers.xml: {0}", ex.Message);
        }

        return profile;
    }

    /// <summary>The keychain key for a server's password.</summary>
    public static string SecretKey(string host, string username) => $"Spotnet_{host}_{username}";

    /// <summary>
    /// Reads the Type attribute the way the Windows loader does: trimmed, case
    /// insensitive, singular or plural. An entry with no Type at all is treated as the
    /// headers server, which is what this client used to write.
    /// </summary>
    internal static ServerRole? ParseRole(string? type)
    {
        string value = (type ?? "").Trim().ToUpperInvariant();
        return value switch
        {
            "" or "HEADER" or "HEADERS" => ServerRole.Headers,
            "DOWNLOAD" or "DOWNLOADS" => ServerRole.Download,
            "UPLOAD" or "UPLOADS" => ServerRole.Upload,
            _ => null,
        };
    }

    /// <summary>The attribute value to write for a role, matching what Windows writes.</summary>
    internal static string RoleAttribute(ServerRole role) => role switch
    {
        ServerRole.Headers => "Headers",
        ServerRole.Download => "Downloads",
        ServerRole.Upload => "Uploads",
        _ => "Headers",
    };
}
