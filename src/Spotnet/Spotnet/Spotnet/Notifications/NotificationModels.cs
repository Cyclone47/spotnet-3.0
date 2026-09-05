using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Spotnet.Notifications;

public enum NotificationRuleType
{
    Filter = 0,
    Keyword = 1,
    Download = 2
}

public class SpotSummaryItem
{
    public long Id { get; set; }
    public string MessageId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Category { get; set; }
    public string CategoryName { get; set; } = "";
    public string FormattedSize { get; set; } = "";
    public long Date { get; set; }
    public string FormattedDate { get; set; } = "";
}

public class NotificationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public NotificationRuleType Type { get; set; } = NotificationRuleType.Filter;

    // Filter alert properties
    public string FilterId { get; set; } = "";
    public string FilterName { get; set; } = "";
    public string FilterQuery { get; set; } = "";

    // Keyword alert properties
    public string Keywords { get; set; } = ""; // Comma or space separated keywords
    public int? Category { get; set; } // null or 0 = All categories

    // Frequency in minutes: 0 = Direct (bij elke sync), or 15, 30, 60, 480, 1440, or custom (min 5)
    public int CheckIntervalMinutes { get; set; } = 15;

    public bool Enabled { get; set; } = true;

    // State tracking
    public long LastCheckedRowId { get; set; } = 0;
    public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsDirectOnSync => CheckIntervalMinutes == 0;

    [JsonIgnore]
    public string IntervalDescription => CheckIntervalMinutes switch
    {
        0 => "Direct bij elke sync",
        15 => "Elke 15 minuten",
        30 => "Elke 30 minuten",
        60 => "Elk uur",
        480 => "Elke 8 uur",
        1440 => "Elke 24 uur",
        _ => $"Elke {CheckIntervalMinutes} minuten"
    };
}

public class SpotNotificationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public NotificationRuleType RuleType { get; set; } = NotificationRuleType.Filter;

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int SpotCount { get; set; }
    public List<SpotSummaryItem> Spots { get; set; } = new List<SpotSummaryItem>();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; } = false;

    [JsonIgnore]
    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - CreatedAtUtc;
            if (diff.TotalMinutes < 1) return "Zojuist";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min geleden";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} uur geleden";
            return CreatedAtUtc.ToLocalTime().ToString("dd-MM HH:mm");
        }
    }
}

public class NotificationConfig
{
    public bool WindowsNotificationsEnabled { get; set; } = true;
    public int AutoSyncIntervalMinutes { get; set; } = 15; // Minimum 5
    public List<NotificationRule> Rules { get; set; } = new List<NotificationRule>();
    public List<SpotNotificationItem> Notifications { get; set; } = new List<SpotNotificationItem>();
}
