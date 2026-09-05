using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Spotnet.Remote;

public class PendingPairing
{
    public string Pin { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public class RemoteAuthManager
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<RemoteAuthManager> InstanceHolder = new Lazy<RemoteAuthManager>(() => new RemoteAuthManager());
    public static RemoteAuthManager Instance => InstanceHolder.Value;

    private readonly ConcurrentDictionary<string, PendingPairing> _pendingPairings = new ConcurrentDictionary<string, PendingPairing>();
    private RemoteConfig _config = RemoteConfig.Load();

    public RemoteConfig Config
    {
        get => _config;
        set => _config = value;
    }

    public void ReloadConfig()
    {
        _config = RemoteConfig.Load();
    }

    public PendingPairing CreatePairingSession()
    {
        // Clean up expired pairings
        DateTime now = DateTime.UtcNow;
        foreach (var kvp in _pendingPairings.Where(p => p.Value.ExpiresAt < now).ToList())
        {
            _pendingPairings.TryRemove(kvp.Key, out _);
        }

        // Generate 6-digit PIN (100000 - 999999)
        int pinNum = RandomNumberGenerator.GetInt32(100000, 1000000);
        string pin = pinNum.ToString();
        string token = Guid.NewGuid().ToString("N");

        var pairing = new PendingPairing
        {
            Pin = pin,
            Token = token,
            ExpiresAt = now.AddMinutes(5)
        };

        _pendingPairings[pin] = pairing;
        _pendingPairings[token] = pairing;

        Log.Info("Created pairing session PIN={0} (valid 5 min)", pin);
        return pairing;
    }

    public async System.Threading.Tasks.Task<LoginResponseDto> TryLoginAsync(LoginRequestDto request, string clientIp)
    {
        if (request == null || string.IsNullOrEmpty(request.Password))
        {
            return new LoginResponseDto { Success = false, ErrorMessage = "Vul je wachtwoord in." };
        }

        if (PasswordSecurity.IsIpLockedOut(clientIp, out TimeSpan lockRemaining))
        {
            int mins = Math.Max(1, (int)Math.Ceiling(lockRemaining.TotalMinutes));
            return new LoginResponseDto
            {
                Success = false,
                ErrorMessage = $"Te veel mislukte inlogpogingen. Dit IP-adres is tijdelijk geblokkeerd. Probeer het over {mins} minuten opnieuw."
            };
        }

        // If auth is not required, or credentials match
        bool credentialsOk = !_config.RequireAuth || _config.VerifyPassword(request.Password);
        if (!credentialsOk)
        {
            var (isLocked, remaining) = await PasswordSecurity.RecordFailedAttemptAsync(clientIp);
            if (isLocked)
            {
                int mins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                return new LoginResponseDto
                {
                    Success = false,
                    ErrorMessage = $"Te veel mislukte inlogpogingen. Dit IP-adres is geblokkeerd voor {mins} minuten."
                };
            }

            return new LoginResponseDto
            {
                Success = false,
                ErrorMessage = "Onjuist wachtwoord."
            };
        }

        // Login successful, reset rate limit
        PasswordSecurity.ResetAttempts(clientIp);

        // Generate 256-bit device token
        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        string deviceToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        string tokenHash = ComputeHash(deviceToken);

        string deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "Mobiel Apparaat" : request.DeviceName.Trim();
        string user = string.IsNullOrWhiteSpace(_config.AuthUsername) ? "admin" : _config.AuthUsername.Trim();
        string displayName = deviceName;

        var device = new PairedDevice
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = displayName,
            TokenHash = tokenHash,
            PairedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IpAddress = clientIp ?? ""
        };

        _config.PairedDevices.Add(device);
        _config.Save();

        Log.Info("Remote login from device '{0}' ({1})", device.Name, device.IpAddress);

        return new LoginResponseDto
        {
            Success = true,
            DeviceId = device.Id,
            DeviceToken = deviceToken,
            Username = user
        };
    }

    public PairResponseDto TryPair(PairRequestDto request, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(request.Pin) && string.IsNullOrWhiteSpace(request.Token))
        {
            return new PairResponseDto { Success = false, ErrorMessage = "Geen koppelcode of token opgegeven." };
        }

        PendingPairing pairing = null;
        if (!string.IsNullOrWhiteSpace(request.Token) && _pendingPairings.TryGetValue(request.Token.Trim(), out var pByToken))
        {
            pairing = pByToken;
        }
        else if (!string.IsNullOrWhiteSpace(request.Pin))
        {
            string cleanPin = request.Pin.Trim().Replace("-", "").Replace(" ", "");
            if (_pendingPairings.TryGetValue(cleanPin, out var pByPin))
            {
                pairing = pByPin;
            }
        }

        if (pairing == null || pairing.ExpiresAt < DateTime.UtcNow)
        {
            return new PairResponseDto { Success = false, ErrorMessage = "Koppelcode is onjuist of verlopen." };
        }

        // Keep pairing session valid for its 5-minute lifespan so multiple touches or re-scans work seamlessly
        // (expired sessions are automatically purged in CreatePairingSession)

        // Generate device session token
        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        string deviceToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        string tokenHash = ComputeHash(deviceToken);

        var device = new PairedDevice
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(request.DeviceName) ? "Mobiel Apparaat" : request.DeviceName.Trim(),
            TokenHash = tokenHash,
            PairedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IpAddress = clientIp ?? ""
        };

        _config.PairedDevices.Add(device);
        _config.Save();

        Log.Info("Successfully paired device '{0}' ({1})", device.Name, device.IpAddress);

        return new PairResponseDto
        {
            Success = true,
            DeviceId = device.Id,
            DeviceToken = deviceToken
        };
    }

    public bool ValidateToken(string rawToken, string clientIp, out PairedDevice matchedDevice)
    {
        matchedDevice = null;
        if (!_config.RequireAuth)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        string tokenHash = ComputeHash(rawToken.Trim());
        var device = _config.PairedDevices.FirstOrDefault(d => d.TokenHash.Equals(tokenHash, StringComparison.OrdinalIgnoreCase));
        if (device != null)
        {
            device.LastSeenAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(clientIp))
            {
                device.IpAddress = clientIp;
            }
            matchedDevice = device;
            return true;
        }

        return false;
    }

    public bool RevokeDevice(string deviceId)
    {
        var device = _config.PairedDevices.FirstOrDefault(d => d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        if (device != null)
        {
            _config.PairedDevices.Remove(device);
            _config.Save();
            Log.Info("Revoked access for device '{0}'", device.Name);
            return true;
        }
        return false;
    }

    public void RevokeAllDevices()
    {
        _config.PairedDevices.Clear();
        _config.Save();
        Log.Info("Revoked access for all paired devices.");
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
