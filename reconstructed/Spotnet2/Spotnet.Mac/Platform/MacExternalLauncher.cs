using System;
using System.Diagnostics;
using System.IO;
using NLog;
using Spotnet.Helpers;
using Spotnet.Platform;

namespace Spotnet.Mac.Platform;

/// <summary>
/// macOS implementation of IExternalLauncher using the native macOS 'open' utility.
/// </summary>
public sealed class MacExternalLauncher : IExternalLauncher
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public bool OpenUrl(string? url)
    {
        if (!WebLinkHelper.TryResolveWebLink(url, out string? target) || string.IsNullOrEmpty(target))
        {
            Log.Debug("Not launching URL as it is invalid or not http/https: {0}", url);
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = target,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to open URL in browser: {0}", target);
            return false;
        }
    }

    public bool OpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to open folder: {0}", folderPath);
            return false;
        }
    }

    public bool OpenFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to open file: {0}", filePath);
            return false;
        }
    }
}
