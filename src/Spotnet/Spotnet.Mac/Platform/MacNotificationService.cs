using System;
using System.Diagnostics;
using NLog;
using Spotnet.Mac.Services;

namespace Spotnet.Mac.Platform;

/// <summary>
/// Native macOS notification service that posts alerts to the Notification Center via osascript.
/// </summary>
public sealed class MacNotificationService : Spotnet.Notifications.ISpotnetNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly UserPreferencesService? _preferences;

    public MacNotificationService(UserPreferencesService? preferences = null)
    {
        _preferences = preferences;
    }

    public bool IsEnabled => _preferences?.Current.ShowDesktopNotifications ?? true;

    /// <summary>
    /// Displays a native macOS notification banner.
    /// </summary>
    public bool ShowNotification(string message, string title = "Spotnet", string? subtitle = null, bool force = false)
    {
        if (!force && !IsEnabled)
        {
            Log.Debug("Notification suppressed by user preferences: {0}", message);
            return false;
        }

        try
        {
            string escapedMsg = EscapeAppleScriptString(message);
            string escapedTitle = EscapeAppleScriptString(title);
            string script;

            if (!string.IsNullOrEmpty(subtitle))
            {
                string escapedSub = EscapeAppleScriptString(subtitle);
                script = $"display notification \"{escapedMsg}\" with title \"{escapedTitle}\" subtitle \"{escapedSub}\"";
            }
            else
            {
                script = $"display notification \"{escapedMsg}\" with title \"{escapedTitle}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", script },
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to display macOS notification: {0}", message);
            return false;
        }
    }

    /// <summary>
    /// The shared notification engine's desktop toast. Same preference gate as
    /// the other notifications on this platform; the engine's own checkbox
    /// (meldingcentrum) gates it one level up.
    /// </summary>
    public bool Show(string title, string body) => ShowNotification(body, title: title);

    public void NotifyDownloadFinished(string spotTitle, bool success, string? detail = null)
    {
        if (success)
        {
            ShowNotification(spotTitle, title: "Spotnet", subtitle: "Download voltooid");
        }
        else
        {
            string msg = string.IsNullOrEmpty(detail) ? spotTitle : $"{spotTitle} ({detail})";
            ShowNotification(msg, title: "Spotnet", subtitle: "Download voltooid met fouten");
        }
    }

    public void NotifyDatabaseRepairFinished(bool success, string detail)
    {
        string sub = success ? "Database hersteld" : "Database herstel mislukt";
        ShowNotification(detail, title: "Spotnet", subtitle: sub);
    }

    private static string EscapeAppleScriptString(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }
}
