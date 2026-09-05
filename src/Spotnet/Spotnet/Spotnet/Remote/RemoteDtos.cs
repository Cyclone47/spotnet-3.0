using System;
using System.Collections.Generic;

namespace Spotnet.Remote;

public class SpotDto
{
    public long Id { get; set; }
    public string MessageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Tag { get; set; } = "";
    public int Category { get; set; }
    public string CategoryName { get; set; } = "";
    public long FileSize { get; set; }
    public string FormattedSize { get; set; } = "";
    public long Date { get; set; }
    public string FormattedDate { get; set; } = "";
    public int SpamReports { get; set; }
    public bool IsFavorite { get; set; }
}

public class SpotDetailDto : SpotDto
{
    public string Description { get; set; } = "";
    public bool HasImage { get; set; }
    public bool HasNzb { get; set; }
    public string NntpGroup { get; set; } = "";
}

public class DownloadItemDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public long SpeedBytesPerSec { get; set; }
    public string SpeedFormatted { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public string TotalSizeFormatted { get; set; } = "";
    public string EtaFormatted { get; set; } = "";
    public bool IsPaused { get; set; }
    public bool IsComplete { get; set; }
    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
}

public class QueueStatusDto
{
    public bool IsPaused { get; set; }
    public string OverallSpeedFormatted { get; set; } = "";
    public long OverallSpeedBytesPerSec { get; set; }
    public double OverallProgress { get; set; }
    public string RemainingSizeFormatted { get; set; } = "";
    public int ActiveCount { get; set; }
    public List<DownloadItemDto> Items { get; set; } = new List<DownloadItemDto>();
}

public class ServerStatusDto
{
    public string Version { get; set; } = "3.0";
    public bool IsReady { get; set; }
    public string CurrentProvider { get; set; } = "";
    public long TotalSpotsInDb { get; set; }
    public int QueueCount { get; set; }
    public long DownloadSpeed { get; set; }
    public string DownloadSpeedFormatted { get; set; } = "";
    public int PairedDevicesCount { get; set; }
    public int Port { get; set; }
    public bool LanEnabled { get; set; }
    public bool IsSyncing { get; set; }
    public string DefaultNickname { get; set; } = "";
    public bool RequireAuth { get; set; }
    public bool HasPasswordAuth { get; set; }
}

public class LoginRequestDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string DeviceName { get; set; } = "Mobiel Apparaat";
}

public class LoginResponseDto
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
}

public class PairRequestDto
{
    public string Pin { get; set; } = "";
    public string Token { get; set; } = "";
    public string DeviceName { get; set; } = "Mobiel Apparaat";
}

public class PairResponseDto
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string DeviceId { get; set; } = "";
}

public class FilterDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<FilterDto> Children { get; set; } = new List<FilterDto>();
}

public class SpotCommentDto
{
    public long Id { get; set; }
    public string SpotMessageId { get; set; } = "";
    public string Sender { get; set; } = "";
    public string DateFormatted { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string RawBody { get; set; } = "";
    public string Avatar { get; set; } = "";
    public bool IsAuthor { get; set; }
    public bool IsVerified { get; set; }
}

public class PostCommentRequestDto
{
    public string Nickname { get; set; } = "";
    public string Body { get; set; } = "";
}

public class SyncStatusDto
{
    public bool Success { get; set; }
    public bool IsSyncing { get; set; }
    public string Message { get; set; } = "";
}

public class NotificationSpotDto
{
    public long Id { get; set; }
    public string MessageId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Category { get; set; }
    public string CategoryName { get; set; } = "";
    public string FormattedSize { get; set; } = "";
    public string FormattedDate { get; set; } = "";
}

public class NotificationItemDto
{
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string RuleType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int SpotCount { get; set; }
    public string TimeAgo { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
    public List<NotificationSpotDto> Spots { get; set; } = new List<NotificationSpotDto>();
}

public class NotificationsResponseDto
{
    public int UnreadCount { get; set; }
    public List<NotificationItemDto> Notifications { get; set; } = new List<NotificationItemDto>();
}

