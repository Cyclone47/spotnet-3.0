using System;

namespace Spotnet.Mac.Models;

/// <summary>
/// Represents a community spam report filed against a spot.
/// Mirrors Windows Spotnet.Model.SpamReport and the spamreports table.
/// </summary>
public sealed class SpamReportItem
{
    public long RowId { get; set; }
    public string MsgId { get; set; } = string.Empty;
    public string Modulus { get; set; } = string.Empty;
    public long Date { get; set; }
    public string ReportMsgId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;

    public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Date).LocalDateTime;
    public string FormattedDate => DateTime.ToString("dd-MM-yyyy HH:mm");
}
