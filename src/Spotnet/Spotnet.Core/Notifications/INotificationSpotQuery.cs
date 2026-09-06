using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Spotnet.Notifications;

/// <summary>
/// The pieces of the spots database the notification engine needs. The Windows
/// client binds it to its System.Data.SQLite engine, the macOS client to its
/// Microsoft.Data.Sqlite one — the engine itself stays free of both.
/// </summary>
public interface INotificationSpotQuery
{
    /// <summary>Highest rowid in the spots table (0 when the table is empty or unreadable).</summary>
    Task<long> GetMaxSpotRowIdAsync();

    /// <summary>
    /// Spots matching <paramref name="rule"/> with a rowid above <paramref name="sinceRowId"/>,
    /// oldest first, at most <paramref name="limit"/> rows. Returns an empty list on any failure.
    /// </summary>
    Task<List<SpotSummaryItem>> QuerySpotsForRuleAsync(NotificationRule rule, long sinceRowId, int limit);
}

/// <summary>
/// The desktop-toast side of the engine. One implementation per platform:
/// the Windows balloon (NotificationHelper) and the macOS Notification Center
/// banner (MacNotificationService).
/// </summary>
public interface ISpotnetNotifier
{
    /// <summary>Shows a desktop toast. Returns false when the platform refused it.</summary>
    bool Show(string title, string body);
}
