using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Mac.Platform;

/// <summary>
/// What to do with the machine when the last download has finished. Port of
/// Windows' OperatingSystemHelper.ShutdownComputerNow plus the ShutdownComputerDialog
/// countdown: a 60-second window with "afsluiten annuleren" and "NU AFSLUITEN".
/// </summary>
public static class MacPowerActions
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>How long the confirmation dialog waits before acting. Windows: 60 s.</summary>
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Shuts the Mac down now: `osascript -e 'tell app "System Events" to shut down'`,
    /// the macOS counterpart of Windows' `shutdown /p /f`. Goes through System Events,
    /// which asks nothing further when the user has granted automation rights.
    /// </summary>
    public static bool ShutdownNow()
    {
        Log.Info("Shutdown Mac");
        return RunAppleScript("tell application \"System Events\" to shut down");
    }

    /// <summary>
    /// Puts the Mac to sleep now, for the slaapstand variant of the setting.
    /// </summary>
    public static bool SleepNow()
    {
        Log.Info("Sleep Mac");
        return RunAppleScript("tell application \"System Events\" to sleep");
    }

    private static bool RunAppleScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", script },
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            return process != null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AppleScript power action failed: {0}", ex.Message);
            return false;
        }
    }
}
