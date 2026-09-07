using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NLog;

namespace Spotnet.Remote;

public class PairedDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Telefoon";
    public string TokenHash { get; set; } = "";
    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = "";
}

/// <summary>
/// De instellingen van Spotnet Remote, opgeslagen als remote_config.json. Het
/// JSON-formaat is identiek aan dat van de Windows-client; op macOS bepaalt
/// <see cref="ConfigPathProvider"/> waar het bestand staat.
/// </summary>
public class RemoteConfig
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object Lock = new object();

    /// <summary>
    /// Levert de map waarin remote_config.json staat. Windows wijst die op
    /// AppHelper.SettingsFolder; macOS op zijn eigen instellingenmap. Statisch
    /// vervangbaar zodat de bestaande tests hun eigen map kunnen forceren.
    /// </summary>
    public static Func<string> ConfigPathProvider { get; set; } =
        () => Path.Combine(AppContext.BaseDirectory, "remote_config.json");

    private static string ConfigPath => ConfigPathProvider();

    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 8770;
    public bool AllowLan { get; set; } = true;
    public bool RequireAuth { get; set; } = true;
    public bool KeepAwake { get; set; } = false;
    // Retained only so older configuration files/clients remain compatible.
    public string AuthUsername { get; set; } = "admin";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public List<PairedDevice> PairedDevices { get; set; } = new List<PairedDevice>();

    public void SetPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            PasswordHash = "";
            PasswordSalt = "";
            return;
        }

        PasswordSecurity.HashPassword(password, out string hash, out string salt);
        PasswordHash = hash;
        PasswordSalt = salt;
    }

    public bool VerifyCredentials(string username, string password)
        => VerifyPassword(password);

    public bool VerifyPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (string.IsNullOrEmpty(PasswordHash) || string.IsNullOrEmpty(PasswordSalt))
        {
            return false;
        }

        return PasswordSecurity.VerifyPassword(password, PasswordHash, PasswordSalt);
    }

    public static RemoteConfig Load()
    {
        lock (Lock)
        {
            try
            {
                string path = ConfigPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<RemoteConfig>(json);
                    if (config != null) return config;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to load remote_config.json: {0}", ex.Message);
            }
            return new RemoteConfig();
        }
    }

    public void Save()
    {
        lock (Lock)
        {
            try
            {
                string path = ConfigPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save remote_config.json: {0}", ex.Message);
            }
        }
    }
}
