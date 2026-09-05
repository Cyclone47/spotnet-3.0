using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.DAL;
using Spotnet.Helpers;
using Spotnet.Properties;
using Spotnet.Remote;

namespace Spotnet.Notifications;

public class NotificationManager
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<NotificationManager> InstanceHolder = new Lazy<NotificationManager>(() => new NotificationManager());
    public static NotificationManager Instance => InstanceHolder.Value;

    private static readonly object Lock = new object();
    private static string ConfigPath => Path.Combine(AppHelper.SettingsFolder, "notifications_config.json");

    private NotificationConfig _config;
    private Timer _evaluationTimer;
    private bool _initialized;

    public event Action UnreadCountChanged;
    public event Action NotificationsUpdated;
    public event Action RulesUpdated;

    public NotificationConfig Config
    {
        get
        {
            lock (Lock) return _config;
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (Lock)
            {
                return _config?.Notifications?.Count(n => !n.IsRead) ?? 0;
            }
        }
    }

    public NotificationManager()
    {
        _config = LoadConfig();
    }

    public void Initialize()
    {
        lock (Lock)
        {
            if (_initialized) return;
            _initialized = true;

            // Hook to DbUpdater so Direct alerts trigger immediately when new spots arrive
            DbUpdater.OnDbUpdateEnd += OnDbUpdateFinished;

            // Start background evaluation timer for periodic rules (every 60s)
            _evaluationTimer = new Timer(_ =>
            {
                try
                {
                    EvaluateRules(onlyDirect: false);
                }
                catch (Exception ex)
                {
                    Log.Warn("Error evaluating notification rules: {0}", ex.Message);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

            // Ensure auto-sync interval is valid if any direct rules exist
            SyncAutoUpdateSettings();

            Log.Info("NotificationManager initialized with {0} rules and {1} notifications.", _config.Rules.Count, _config.Notifications.Count);
        }
    }

    private void OnDbUpdateFinished()
    {
        Task.Run(() =>
        {
            try
            {
                EvaluateRules(onlyDirect: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Error in OnDbUpdateFinished notification evaluation: {0}", ex.Message);
            }
        });
    }

    public void SyncAutoUpdateSettings()
    {
        lock (Lock)
        {
            bool hasDirectRules = _config.Rules.Any(r => r.Enabled && r.IsDirectOnSync);
            if (hasDirectRules)
            {
                // Ensure AutoUpdate is active and minimum 5 minutes
                int currentInterval = Settings.Default.DbAutoUpdateIntervalMin;
                if (currentInterval < 5)
                {
                    Settings.Default.DbAutoUpdateIntervalMin = Math.Max(5, _config.AutoSyncIntervalMinutes);
                }
                Settings.Default.DbAutoUpdateEnabled = true;
                Settings.Default.Save();
                DbUpdater.DbUpdateTimerStart();
            }
        }
    }

    public void SetAutoSyncInterval(int minutes)
    {
        lock (Lock)
        {
            if (minutes < 5) minutes = 5; // Minimum 5 minutes per user requirement
            _config.AutoSyncIntervalMinutes = minutes;
            Settings.Default.DbAutoUpdateIntervalMin = minutes;
            Settings.Default.DbAutoUpdateEnabled = true;
            Settings.Default.Save();
            DbUpdater.DbUpdateTimerStop();
            DbUpdater.DbUpdateTimerStart();
            SaveConfig();
        }
    }

    public void AddOrUpdateRule(NotificationRule rule)
    {
        if (rule == null) return;
        lock (Lock)
        {
            // If new rule, initialize LastCheckedRowId to current max rowid to avoid alerting on all past spots
            if (rule.LastCheckedRowId <= 0)
            {
                rule.LastCheckedRowId = GetMaxSpotRowId();
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
        lock (Lock)
        {
            _config.Rules.RemoveAll(r => r.Id == ruleId);
            SaveConfig();
            RulesUpdated?.Invoke();
        }
    }

    public void ToggleRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;
        lock (Lock)
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
        lock (Lock)
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
        lock (Lock)
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
        lock (Lock)
        {
            _config.Notifications.RemoveAll(n => n.Id == notificationId);
            SaveConfig();
            UnreadCountChanged?.Invoke();
            NotificationsUpdated?.Invoke();
        }
    }

    public void ClearAllNotifications()
    {
        lock (Lock)
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
        lock (Lock)
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
                Title = success ? (Words.NotificationDownloadFinished ?? "Download voltooid") : (Words.NotificationDownloadProblem ?? "Download mislukt"),
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

    public long GetMaxSpotRowId()
    {
        try
        {
            using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
            using ISqlDbTransaction tx = db.BeginReadTransaction();
            using DbCommand cmd = db.CreateCommand(tx);
            cmd.CommandText = "SELECT MAX(rowid) FROM spots";
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value && long.TryParse(res.ToString(), out long maxId))
            {
                return maxId;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to get max spot rowid: {0}", ex.Message);
        }
        return 0;
    }

    public void EvaluateRules(bool onlyDirect = false, string specificRuleId = null, bool isManualTest = false)
    {
        List<NotificationRule> rulesToEvaluate;
        lock (Lock)
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
                EvaluateSingleRule(rule, isManualTest);
            }
            catch (Exception ex)
            {
                Log.Error("Error evaluating rule '{0}': {1}", rule.Name, ex.Message);
            }
        }
    }

    public SpotNotificationItem TestRuleNow(string ruleId)
    {
        NotificationRule rule;
        lock (Lock)
        {
            rule = _config.Rules.FirstOrDefault(r => r.Id == ruleId);
        }
        if (rule == null) return null;

        return EvaluateSingleRule(rule, isManualTest: true);
    }

    private SpotNotificationItem EvaluateSingleRule(NotificationRule rule, bool isManualTest = false)
    {
        long sinceRowId = isManualTest ? Math.Max(0, rule.LastCheckedRowId - 50) : rule.LastCheckedRowId;

        // If rule has never been run and this is not a manual test, initialize rowid to max
        if (sinceRowId <= 0 && !isManualTest)
        {
            rule.LastCheckedRowId = GetMaxSpotRowId();
            rule.LastCheckedUtc = DateTime.UtcNow;
            lock (Lock) SaveConfig();
            return null;
        }

        var matchingSpots = QuerySpotsForRule(rule, sinceRowId, limit: isManualTest ? 5 : 50);

        rule.LastCheckedUtc = DateTime.UtcNow;
        if (matchingSpots.Count > 0 && !isManualTest)
        {
            rule.LastCheckedRowId = Math.Max(rule.LastCheckedRowId, matchingSpots.Max(s => s.Id));
        }

        if (matchingSpots.Count == 0)
        {
            lock (Lock) SaveConfig();
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

        lock (Lock)
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

        // Show Windows desktop notification (Toast / Balloon)
        if (_config.WindowsNotificationsEnabled)
        {
            NotificationHelper.Show(title, body);
        }

        UnreadCountChanged?.Invoke();
        NotificationsUpdated?.Invoke();

        return notif;
    }

    public List<SpotSummaryItem> QuerySpotsForRule(NotificationRule rule, long sinceRowId, int limit = 50)
    {
        var list = new List<SpotSummaryItem>();
        try
        {
            using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
            using ISqlDbTransaction tx = db.BeginReadTransaction();
            using DbCommand cmd = db.CreateCommand(tx);

            var clauses = new List<string>
            {
                "spots.key != 2 AND spots.key != 5"
            };

            if (sinceRowId > 0)
            {
                clauses.Add($"spots.rowid > {sinceRowId}");
            }

            if (rule.Type == NotificationRuleType.Filter)
            {
                if (!string.IsNullOrWhiteSpace(rule.FilterQuery))
                {
                    string clean = RemoteCatalogService.CleanFilterQuery(rule.FilterQuery);
                    clauses.Add($"({clean})");
                }
                else if (!string.IsNullOrWhiteSpace(rule.FilterId) && rule.FilterId.StartsWith("cat_") && int.TryParse(rule.FilterId.Substring(4), out int cId))
                {
                    clauses.Add($"spots.cat = {cId}");
                }
            }
            else // Keyword
            {
                if (rule.Category.HasValue && rule.Category.Value > 0)
                {
                    clauses.Add($"spots.cat = {rule.Category.Value}");
                }

                if (!string.IsNullOrWhiteSpace(rule.Keywords))
                {
                    // Split keywords by comma or spaces
                    var rawTerms = rule.Keywords.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(t => t.Trim())
                                               .Where(t => t.Length > 0)
                                               .ToList();

                    if (rawTerms.Count > 0)
                    {
                        var termClauses = new List<string>();
                        int pIdx = 0;
                        foreach (var term in rawTerms)
                        {
                            string pName = $"@kw_{pIdx++}";
                            termClauses.Add($"spots.subject LIKE {pName}");
                            var param = cmd.CreateParameter();
                            param.ParameterName = pName;
                            param.Value = $"%{term}%";
                            cmd.Parameters.Add(param);
                        }
                        clauses.Add($"({string.Join(" OR ", termClauses)})");
                    }
                }
            }

            cmd.CommandText = $@"
                SELECT spots.rowid, spots.msgid, spots.subject, spots.cat, spots.filesize, spots.date
                FROM spots
                WHERE {string.Join(" AND ", clauses)}
                ORDER BY spots.rowid ASC
                LIMIT {limit}";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long rowId = reader.GetInt64(0);
                string msgId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string subject = reader.IsDBNull(2) ? "" : reader.GetString(2);
                int cat = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                long filesize = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                long date = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);

                list.Add(new SpotSummaryItem
                {
                    Id = rowId,
                    MessageId = msgId,
                    Title = subject,
                    Category = cat,
                    CategoryName = RemoteCatalogService.GetCategoryName(cat),
                    FormattedSize = RemoteCatalogService.FormatFileSize(filesize),
                    Date = date,
                    FormattedDate = RemoteCatalogService.FormatDate(date)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to query spots for rule '{0}': {1}", rule.Name, ex.Message);
        }

        return list;
    }

    private static NotificationConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
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

    private void SaveConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save notifications_config.json: {0}", ex.Message);
        }
    }
}
