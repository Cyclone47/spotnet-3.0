using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spotnet.Downloader;
using Spotnet.Notifications;
using Xunit;

namespace Spotnet.Tests;

public sealed class NotificationHistoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SpotnetHistory-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(root, "notifications_config.json");

    public NotificationHistoryTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(NotificationHistoryType.DownloadFinished)]
    [InlineData(NotificationHistoryType.DownloadFailed)]
    [InlineData(NotificationHistoryType.DownloadPasswordRequired)]
    [InlineData(NotificationHistoryType.DownloadWarning)]
    [InlineData(NotificationHistoryType.Filter)]
    [InlineData(NotificationHistoryType.Keyword)]
    public void DisablingCategoryFiltersExistingHistoryAndStopsNewEntriesWithoutDeletingHistory(NotificationHistoryType type)
    {
        var manager = new NotificationManager(ConfigPath);
        foreach (var category in Enum.GetValues<NotificationHistoryType>())
            manager.AddNotification(new SpotNotificationItem { HistoryType = category });
        Assert.Equal(6, manager.UnreadCount);

        int changes = 0;
        manager.UnreadCountChanged += () => changes++;
        manager.SetHistoryEnabled(type, false);
        Assert.Equal(1, changes);
        Assert.Equal(5, manager.UnreadCount);
        Assert.Equal(5, manager.GetHistory().Count);
        Assert.DoesNotContain(manager.GetHistory(), n => n.EffectiveHistoryType == type);
        manager.AddNotification(new SpotNotificationItem { HistoryType = type });
        Assert.Equal(6, manager.Config.Notifications.Count);
        Assert.Equal(1, changes);

        var reopened = new NotificationManager(ConfigPath);
        Assert.Equal(5, reopened.UnreadCount);
        Assert.True(reopened.Config.WindowsNotificationsEnabled);
        reopened.SetHistoryEnabled(type, true);
        Assert.Equal(6, reopened.UnreadCount);
        reopened.AddNotification(new SpotNotificationItem { HistoryType = type });
        Assert.Equal(7, reopened.GetHistory().Count);
    }

    [Theory]
    [InlineData(DownloadStatus.Success, NotificationHistoryType.DownloadFinished)]
    [InlineData(DownloadStatus.Failure, NotificationHistoryType.DownloadFailed)]
    [InlineData(DownloadStatus.FailureNoSuchArticle, NotificationHistoryType.DownloadFailed)]
    [InlineData(DownloadStatus.Warning, NotificationHistoryType.DownloadWarning)]
    [InlineData(DownloadStatus.WrongPassword, NotificationHistoryType.DownloadPasswordRequired)]
    public void DownloadEventsUseTheirOwnCategory(DownloadStatus status, NotificationHistoryType type)
    {
        var manager = new NotificationManager(ConfigPath);
        manager.NotifyDownloadStatus("Example download", status);
        Assert.Equal(type, Assert.Single(manager.GetHistory()).EffectiveHistoryType);
        manager.SetHistoryEnabled(type, false);
        manager.NotifyDownloadStatus("Another download", status);
        Assert.Single(manager.Config.Notifications);
        Assert.Equal(0, manager.UnreadCount);
    }

    [Theory]
    [InlineData(DownloadStatus.Deleted)]
    [InlineData(DownloadStatus.Downloading)]
    [InlineData(DownloadStatus.Paused)]
    public void OrdinaryQueueStatesDoNotProduceHistory(DownloadStatus status)
    {
        var manager = new NotificationManager(ConfigPath);
        manager.NotifyDownloadStatus("Example", status);
        Assert.Empty(manager.GetHistory());
    }

    [Fact]
    public void ExistingConfigKeepsAllTypesEnabledAndRecognizesOldDownloadTitles()
    {
        var config = JsonSerializer.Deserialize<NotificationConfig>("{}");
        Assert.All(Enum.GetValues<NotificationHistoryType>(), type =>
            Assert.True(config.IncludesInHistory(new SpotNotificationItem { HistoryType = type })));
        foreach (string title in new[] { "Download finished with problems", "Download voltooid met problemen" })
            Assert.Equal(NotificationHistoryType.DownloadFailed,
                new SpotNotificationItem { RuleType = NotificationRuleType.Download, Title = title }.EffectiveHistoryType);
        Assert.Equal(NotificationHistoryType.Keyword,
            new SpotNotificationItem { RuleType = NotificationRuleType.Keyword }.EffectiveHistoryType);
    }
}
