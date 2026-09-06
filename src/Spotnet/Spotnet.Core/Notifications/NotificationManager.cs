using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Notifications;

/// <summary>
/// The notification engine: rules over the spots database, bundled unread
/// notifications, persistence in notifications_config.json. Platform-neutral;
/// both clients construct it with their own database adapter and desktop
/// notifier. The periodic timer is the callers' responsibility (they own their
/// own sync timers); the engine exposes <see cref="EvaluateRulesAsync"/> for it.
/// </summary>
/// <summary>
/// Host hook for the auto-sync preferences: <paramref name="intervalMinutes"/> is the
/// interval to store, <paramref name="force"/> distinguishes the user's explicit choice
/// (always write) from the direct-rule minimum (only raise a too-low interval).
/// </summary>
public delegate void AutoUpdateSettingsHandler(int intervalMinutes, bool force);

public class NotificationManager
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new object();
    private readonly string _configPath;
    private readonly INotificationSpotQuery _spotQuery;
    private readonly ISpotnetNotifier? _notifier;

    private NotificationConfig _config;
    private bool _initialized;

    public event Action? UnreadCountChanged;
    public event Action? NotificationsUpdated;
    public event Action? RulesUpdated;

    public NotificationConfig Config
    {
        get
        {
            lock (_lock) return _config;
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_lock)
            {
                return _config?.Notifications?.Count(n => !n.IsRead) ?? 0;
            }
        }
    }

    public NotificationManager(INotificationSpotQuery spotQuery, ISpotnetNotifier? notifier, string settingsFolder)
    {
        _spotQuery = spotQuery ?? throw new ArgumentNullException(nameof(spotQuery));
        _notifier = notifier;
        _configPath = Path.Combine(settingsFolder ?? "", "notifications_config.json");
        _config = LoadConfig();
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;
            SyncAutoUpdateSettings();
            Log.Info("NotificationManager initialized with {0} rules and {1} notifications.", _config.Rules.Count, _config.Notifications.Count);
        }
    }

    /// <summary>
    /// Called by the host after a spots sync finished, so "Direct bij elke sync"
    /// rules evaluate right away. Safe to call from any thread.
    /// </summary>
    public void OnSyncFinished()
    {
        Task.Run(async () =>
        {
            try
            {
                await EvaluateRulesAsync(onlyDirect: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("Error in OnSyncFinished notification evaluation: {0}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Called by the host when its periodic evaluation timer fires (Windows: every
    /// 60 s). Evaluates the non-direct rules whose interval has elapsed.
    /// </summary>
    public void OnPeriodicTimer()
    {
        Task.Run(async () =>
        {
            try
            {
                await EvaluateRulesAsync(onlyDirect: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("Error evaluating notification rules: {0}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Aligns the host's auto-sync with the rules: any enabled direct rule forces
    /// auto-sync on and its interval to at least the configured minimum.
    /// </summary>
    public void SyncAutoUpdateSettings()
    {
        lock (_lock)
        {
            bool hasDirectRules = _config.Rules.Any(r => r.Enabled && r.IsDirectOnSync);
            SyncAutoUpdateSettingsCore(hasDirectRules);
        }
    }

    /// <summary>Host hook: apply the computed auto-sync settings to the app preferences.
    /// True from <see cref="SetAutoSyncInterval"/> (the user picked the interval — write
    /// it unconditionally), false from the direct-rule check (only raise the host's
    /// interval to the minimum when it is lower).</summary>
    public AutoUpdateSettingsHandler? AutoUpdateSettingsApplier { get; set; }

    private void SyncAutoUpdateSettingsCore(bool hasDirectRules)
    {
        if (hasDirectRules && AutoUpdateSettingsApplier != null)
        {
            // The applier enforces the minimum itself (5 minutes) and restarts the
            // host's auto-sync timer, like Windows' DbUpdateTimerStart does.
            AutoUpdateSettingsApplier(Math.Max(5, _config.AutoSyncIntervalMinutes), force: false);
        }
    }

    /// <summary>
    /// Toggles the desktop toast and persists it — the checkbox in the
    /// notification-center window.
    /// </summary>
    public void SetDesktopNotificationsEnabled(bool enabled)
    {
        lock (_lock)
        {
            _config.WindowsNotificationsEnabled = enabled;
            SaveConfig();
        }
    }

    public void SetAutoSyncInterval(int minutes)
    {
        lock (_lock)
        {
            if (minutes < 5) minutes = 5; // Minimum 5 minuten
            _config.AutoSyncIntervalMinutes = minutes;
            AutoUpdateSettingsApplier?.Invoke(minutes, force: true);
            SaveConfig();
        }
    }

    public void AddOrUpdateRule(NotificationRule rule)
    {
        if (rule == null) return;
        lock (_lock)
        {
            // If new rule, initialize LastCheckedRowId to current max rowid to avoid alerting on all past spots
            if (rule.LastCheckedRowId <= 0)
            {
                rule.LastCheckedRowId = _spotQuery.GetMaxSpotRowIdAsync().GetAwaiter().GetResult();
                rule.LastCheckedUtc = DateTime.UtcNow;
            }

            int idx = _config.Rules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0)
            {
                _config.Rules[idx] = rule;
            }
            else
            {
                _config.Rules.Add(rule);
            }

            SyncAutoUpdateSettings();
            SaveConfig();
            RulesUpdated?.Invoke();
        }
    }

    public void DeleteRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;
        lock (_lock)
        {
            _config.Rules.RemoveAll(r => r.Id == ruleId);
            SaveConfig();
            RulesUpdated?.Invoke();
        }
    }

    public void ToggleRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;
        lock (_lock)
        {
            var rule = _config.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.Enabled = !rule.Enabled;
                SyncAutoUpdateSettings();
                SaveConfig();
                RulesUpdated?.Invoke();
            }
        }
    }

    public void MarkAsRead(string notificationId)
    {
        if (string.IsNullOrEmpty(notificationId)) return;
        lock (_lock)
        {
            var notif = _config.Notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notif != null && !notif.IsRead)
            {
                notif.IsRead = true;
                SaveConfig();
                UnreadCountChanged?.Invoke();
                NotificationsUpdated?.Invoke();
            }
        }
    }

    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            bool changed = false;
            foreach (var n in _config.Notifications)
            {
                if (!n.IsRead)
                {
                    n.IsRead = true;
                    changed = true;
                }
            }
            if (changed)
            {
                SaveConfig();
                UnreadCountChanged?.Invoke();
                NotificationsUpdated?.Invoke();
            }
        }
    }

    public void DeleteNotification(string notificationId)
    {
        if (string.IsNullOrEmpty(notificationId)) return;
        lock (_lock)
        {
            _config.Notifications.RemoveAll(n => n.Id == notificationId);
            SaveConfig();
            UnreadCountChanged?.Invoke();
            NotificationsUpdated?.Invoke();
        }
    }

    public void ClearAllNotifications()
    {
        lock (_lock)
        {
            _config.Notifications.Clear();
            SaveConfig();
            UnreadCountChanged?.Invoke();
            NotificationsUpdated?.Invoke();
        }
    }

    public void AddNotification(SpotNotificationItem item)
    {
        if (item == null) return;
        lock (_lock)
        {
            _config.Notifications.Insert(0, item);
            if (_config.Notifications.Count > 100)
            {
                _config.Notifications.RemoveRange(100, _config.Notifications.Count - 100);
            }
            SaveConfig();
        }
        UnreadCountChanged?.Invoke();
        NotificationsUpdated?.Invoke();
    }

    /// <summary>
    /// Records a finished download as a notification (the Windows client does this
    /// from DisplayTooltip), plus a desktop toast when enabled.
    /// </summary>
    public void NotifyDownloadComplete(string spotTitle, bool success = true)
    {
        try
        {
            var item = new SpotNotificationItem
            {
                Id = Guid.NewGuid().ToString("N"),
                RuleId = "download",
                RuleName = "Downloads",
                RuleType = NotificationRuleType.Download,
                Title = success ? "Download voltooid" : "Download mislukt",
                Body = spotTitle ?? "",
                SpotCount = 1,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                Spots = new List<SpotSummaryItem>
                {
                    new SpotSummaryItem
                    {
                        Title = spotTitle ?? "",
                        FormattedDate = "Zojuist"
                    }
                }
            };
            AddNotification(item);
            Log.Info("Recorded download notification in NotificationManager: {0} ({1})", item.Title, spotTitle);
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to record download notification: {0}", ex.Message);
        }
    }

    public async Task EvaluateRulesAsync(bool onlyDirect = false, string? specificRuleId = null, bool isManualTest = false)
    {
        List<NotificationRule> rulesToEvaluate;
        lock (_lock)
        {
            if (specificRuleId != null)
            {
                rulesToEvaluate = _config.Rules.Where(r => r.Id == specificRuleId).ToList();
            }
            else
            {
                rulesToEvaluate = _config.Rules.Where(r => r.Enabled).ToList();
                if (onlyDirect)
                {
                    rulesToEvaluate = rulesToEvaluate.Where(r => r.IsDirectOnSync).ToList();
                }
                else
                {
                    rulesToEvaluate = rulesToEvaluate.Where(r =>
                        !r.IsDirectOnSync && (DateTime.UtcNow - r.LastCheckedUtc).TotalMinutes >= r.CheckIntervalMinutes
                    ).ToList();
                }
            }
        }

        if (rulesToEvaluate.Count == 0) return;

        foreach (var rule in rulesToEvaluate)
        {
            try
            {
                await EvaluateSingleRuleAsync(rule, isManualTest).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Error evaluating rule '{0}': {1}", rule.Name, ex.Message);
            }
        }
    }

    public async Task<SpotNotificationItem?> TestRuleNowAsync(string ruleId)
    {
        NotificationRule? rule;
        lock (_lock)
        {
            rule = _config.Rules.FirstOrDefault(r => r.Id == ruleId);
        }
        if (rule == null) return null;

        return await EvaluateSingleRuleAsync(rule, isManualTest: true).ConfigureAwait(false);
    }

    private async Task<SpotNotificationItem?> EvaluateSingleRuleAsync(NotificationRule rule, bool isManualTest = false)
    {
        long sinceRowId = isManualTest ? Math.Max(0, rule.LastCheckedRowId - 50) : rule.LastCheckedRowId;

        // If rule has never been run and this is not a manual test, initialize rowid to max
        if (sinceRowId <= 0 && !isManualTest)
        {
            rule.LastCheckedRowId = await _spotQuery.GetMaxSpotRowIdAsync().ConfigureAwait(false);
            rule.LastCheckedUtc = DateTime.UtcNow;
            lock (_lock) SaveConfig();
            return null;
        }

        var matchingSpots = await _spotQuery.QuerySpotsForRuleAsync(rule, sinceRowId, limit: isManualTest ? 5 : 50).ConfigureAwait(false);

        rule.LastCheckedUtc = DateTime.UtcNow;
        if (matchingSpots.Count > 0 && !isManualTest)
        {
            rule.LastCheckedRowId = Math.Max(rule.LastCheckedRowId, matchingSpots.Max(s => s.Id));
        }

        if (matchingSpots.Count == 0)
        {
            lock (_lock) SaveConfig();
            return null;
        }

        // Bundle results into notification
        string title;
        string body;

        if (rule.Type == NotificationRuleType.Filter)
        {
            string filterLabel = string.IsNullOrWhiteSpace(rule.FilterName) ? "Filter" : rule.FilterName;
            if (matchingSpots.Count == 1)
            {
                title = $"Nieuwe spot in '{filterLabel}'";
                body = $"{matchingSpots[0].Title} ({matchingSpots[0].CategoryName}, {matchingSpots[0].FormattedSize})";
            }
            else
            {
                title = $"Spotnet: {matchingSpots.Count} nieuwe spots in '{filterLabel}'";
                var sample = matchingSpots.Take(3).Select(s => s.Title);
                body = string.Join(", ", sample) + (matchingSpots.Count > 3 ? $" (+{matchingSpots.Count - 3} meer)" : "");
            }
        }
        else // Keyword
        {
            string kwLabel = string.IsNullOrWhiteSpace(rule.Keywords) ? rule.Name : rule.Keywords;
            if (matchingSpots.Count == 1)
            {
                title = $"Alert: '{kwLabel}' gevonden!";
                body = $"{matchingSpots[0].Title} ({matchingSpots[0].CategoryName}, {matchingSpots[0].FormattedSize})";
            }
            else
            {
                title = $"Alert: {matchingSpots.Count} nieuwe spots voor '{kwLabel}'";
                var sample = matchingSpots.Take(3).Select(s => s.Title);
                body = string.Join(", ", sample) + (matchingSpots.Count > 3 ? $" (+{matchingSpots.Count - 3} meer)" : "");
            }
        }

        var notif = new SpotNotificationItem
        {
            Id = Guid.NewGuid().ToString("N"),
            RuleId = rule.Id,
            RuleName = rule.Name,
            RuleType = rule.Type,
            Title = title,
            Body = body,
            SpotCount = matchingSpots.Count,
            Spots = matchingSpots,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false
        };

        lock (_lock)
        {
            // Insert at beginning
            _config.Notifications.Insert(0, notif);
            // Cap at 100 entries
            if (_config.Notifications.Count > 100)
            {
                _config.Notifications.RemoveRange(100, _config.Notifications.Count - 100);
            }
            SaveConfig();
        }

        // Show the desktop toast when the user wants them
        if (_config.WindowsNotificationsEnabled)
        {
            try
            {
                _notifier?.Show(title, body);
            }
            catch (Exception ex)
            {
                Log.Warn("Desktop notifier failed: {0}", ex.Message);
            }
        }

        UnreadCountChanged?.Invoke();
        NotificationsUpdated?.Invoke();

        return notif;
    }

    private static NotificationConfig LoadConfigFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<NotificationConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to load notifications_config.json: {0}", ex.Message);
        }
        return new NotificationConfig();
    }

    private NotificationConfig LoadConfig() => LoadConfigFrom(_configPath);

    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { WriteIndented = true };

    private void SaveConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, ConfigJsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save notifications_config.json: {0}", ex.Message);
        }
    }
}
