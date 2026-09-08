using System;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Helpers;

public static class VersionHelper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Determines whether the release notes tab should be shown on startup.
    /// Returns true if this is an update (current version is newer than the last recorded version)
    /// or if no previous version was recorded yet.
    /// </summary>
    public static bool ShouldShowReleaseNotesOnStartup(Version currentVersion, string lastSeenVersionStr)
    {
        if (currentVersion == null) return false;

        if (string.IsNullOrWhiteSpace(lastSeenVersionStr))
        {
            return true;
        }

        if (Version.TryParse(lastSeenVersionStr.Trim(), out Version previousVersion))
        {
            return currentVersion > previousVersion;
        }

        return true;
    }

    /// <summary>
    /// Checks if the running version represents a new version or update. If so, updates
    /// Settings.Default.LastSeenVersion to the current version, saves settings, and returns true.
    /// </summary>
    public static bool CheckAndAcknowledgeUpdate()
    {
        try
        {
            Version current = AppHelper.AppVersion;
            string lastSeen = Settings.Default.LastSeenVersion;

            if (ShouldShowReleaseNotesOnStartup(current, lastSeen))
            {
                Settings.Default.LastSeenVersion = current.ToString();
                Settings.Default.IsNewVersion = false;
                Settings.Default.Save();
                Log.Info("Update detected: current version {0} is newer than last seen version '{1}'. Opening release notes tab.", current, lastSeen);
                return true;
            }
            else if (string.IsNullOrWhiteSpace(lastSeen))
            {
                Settings.Default.LastSeenVersion = current.ToString();
                Settings.Default.Save();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to check version for release notes update: {0}", ex.Message);
        }

        return false;
    }
}
