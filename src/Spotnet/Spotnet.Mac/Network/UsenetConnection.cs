using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Services;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// Opens an authenticated connection to the news server for a given role, reading the
/// configuration out of servers.xml and the password out of the keychain. Every service
/// that talks to Usenet needs the same handful of lines, so they share this one.
/// </summary>
public sealed class UsenetConnection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly UserPreferencesService _preferences;

    public UsenetConnection(IAppPaths appPaths, ISecretStore secretStore, UserPreferencesService? preferences = null)
    {
        _appPaths = appPaths;
        _secretStore = secretStore;
        _preferences = preferences ?? new UserPreferencesService(appPaths);
    }

    /// <summary>
    /// Returns a connected, authenticated client, or null when no server is set up.
    /// </summary>
    /// <param name="role">
    /// What the connection is for. Header synchronisation, spot bodies and comments use
    /// the headers server; binaries use the download server; posting a comment uses the
    /// upload server. A profile that names only one server uses it for everything.
    /// </param>
    public async Task<NntpClient?> OpenAsync(ServerRole role = ServerRole.Headers, CancellationToken cancellationToken = default)
    {
        var server = LoadServerConfig(role);
        if (server == null)
        {
            Log.Debug("No Usenet server configured for {0}.", role);
            return null;
        }

        var client = new NntpClient();
        try
        {
            await client.ConnectAsync(
                server.Server, server.Port, server.SSL,
                allowInvalidCertificate: _preferences.Current.AllowInvalidServerCertificate,
                proxy: ProxySettings.FromPreferences(_preferences.Current, _secretStore),
                cancellationToken: cancellationToken);

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

    /// <summary>The configured server for a role, or null when servers.xml holds none.</summary>
    public ServerInfo? LoadServerConfig(ServerRole role = ServerRole.Headers) =>
        ServerProfile.Load(_appPaths, _secretStore).Get(role);
}
