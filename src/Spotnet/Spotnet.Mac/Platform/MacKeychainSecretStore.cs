using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Platform;

/// <summary>
/// Secure credential storage using the macOS Keychain Services via the native `security` tool,
/// with an in-memory fallback for unit testing and non-macOS environments.
/// </summary>
public sealed class MacKeychainSecretStore : ISecretStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string ServiceName = "Spotnet";
    private readonly InMemorySecretStore _fallbackStore = new();

    private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public void SetSecret(string key, string secret)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (!IsMac)
        {
            _fallbackStore.SetSecret(key, secret);
            return;
        }

        try
        {
            // First delete existing if any, then add (-U updates in newer macOS versions)
            RunSecurityCommand($"add-generic-password -a \"{EscapeArg(key)}\" -s \"{ServiceName}\" -w \"{EscapeArg(secret)}\" -U");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to store secret for key {0} in macOS Keychain. Using memory fallback.", key);
            _fallbackStore.SetSecret(key, secret);
        }
    }

    public string? GetSecret(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (!IsMac)
        {
            return _fallbackStore.GetSecret(key);
        }

        try
        {
            var output = RunSecurityCommand($"find-generic-password -a \"{EscapeArg(key)}\" -s \"{ServiceName}\" -w");
            if (!string.IsNullOrEmpty(output))
            {
                return output.TrimEnd('\r', '\n');
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Key {0} not found in macOS Keychain or failed: {1}", key, ex.Message);
        }

        return _fallbackStore.GetSecret(key);
    }

    public bool DeleteSecret(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        _fallbackStore.DeleteSecret(key);

        if (!IsMac) return true;

        try
        {
            RunSecurityCommand($"delete-generic-password -a \"{EscapeArg(key)}\" -s \"{ServiceName}\"");
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to delete key {0} from macOS Keychain: {1}", key, ex.Message);
            return false;
        }
    }

    private static string RunSecurityCommand(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch /usr/bin/security");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(3000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"security tool exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }
}
