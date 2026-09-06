using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.DAL;
using Spotnet.Helpers;
using Spotnet.Notifications;
using Spotnet.Properties;

namespace Spotnet.Notifications;

/// <summary>
/// Windows composition root for the shared notification engine in Spotnet.Core.
/// Binds the engine to the Windows SQLite engine (via SqlDbFactory), to
/// Settings.Default's auto-sync fields, and to the WPF evaluation timer.
/// NotificationManager.Instance resolves to the engine inside this host, so
/// existing call sites keep working unchanged.
/// </summary>
public class NotificationHost : INotificationSpotQuery
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly Lazy<NotificationHost> InstanceHolder = new Lazy<NotificationHost>(() => new NotificationHost());
    public static NotificationHost Instance => InstanceHolder.Value;

    private Timer? _evaluationTimer;
    private bool _initialized;

    public NotificationManager Engine { get; }

    NotificationHost()
    {
        Engine = new NotificationManager(this, new Helpers.NotifierAdapter(), AppHelper.SettingsFolder);
        Engine.AutoUpdateSettingsApplier = ApplyAutoUpdateSettings;
    }

    /// <summary>
    /// Windows' SyncAutoUpdateSettings/SetAutoSyncInterval logic over Settings.Default:
    /// force writes the interval unconditionally (the user picked it), otherwise only
    /// raises a too-low interval to the minimum of 5 minutes.
    /// </summary>
    private static void ApplyAutoUpdateSettings(int intervalMinutes, bool force)
    {
        try
        {
            if (force)
            {
                Settings.Default.DbAutoUpdateIntervalMin = intervalMinutes;
            }
            else if (Settings.Default.DbAutoUpdateIntervalMin < 5)
            {
                Settings.Default.DbAutoUpdateIntervalMin = Math.Max(5, intervalMinutes);
            }
            Settings.Default.DbAutoUpdateEnabled = true;
            Settings.Default.Save();
            if (force)
            {
                // Windows' SetAutoSyncInterval herstart de timer zodat de nieuwe
                // interval meteen telt; DbUpdateTimerStart maakt alleen aan als hij
                // er nog niet is.
                DbUpdater.DbUpdateTimerStop();
            }
            DbUpdater.DbUpdateTimerStart();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to apply notification auto-sync settings: {0}", ex.Message);
        }
    }

    public void Initialize()
    {
        lock (this)
        {
            if (_initialized) return;
            _initialized = true;

            Engine.Initialize();

            // Direct alerts trigger immediately when new spots arrive
            DbUpdater.OnDbUpdateEnd += OnDbUpdateFinished;

            // Background evaluation timer for periodic rules, every 60 s
            _evaluationTimer = new Timer(_ => Engine.OnPeriodicTimer(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

            Log.Info("NotificationHost initialized.");
        }
    }

    private void OnDbUpdateFinished()
    {
        Engine.OnSyncFinished();
    }

    /// <summary>Adapts the static Windows balloon helper to the engine's notifier interface.</summary>
    private sealed class NotifierAdapter : ISpotnetNotifier
    {
        public bool Show(string title, string body)
        {
            try
            {
                Helpers.NotificationHelper.Show(title, body);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Windows notification failed: {0}", ex.Message);
                return false;
            }
        }
    }

    // ── INotificationSpotQuery ────────────────────────────────────────────────

    public Task<long> GetMaxSpotRowIdAsync()
    {
        return Task.Run(() =>
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
            return 0L;
        });
    }

    public Task<List<SpotSummaryItem>> QuerySpotsForRuleAsync(NotificationRule rule, long sinceRowId, int limit)
    {
        return Task.Run(() => QuerySpotsForRule(rule, sinceRowId, limit));
    }

    /// <summary>
    /// The rule query the engine ran inline before the move to Core; same clauses,
    /// same column order, same formatters.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Dynamic WHERE clause with parameterized keyword values")]
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
                    string clean = NotificationSpotFormatting.CleanFilterQuery(rule.FilterQuery);
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
                    CategoryName = NotificationSpotFormatting.GetCategoryName(cat),
                    FormattedSize = NotificationSpotFormatting.FormatFileSize(filesize),
                    Date = date,
                    FormattedDate = NotificationSpotFormatting.FormatDate(date)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to query spots for rule '{0}': {1}", rule.Name, ex.Message);
        }

        return list;
    }
}
