using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Notifications;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Fase 4: de gedeelde meldingen-engine (Spotnet.Core) en haar Mac-kant.
/// De engine wordt getest met een nep-database; de SQL-regelquery tegen een echte
/// tijdelijke SQLite-database, zoals Windows dat in NotificationSystemTests doet
/// met zijn eigen engine.
/// </summary>
public class NotificationEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbFile;

    public NotificationEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "spotnet_notif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbFile = Path.Combine(_tempDir, "test.db");
    }

    /// <summary>Lazy SQLite setup — alleen de SQL-tests openen een database.</summary>
    private (SpotDatabaseService svc, MacSqliteDb db) CreateDb()
    {
        var db = new MacSqliteDb(_tempDbFile);
        var svc = new SpotDatabaseService(db);
        svc.EnsureCreatedAsync().GetAwaiter().GetResult();
        return (svc, db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ── De nep-database voor de engine-tests ──────────────────────────────────

    private sealed class FakeSpotQuery : INotificationSpotQuery
    {
        public List<SpotSummaryItem> Spots { get; } = new();
        public long MaxRowId { get; set; } = 1000;
        public int QueryCount { get; private set; }

        public Task<long> GetMaxSpotRowIdAsync() => Task.FromResult(MaxRowId);

        public Task<List<SpotSummaryItem>> QuerySpotsForRuleAsync(NotificationRule rule, long sinceRowId, int limit)
        {
            QueryCount++;
            return Task.FromResult(Spots.Where(s => s.Id > sinceRowId).Take(limit).ToList());
        }
    }

    private sealed class FakeNotifier : ISpotnetNotifier
    {
        public List<(string Title, string Body)> Shown { get; } = new();
        public bool Show(string title, string body)
        {
            Shown.Add((title, body));
            return true;
        }
    }

    private sealed class RecordingApplier
    {
        public List<int> Applied { get; } = new();
    }

    private NotificationManager CreateEngine(FakeSpotQuery query, FakeNotifier notifier, RecordingApplier? applier = null)
    {
        var mgr = new NotificationManager(query, notifier, _tempDir);
        if (applier != null)
        {
            mgr.AutoUpdateSettingsApplier = (minutes, force) => applier.Applied.Add(minutes);
        }
        return mgr;
    }

    // ── Regels ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rule_IntervalDescriptions_MatchWindowsLabels()
    {
        Assert.Equal("Direct bij elke sync", new NotificationRule { CheckIntervalMinutes = 0 }.IntervalDescription);
        Assert.Equal("Elke 15 minuten", new NotificationRule { CheckIntervalMinutes = 15 }.IntervalDescription);
        Assert.Equal("Elke 30 minuten", new NotificationRule { CheckIntervalMinutes = 30 }.IntervalDescription);
        Assert.Equal("Elk uur", new NotificationRule { CheckIntervalMinutes = 60 }.IntervalDescription);
        Assert.Equal("Elke 8 uur", new NotificationRule { CheckIntervalMinutes = 480 }.IntervalDescription);
        Assert.Equal("Elke 24 uur", new NotificationRule { CheckIntervalMinutes = 1440 }.IntervalDescription);
        Assert.Equal("Elke 45 minuten", new NotificationRule { CheckIntervalMinutes = 45 }.IntervalDescription);
        Assert.True(new NotificationRule { CheckIntervalMinutes = 0 }.IsDirectOnSync);
        Assert.False(new NotificationRule { CheckIntervalMinutes = 15 }.IsDirectOnSync);
    }

    [Fact]
    public void AddUpdateToggleDeleteRule_WorksAndInitializesWatermark()
    {
        var query = new FakeSpotQuery { MaxRowId = 4242 };
        var mgr = CreateEngine(query, new FakeNotifier());

        var rule = new NotificationRule { Name = "F1", Type = NotificationRuleType.Keyword, Keywords = "F1, Formule 1" };
        mgr.AddOrUpdateRule(rule);

        var stored = mgr.Config.Rules.Single(r => r.Id == rule.Id);
        Assert.Equal(4242, stored.LastCheckedRowId); // nieuwe regel begint op max, niet op 0
        Assert.True(stored.Enabled);

        mgr.ToggleRule(rule.Id);
        Assert.False(mgr.Config.Rules.Single(r => r.Id == rule.Id).Enabled);

        rule.Name = "F1 gewoon";
        mgr.AddOrUpdateRule(rule);
        Assert.Equal("F1 gewoon", mgr.Config.Rules.Single(r => r.Id == rule.Id).Name);
        Assert.Single(mgr.Config.Rules);

        mgr.DeleteRule(rule.Id);
        Assert.Empty(mgr.Config.Rules);
    }

    [Fact]
    public void SetAutoSyncInterval_ClampsToMinimum5()
    {
        var mgr = CreateEngine(new FakeSpotQuery(), new FakeNotifier());
        mgr.SetAutoSyncInterval(1);
        Assert.Equal(5, mgr.Config.AutoSyncIntervalMinutes);
        mgr.SetAutoSyncInterval(60);
        Assert.Equal(60, mgr.Config.AutoSyncIntervalMinutes);
    }

    [Fact]
    public void DirectRule_ForcesAutoSyncThroughApplier()
    {
        var query = new FakeSpotQuery();
        var notifier = new FakeNotifier();
        var applier = new RecordingApplier();
        var mgr = CreateEngine(query, notifier, applier);

        mgr.AddOrUpdateRule(new NotificationRule
        {
            Name = "direct",
            Type = NotificationRuleType.Keyword,
            Keywords = "linux",
            CheckIntervalMinutes = 0
        });

        Assert.NotEmpty(applier.Applied); // auto-sync moet via de host worden aangezet
        Assert.True(applier.Applied.All(m => m >= 5));
    }

    // ── Evaluatie ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_NewRuleFirst_OnlySetsWatermark_NoNotification()
    {
        var query = new FakeSpotQuery { MaxRowId = 900 };
        var mgr = CreateEngine(query, new FakeNotifier());
        var rule = new NotificationRule { Name = "r", Type = NotificationRuleType.Keyword, Keywords = "x" };
        mgr.AddOrUpdateRule(rule);

        await mgr.EvaluateRulesAsync(onlyDirect: true);

        Assert.Empty(mgr.Config.Notifications);
        Assert.Equal(900, mgr.Config.Rules.Single().LastCheckedRowId);
    }

    [Fact]
    public async Task Evaluate_MatchingSpots_BundlesAndNotifies()
    {
        var query = new FakeSpotQuery { MaxRowId = 100 };
        query.Spots.AddRange(new[]
        {
            new SpotSummaryItem { Id = 101, Title = "Ubuntu 26.04", CategoryName = "Applicaties", FormattedSize = "1.2 GB", MessageId = "a@b" },
            new SpotSummaryItem { Id = 102, Title = "Ubuntu 26.10", CategoryName = "Applicaties", FormattedSize = "1.3 GB", MessageId = "c@d" }
        });
        var notifier = new FakeNotifier();
        var mgr = CreateEngine(query, notifier);

        var rule = new NotificationRule { Name = "ubuntu", Type = NotificationRuleType.Keyword, Keywords = "ubuntu", CheckIntervalMinutes = 15, LastCheckedRowId = 100, LastCheckedUtc = DateTime.UtcNow.AddMinutes(-30) };
        mgr.AddOrUpdateRule(rule);

        await mgr.EvaluateRulesAsync(onlyDirect: false);

        var notif = Assert.Single(mgr.Config.Notifications);
        Assert.Equal(2, notif.SpotCount);
        Assert.Contains("ubuntu", notif.Title, StringComparison.OrdinalIgnoreCase);
        Assert.False(notif.IsRead);
        Assert.Equal(1, mgr.UnreadCount);
        Assert.Single(notifier.Shown); // toast ging uit (WindowsNotificationsEnabled default true)
        Assert.Equal(102, mgr.Config.Rules.Single().LastCheckedRowId);
    }

    [Fact]
    public async Task Evaluate_DesktopNotificationDisabled_NoToastButStillRecorded()
    {
        var query = new FakeSpotQuery { MaxRowId = 10 };
        query.Spots.Add(new SpotSummaryItem { Id = 11, Title = "Spot", CategoryName = "Films", FormattedSize = "4 GB" });
        var notifier = new FakeNotifier();
        var mgr = CreateEngine(query, notifier);
        mgr.SetDesktopNotificationsEnabled(false);

        var rule = new NotificationRule { Name = "r", Type = NotificationRuleType.Keyword, Keywords = "spot", LastCheckedRowId = 10, LastCheckedUtc = DateTime.UtcNow.AddMinutes(-30) };
        mgr.AddOrUpdateRule(rule);

        await mgr.EvaluateRulesAsync(onlyDirect: false);

        Assert.Single(mgr.Config.Notifications);
        Assert.Empty(notifier.Shown);
    }

    [Fact]
    public async Task Evaluate_NothingNew_SavesLastCheckedButNoNotification()
    {
        var query = new FakeSpotQuery { MaxRowId = 50 };
        var mgr = CreateEngine(query, new FakeNotifier());
        var rule = new NotificationRule { Name = "r", Type = NotificationRuleType.Keyword, Keywords = "x", LastCheckedRowId = 50, LastCheckedUtc = DateTime.UtcNow.AddMinutes(-30) };
        mgr.AddOrUpdateRule(rule);

        await mgr.EvaluateRulesAsync(onlyDirect: false);

        Assert.Empty(mgr.Config.Notifications);
        Assert.Equal(0, mgr.UnreadCount);
    }

    [Fact]
    public async Task Evaluate_PeriodicRun_SkipsRulesInsideTheirInterval()
    {
        var query = new FakeSpotQuery { MaxRowId = 50 };
        query.Spots.Add(new SpotSummaryItem { Id = 51, Title = "Nieuw", CategoryName = "Films", FormattedSize = "1 GB" });
        var mgr = CreateEngine(query, new FakeNotifier());

        var fresh = new NotificationRule { Name = "vers", Type = NotificationRuleType.Keyword, Keywords = "x", CheckIntervalMinutes = 15, LastCheckedRowId = 50, LastCheckedUtc = DateTime.UtcNow.AddSeconds(-30) };
        var due = new NotificationRule { Name = "verschuldigd", Type = NotificationRuleType.Keyword, Keywords = "y", CheckIntervalMinutes = 15, LastCheckedRowId = 50, LastCheckedUtc = DateTime.UtcNow.AddMinutes(-20) };
        mgr.AddOrUpdateRule(fresh);
        mgr.AddOrUpdateRule(due);

        await mgr.EvaluateRulesAsync(onlyDirect: false);

        Assert.Single(mgr.Config.Notifications); // alleen de verschuldigde regel
        Assert.Contains("verschuldigd", mgr.Config.Notifications[0].RuleName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_DirectOnSync_OnlyRunsOnSyncFinished()
    {
        var query = new FakeSpotQuery { MaxRowId = 10 };
        query.Spots.Add(new SpotSummaryItem { Id = 11, Title = "Sync spot", CategoryName = "Series", FormattedSize = "2 GB" });
        var mgr = CreateEngine(query, new FakeNotifier());

        var rule = new NotificationRule { Name = "direct", Type = NotificationRuleType.Keyword, Keywords = "sync", CheckIntervalMinutes = 0, LastCheckedRowId = 10, LastCheckedUtc = DateTime.UtcNow };
        mgr.AddOrUpdateRule(rule);

        // Periodieke run: direct-regels horen daar niet thuis.
        await mgr.EvaluateRulesAsync(onlyDirect: false);
        Assert.Empty(mgr.Config.Notifications);

        // Na een sync: direct-regel evalueert wél.
        mgr.OnSyncFinished();
        await Task.Delay(200); // OnSyncFinished werkt op de achtergrond
        Assert.Single(mgr.Config.Notifications);
    }

    [Fact]
    public async Task TestRuleNow_ManualTest_DoesNotAdvanceWatermark()
    {
        var query = new FakeSpotQuery { MaxRowId = 100 };
        query.Spots.Add(new SpotSummaryItem { Id = 60, Title = "Oudere spot", CategoryName = "Films", FormattedSize = "700 MB" });
        var mgr = CreateEngine(query, new FakeNotifier());

        var rule = new NotificationRule { Name = "r", Type = NotificationRuleType.Keyword, Keywords = "oudere", LastCheckedRowId = 100, LastCheckedUtc = DateTime.UtcNow };
        mgr.AddOrUpdateRule(rule);

        var result = await mgr.TestRuleNowAsync(rule.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result.SpotCount);
        // Een handmatige test schuift het watermerk niet door en slaat de testmelding
        // niet op als ongelezen melding? Windows slaat hem wél op; volg Windows.
        Assert.Single(mgr.Config.Notifications);
        Assert.Equal(100, mgr.Config.Rules.Single().LastCheckedRowId);
    }

    // ── Meldingenbeheer ───────────────────────────────────────────────────────

    [Fact]
    public void Notifications_AddMarkDelete_TrackUnreadCount()
    {
        var mgr = CreateEngine(new FakeSpotQuery(), new FakeNotifier());
        mgr.ClearAllNotifications();

        var n1 = new SpotNotificationItem { Title = "één", Body = "b" };
        var n2 = new SpotNotificationItem { Title = "twee", Body = "b" };
        mgr.AddNotification(n1);
        mgr.AddNotification(n2);
        Assert.Equal(2, mgr.UnreadCount);

        mgr.MarkAsRead(n1.Id);
        Assert.Equal(1, mgr.UnreadCount);

        mgr.MarkAllAsRead();
        Assert.Equal(0, mgr.UnreadCount);

        mgr.DeleteNotification(n2.Id);
        Assert.Single(mgr.Config.Notifications);

        mgr.ClearAllNotifications();
        Assert.Empty(mgr.Config.Notifications);
    }

    [Fact]
    public void Notifications_CappedAt100()
    {
        var mgr = CreateEngine(new FakeSpotQuery(), new FakeNotifier());
        mgr.ClearAllNotifications();
        for (int i = 0; i < 105; i++)
        {
            mgr.AddNotification(new SpotNotificationItem { Title = $"n{i}" });
        }
        Assert.Equal(100, mgr.Config.Notifications.Count);
        Assert.Equal("n104", mgr.Config.Notifications[0].Title); // nieuwste vooraan
    }

    [Fact]
    public void NotifyDownloadComplete_RecordsUnreadWithDutchTitle()
    {
        var mgr = CreateEngine(new FakeSpotQuery(), new FakeNotifier());
        mgr.ClearAllNotifications();

        mgr.NotifyDownloadComplete("Mijn spot", success: true);
        var ok = mgr.Config.Notifications.Single();
        Assert.Equal("Download voltooid", ok.Title);
        Assert.Equal("Mijn spot", ok.Body);
        Assert.Equal(NotificationRuleType.Download, ok.RuleType);
        Assert.False(ok.IsRead);

        mgr.NotifyDownloadComplete("Mijn spot", success: false);
        Assert.Equal("Download mislukt", mgr.Config.Notifications[0].Title);
        Assert.Equal(2, mgr.UnreadCount);
    }

    // ── Persistentie ──────────────────────────────────────────────────────────

    [Fact]
    public void Persistence_ConfigSurvivesRecreation()
    {
        var query = new FakeSpotQuery { MaxRowId = 7 };
        var mgr1 = CreateEngine(query, new FakeNotifier());
        mgr1.AddOrUpdateRule(new NotificationRule { Name = "blijft", Type = NotificationRuleType.Keyword, Keywords = "x" });
        mgr1.AddNotification(new SpotNotificationItem { Title = "melding", Body = "b" });
        mgr1.SetAutoSyncInterval(30);

        var mgr2 = new NotificationManager(new FakeSpotQuery(), new FakeNotifier(), _tempDir);
        Assert.Single(mgr2.Config.Rules);
        Assert.Equal("blijft", mgr2.Config.Rules[0].Name);
        Assert.Single(mgr2.Config.Notifications);
        Assert.Equal(30, mgr2.Config.AutoSyncIntervalMinutes);
    }

    [Fact]
    public void Persistence_BrokenConfigFile_FallsBackToDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "notifications_config.json"), "{ geen json ");
        var mgr = new NotificationManager(new FakeSpotQuery(), new FakeNotifier(), _tempDir);
        Assert.Empty(mgr.Config.Rules);
        Assert.True(mgr.Config.WindowsNotificationsEnabled);
    }

    // ── De Mac-SQL-kant ───────────────────────────────────────────────────────

    [Fact]
    public async Task MacQuery_MaxRowId_AndKeywordSearch_AgainstSqlite()
    {
        var (svc, db) = CreateDb();
        using var _ = db;

        await svc.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "m1@x", Subject = "Linux Mint 22", Category = 4, Key = 0, Date = 1700000000, Filesize = 1200000000 },
            new SpotItem { MsgId = "m2@x", Subject = "Windows 11 ISO", Category = 4, Key = 0, Date = 1700000100, Filesize = 5000000000 },
            new SpotItem { MsgId = "m3@x", Subject = "F1 Grand Prix review", Category = 1, Key = 0, Date = 1700000200, Filesize = 800000000 }
        });

        Assert.Equal(3, await svc.GetMaxSpotRowIdAsync());

        var rule = new NotificationRule { Name = "linux", Type = NotificationRuleType.Keyword, Keywords = "linux" };
        var hits = await svc.QuerySpotsForNotificationRuleAsync(rule, sinceRowId: 0, limit: 50);
        var hit = Assert.Single(hits);
        Assert.Equal("Linux Mint 22", hit.Title);
        Assert.Equal("Applicaties", hit.CategoryName);
        Assert.Equal("1.12 GB", hit.FormattedSize);
        Assert.NotEmpty(hit.FormattedDate);

        // Watermerk: spots boven rowid 2 alleen.
        var newer = await svc.QuerySpotsForNotificationRuleAsync(rule, sinceRowId: 2, limit: 50);
        Assert.Empty(newer);
    }

    [Fact]
    public async Task MacQuery_MultipleKeywords_OrAcrossTerms()
    {
        var (svc, db) = CreateDb();
        using var _ = db;

        await svc.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "a@x", Subject = "Koffie filter", Category = 4, Key = 0, Date = 1700000000 },
            new SpotItem { MsgId = "b@x", Subject = "Thee zeef", Category = 4, Key = 0, Date = 1700000000 }
        });

        var rule = new NotificationRule { Name = "warm", Type = NotificationRuleType.Keyword, Keywords = "koffie, thee" };
        var hits = await svc.QuerySpotsForNotificationRuleAsync(rule, 0, 50);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task MacQuery_KeyGuardExcludesCommentAndReportRows()
    {
        var (svc, db) = CreateDb();
        using var _ = db;

        await svc.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "real@x", Subject = "Echte spot met linux", Category = 4, Key = 0, Date = 1700000000 },
            new SpotItem { MsgId = "mod@x", Subject = "Moderatie-row met linux", Category = 4, Key = 2, Date = 1700000000 }
        });

        var rule = new NotificationRule { Name = "linux", Type = NotificationRuleType.Keyword, Keywords = "linux" };
        var hits = await svc.QuerySpotsForNotificationRuleAsync(rule, 0, 50);
        var hit = Assert.Single(hits);
        Assert.Equal("real@x", hit.MessageId);
    }

    [Fact]
    public async Task MacQuery_FilterRule_WithCategoryExpression()
    {
        var (svc, db) = CreateDb();
        using var _ = db;

        await svc.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "f1@x", Subject = "Film A", Category = 1, Key = 0, Date = 1700000000, Filesize = 4400000000 },
            new SpotItem { MsgId = "f2@x", Subject = "Muziek B", Category = 2, Key = 0, Date = 1700000000 }
        });

        var rule = new NotificationRule { Name = "films", Type = NotificationRuleType.Filter, FilterName = "Films", FilterQuery = "cat = 1" };
        var hits = await svc.QuerySpotsForNotificationRuleAsync(rule, 0, 50);
        var hit = Assert.Single(hits);
        Assert.Equal("Film A", hit.Title);
    }

    [Fact]
    public async Task MacAdapter_BridgesToEngine()
    {
        var (svc, db) = CreateDb();
        using var _ = db;

        await svc.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "b1@x", Subject = "Bridge test", Category = 6, Key = 0, Date = 1700000000 }
        });

        var adapter = new Spotnet.Mac.Platform.MacNotificationSpotQuery(svc);
        Assert.Equal(1, await adapter.GetMaxSpotRowIdAsync());
        var hits = await adapter.QuerySpotsForRuleAsync(new NotificationRule { Name = "r", Type = NotificationRuleType.Keyword, Keywords = "bridge" }, 0, 50);
        Assert.Single(hits);
    }
}
