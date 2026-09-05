using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spotnet.Notifications;
using Spotnet.Remote;
using Xunit;

namespace Spotnet.Tests;

public class NotificationSystemTests
{
    [Fact]
    public void NotificationRule_IntervalDescription_ReturnsCorrectDutchLabels()
    {
        var r0 = new NotificationRule { CheckIntervalMinutes = 0 };
        Assert.Equal("Direct bij elke sync", r0.IntervalDescription);
        Assert.True(r0.IsDirectOnSync);

        var r15 = new NotificationRule { CheckIntervalMinutes = 15 };
        Assert.Equal("Elke 15 minuten", r15.IntervalDescription);
        Assert.False(r15.IsDirectOnSync);

        var r30 = new NotificationRule { CheckIntervalMinutes = 30 };
        Assert.Equal("Elke 30 minuten", r30.IntervalDescription);

        var r60 = new NotificationRule { CheckIntervalMinutes = 60 };
        Assert.Equal("Elk uur", r60.IntervalDescription);

        var r480 = new NotificationRule { CheckIntervalMinutes = 480 };
        Assert.Equal("Elke 8 uur", r480.IntervalDescription);

        var r1440 = new NotificationRule { CheckIntervalMinutes = 1440 };
        Assert.Equal("Elke 24 uur", r1440.IntervalDescription);

        var rCustom = new NotificationRule { CheckIntervalMinutes = 45 };
        Assert.Equal("Elke 45 minuten", rCustom.IntervalDescription);
    }

    [Fact]
    public void NotificationManager_AutoSyncInterval_ClampsToMinimum5Minutes()
    {
        var mgr = NotificationManager.Instance;

        // Attempting to set below 5 minutes should clamp to 5
        mgr.SetAutoSyncInterval(1);
        Assert.Equal(5, mgr.Config.AutoSyncIntervalMinutes);

        mgr.SetAutoSyncInterval(3);
        Assert.Equal(5, mgr.Config.AutoSyncIntervalMinutes);

        // Setting >= 5 should be accepted
        mgr.SetAutoSyncInterval(15);
        Assert.Equal(15, mgr.Config.AutoSyncIntervalMinutes);

        mgr.SetAutoSyncInterval(60);
        Assert.Equal(60, mgr.Config.AutoSyncIntervalMinutes);
    }

    [Fact]
    public void NotificationManager_AddUpdateDeleteRule_WorksCorrectly()
    {
        var mgr = NotificationManager.Instance;
        string ruleId = Guid.NewGuid().ToString("N");

        var rule = new NotificationRule
        {
            Id = ruleId,
            Name = "Formula 1 Alerts",
            Type = NotificationRuleType.Keyword,
            Keywords = "F1, Formule 1, Grand Prix",
            CheckIntervalMinutes = 0
        };

        mgr.AddOrUpdateRule(rule);
        var retrieved = mgr.Config.Rules.FirstOrDefault(r => r.Id == ruleId);
        Assert.NotNull(retrieved);
        Assert.Equal("Formula 1 Alerts", retrieved.Name);
        Assert.True(retrieved.Enabled);

        // Toggle rule
        mgr.ToggleRule(ruleId);
        retrieved = mgr.Config.Rules.FirstOrDefault(r => r.Id == ruleId);
        Assert.NotNull(retrieved);
        Assert.False(retrieved.Enabled);

        // Delete rule
        mgr.DeleteRule(ruleId);
        retrieved = mgr.Config.Rules.FirstOrDefault(r => r.Id == ruleId);
        Assert.Null(retrieved);
    }

