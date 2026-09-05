using System;
using Spotnet.Mac.Services;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// A SOCKS5 proxy to route news traffic through, as the Windows client's
/// <c>UseSocksProxy</c> setting does.
///
/// The password is never in preferences.json; like the news server credentials it lives
/// in the keychain, under <see cref="SecretKey"/>.
/// </summary>
public sealed record ProxySettings(string Host, int Port, string Username, string Password)
{
    /// <summary>The keychain key for the proxy password.</summary>
    public const string SecretKey = "Spotnet_Socks5Proxy";

    public bool IsUsable => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535;

    /// <summary>
    /// The configured proxy, or null when the user has not turned one on. Reading it
    /// returns null rather than an unusable record so a caller only has to null-check.
    /// </summary>
    public static ProxySettings? FromPreferences(UserPreferences preferences, ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(secretStore);

        return Create(preferences.UseSocksProxy, preferences.SocksProxyHost, preferences.SocksProxyPort,
                      preferences.SocksProxyUsername, secretStore.GetSecret(SecretKey) ?? "");
    }

    /// <summary>
    /// Builds a proxy from loose values. The settings dialog uses this to test the proxy
    /// as it currently stands on screen, before anything is saved.
    /// </summary>
    public static ProxySettings? Create(bool enabled, string? host, int port, string? username, string? password)
    {
        if (!enabled) return null;

        var settings = new ProxySettings((host ?? "").Trim(), port, (username ?? "").Trim(), password ?? "");
        return settings.IsUsable ? settings : null;
    }

    /// <summary>Never print the password; this type ends up in log lines.</summary>
    public override string ToString() =>
        Username.Length > 0 ? $"socks5://{Username}@{Host}:{Port}" : $"socks5://{Host}:{Port}";
}
