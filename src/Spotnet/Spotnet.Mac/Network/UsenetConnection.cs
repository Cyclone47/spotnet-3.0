using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// Reads the configured news server out of servers.xml (password from the keychain) and
/// opens an authenticated connection. Every service that talks to Usenet needs the same
/// four lines, so they share this one.
/// </summary>
public sealed class UsenetConnection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;

    public UsenetConnection(IAppPaths appPaths, ISecretStore secretStore)
    {
        _appPaths = appPaths;
        _secretStore = secretStore;
    }

    /// <summary>Returns a connected, authenticated client, or null when no server is set up.</summary>
    public async Task<NntpClient?> OpenAsync(CancellationToken cancellationToken = default)
    {
        var server = LoadServerConfig();
        if (server == null)
        {
            Log.Debug("No Usenet server configured.");
            return null;
        }

        var client = new NntpClient();
        try
        {
            await client.ConnectAsync(server.Server, server.Port, server.SSL, cancellationToken);
            if (!string.IsNullOrEmpty(server.Username))
            {
                await client.AuthenticateAsync(server.Username, server.Password, cancellationToken);
            }
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public ServerInfo? LoadServerConfig()
    {
        string path = Path.Combine(_appPaths.DataFolder, "servers.xml");
        if (!File.Exists(path)) return null;

        try
        {
            var serverEl = XDocument.Load(path).Root?.Element("Server");
            if (serverEl == null) return null;

            string host = serverEl.Attribute("Server")?.Value ?? "";
            string user = serverEl.Attribute("Username")?.Value ?? "";

            return new ServerInfo
            {
                Server = host,
                Port = int.TryParse(serverEl.Attribute("Port")?.Value, out var p) ? p : 563,
                SSL = (serverEl.Attribute("SSL")?.Value ?? "1") == "1",
                Username = user,
                Password = _secretStore.GetSecret($"Spotnet_{host}_{user}") ?? ""
            };
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not read servers.xml: {0}", ex.Message);
            return null;
        }
    }
}