    [Fact]
    public void NotificationManager_NotificationsUnreadAndBundling_TracksCorrectly()
    {
        var mgr = NotificationManager.Instance;

        // Clear existing notifications for isolated test
        mgr.ClearAllNotifications();
        Assert.Equal(0, mgr.UnreadCount);

        // Create notification with bundled spots
        var spots = new List<SpotSummaryItem>
        {
            new SpotSummaryItem { Id = 1, MessageId = "msg1@usenet", Title = "Falling Skies S01", Category = 1, CategoryName = "Beeld" },
            new SpotSummaryItem { Id = 2, MessageId = "msg2@usenet", Title = "Falling Skies S02", Category = 1, CategoryName = "Beeld" },
            new SpotSummaryItem { Id = 3, MessageId = "msg3@usenet", Title = "Falling Skies S03", Category = 1, CategoryName = "Beeld" }
        };

        var notif = new SpotNotificationItem
        {
            RuleId = "rule-falling-skies",
            RuleName = "Falling Skies Serie",
            RuleType = NotificationRuleType.Keyword,
            Title = "Nieuwe spots voor 'Falling Skies Serie'",
            Body = "Er zijn 3 nieuwe spots gevonden.",
            SpotCount = spots.Count,
            Spots = spots
        };
        mgr.AddNotification(notif);

        Assert.NotNull(notif);
        Assert.Equal(3, notif.SpotCount);
        Assert.Equal(3, notif.Spots.Count);
        Assert.False(notif.IsRead);
        Assert.Equal(1, mgr.UnreadCount);

        // Add second notification
        var notif2 = new SpotNotificationItem
        {
            RuleId = "rule-muziek",
            RuleName = "Muziek Filter",
            RuleType = NotificationRuleType.Filter,
            Title = "Nieuwe muziek spots",
            Body = "Er is 1 nieuwe spot gevonden.",
            SpotCount = 1,
            Spots = new List<SpotSummaryItem>
            {
                new SpotSummaryItem { Id = 4, MessageId = "msg4@usenet", Title = "Top 40 Week 36", Category = 2, CategoryName = "Geluid" }
            }
        };
        mgr.AddNotification(notif2);

        Assert.Equal(2, mgr.UnreadCount);

        // Mark first notification as read
        mgr.MarkAsRead(notif.Id);
        Assert.Equal(1, mgr.UnreadCount);

        // Mark all as read
        mgr.MarkAllAsRead();
        Assert.Equal(0, mgr.UnreadCount);

        // Delete second notification
        mgr.DeleteNotification(notif2.Id);
        Assert.Single(mgr.Config.Notifications);

        // Clean up
        mgr.ClearAllNotifications();
        Assert.Empty(mgr.Config.Notifications);
        Assert.Equal(0, mgr.UnreadCount);
    }

    [Fact]
    public void RemoteAuthManager_QrPairingToken_AllowsMultipleHitsWithin5MinutesWithoutPrematureDeletion()
    {
        var auth = RemoteAuthManager.Instance;
        var session = auth.CreatePairingSession();
        Assert.False(string.IsNullOrWhiteSpace(session.Token));

        // First QR pairing hit from mobile device 1
        var req1 = new PairRequestDto
        {
            Token = session.Token,
            DeviceName = "iPhone Safari"
        };
        var res1 = auth.TryPair(req1, "192.168.1.101");
        Assert.True(res1.Success);
        Assert.False(string.IsNullOrWhiteSpace(res1.DeviceToken));

        // Re-scan or second hit from same QR session within 5 minutes must still succeed (not prematurely deleted)
        var req2 = new PairRequestDto
        {
            Token = session.Token,
            DeviceName = "iPhone Chrome"
        };
        var res2 = auth.TryPair(req2, "192.168.1.101");
        Assert.True(res2.Success);
        Assert.False(string.IsNullOrWhiteSpace(res2.DeviceToken));

        // Both device tokens must validate cleanly
        Assert.True(auth.ValidateToken(res1.DeviceToken, "192.168.1.101", out _));
        Assert.True(auth.ValidateToken(res2.DeviceToken, "192.168.1.101", out _));
    }

    [Fact]
    public void NotificationManager_UpdateExistingRule_PreservesIdentityAndUpdatesParameters()
    {
        var mgr = NotificationManager.Instance;
        string ruleId = Guid.NewGuid().ToString("N");

        var rule = new NotificationRule
        {
            Id = ruleId,
            Name = "Original Name",
            Type = NotificationRuleType.Filter,
            FilterId = "filt_1",
            FilterName = "Nieuw",
            FilterQuery = "cat=1",
            CheckIntervalMinutes = 15,
            LastCheckedRowId = 12345,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            Enabled = true
        };

        mgr.AddOrUpdateRule(rule);

        var retrieved = mgr.Config.Rules.FirstOrDefault(r => r.Id == ruleId);
        Assert.NotNull(retrieved);
        Assert.Equal("Original Name", retrieved.Name);
        Assert.Equal(12345, retrieved.LastCheckedRowId);

        // Edit/update rule fields
        retrieved.Name = "Updated Name";
        retrieved.CheckIntervalMinutes = 30;
        retrieved.FilterName = "Films HD";
        mgr.AddOrUpdateRule(retrieved);

        var updated = mgr.Config.Rules.FirstOrDefault(r => r.Id == ruleId);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(30, updated.CheckIntervalMinutes);
        Assert.Equal("Films HD", updated.FilterName);
        // Ensure ID and LastCheckedRowId were preserved
        Assert.Equal(ruleId, updated.Id);
        Assert.Equal(12345, updated.LastCheckedRowId);

        // Clean up
        mgr.DeleteRule(ruleId);
    }
}
