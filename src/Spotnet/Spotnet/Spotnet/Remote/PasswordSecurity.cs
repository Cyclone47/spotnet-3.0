using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Remote;

public static class PasswordSecurity
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public const int SaltByteSize = 16;
    public const int HashByteSize = 32;
    public const int Iterations = 100_000;

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);
    public const int ThrottleDelayMs = 1500;

    private class RateLimitRecord
    {
        public int FailedCount { get; set; }
        public DateTime FirstFailedUtc { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
    }

    private static readonly ConcurrentDictionary<string, RateLimitRecord> RateLimits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hashes a password using PBKDF2-HMAC-SHA256 with 100,000 iterations and a 16-byte random salt.
    /// </summary>
    public static void HashPassword(string password, out string hashHex, out string saltHex)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashByteSize
        );

        hashHex = Convert.ToHexString(hash);
        saltHex = Convert.ToHexString(salt);
    }

    /// <summary>
    /// Verifies a password against the stored hex-encoded hash and salt using constant-time comparison.
    /// </summary>
    public static bool VerifyPassword(string password, string storedHashHex, string storedSaltHex)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHashHex) || string.IsNullOrEmpty(storedSaltHex))
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromHexString(storedSaltHex);
            byte[] expectedHash = Convert.FromHexString(storedHashHex);

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashByteSize
            );

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (Exception ex)
        {
            Log.Warn("Password verification failed with exception: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Checks whether the specified IP is currently locked out due to excessive failed attempts.
    /// </summary>
    public static bool IsIpLockedOut(string ip, out TimeSpan lockRemaining)
    {
        lockRemaining = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(ip)) return false;

        if (RateLimits.TryGetValue(ip, out var record))
        {
            if (record.LockedUntilUtc.HasValue)
            {
                var remaining = record.LockedUntilUtc.Value - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    lockRemaining = remaining;
                    return true;
                }

                // Lockout expired, clear record
                RateLimits.TryRemove(ip, out _);
            }
        }

        return false;
    }

    /// <summary>
    /// Records a failed authentication attempt for an IP. Applies throttling delay and locks out if threshold reached.
    /// </summary>
    public static async Task<(bool isLockedOut, TimeSpan lockRemaining)> RecordFailedAttemptAsync(string ip, bool applyDelay = true)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return (false, TimeSpan.Zero);
        }

        if (applyDelay)
        {
            // Apply artificial throttling delay to hinder automated brute forcing
            await Task.Delay(ThrottleDelayMs);
        }

        DateTime now = DateTime.UtcNow;
        var record = RateLimits.AddOrUpdate(ip,
            _ => new RateLimitRecord
            {
                FailedCount = 1,
                FirstFailedUtc = now,
                LockedUntilUtc = null
            },
            (_, existing) =>
            {
                if (existing.LockedUntilUtc.HasValue && existing.LockedUntilUtc.Value > now)
                {
                    return existing;
                }

                // Reset counter if outside window
                if (now - existing.FirstFailedUtc > AttemptWindow)
                {
                    existing.FailedCount = 1;
                    existing.FirstFailedUtc = now;
                    existing.LockedUntilUtc = null;
                }
                else
                {
                    existing.FailedCount++;
                }

                if (existing.FailedCount >= MaxFailedAttempts)
                {
                    existing.LockedUntilUtc = now.Add(LockoutDuration);
                    Log.Warn("IP {0} locked out until {1} due to {2} consecutive failed attempts.", ip, existing.LockedUntilUtc, existing.FailedCount);
                }

                return existing;
            });

        if (record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value > now)
        {
            return (true, record.LockedUntilUtc.Value - now);
        }

        return (false, TimeSpan.Zero);
    }

    /// <summary>
    /// Resets the rate limiting counter for an IP upon successful login.
    /// </summary>
    public static void ResetAttempts(string ip)
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            RateLimits.TryRemove(ip, out _);
        }
    }
}
