using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NLog;
using Spotnet.Helpers;

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

public class RemoteConfig
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object Lock = new object();
    private static string ConfigPath => Path.Combine(AppHelper.SettingsFolder, "remote_config.json");

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
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
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
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save remote_config.json: {0}", ex.Message);
            }
        }
    }
}
