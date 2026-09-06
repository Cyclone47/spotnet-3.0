using System.Collections.Generic;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Notifications;

namespace Spotnet.Mac.Platform;

/// <summary>
/// Bridges the shared notification engine to the Mac SQLite database —
/// the counterpart of Windows' NotificationHost, which binds the same engine
/// to its own SQLite layer.
/// </summary>
public sealed class MacNotificationSpotQuery : INotificationSpotQuery
{
    private readonly SpotDatabaseService _dbService;

    public MacNotificationSpotQuery(SpotDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public Task<long> GetMaxSpotRowIdAsync() => _dbService.GetMaxSpotRowIdAsync();

    public Task<List<SpotSummaryItem>> QuerySpotsForRuleAsync(NotificationRule rule, long sinceRowId, int limit)
        => _dbService.QuerySpotsForNotificationRuleAsync(rule, sinceRowId, limit);
}
