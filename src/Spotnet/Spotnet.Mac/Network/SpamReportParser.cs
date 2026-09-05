using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.Network;

/// <summary>
/// Parses NNTP overview headers from the ReportGroup (e.g. free.willey) into <see cref="SpamReportItem"/>.
/// Mirrors Windows Spotnet.Model.SpamReports.ParseSpamReports.
/// </summary>
public static class SpamReportParser
{
    private static readonly Regex ModulusRegex = new(@"([a-zA-Z0-9]+)\.([a-zA-Z0-9\-]+)\.([a-zA-Z0-9\-]+)", RegexOptions.Compiled);

    /// <summary>
    /// Parses an NNTP overview line (OVER / XOVER) from the report group into a SpamReportItem.
    /// Returns null if the line is not a valid Spotnet REPORT post.
    /// </summary>
    public static SpamReportItem? ParseOverviewLine(string overviewLine)
    {
        if (string.IsNullOrWhiteSpace(overviewLine)) return null;

        var parts = overviewLine.Split('\t');
        if (parts.Length < 5) return null;

        if (!long.TryParse(parts[0], out long articleNum))
        {
            return null;
        }

        string subject = parts[1].Trim();
        int reportIdx = subject.IndexOf("REPORT", StringComparison.OrdinalIgnoreCase);
        if (reportIdx < 0) return null;

        int open = subject.IndexOf('<', reportIdx);
        int close = subject.IndexOf('>', open >= 0 ? open : reportIdx);
        if (open < 0 || close <= open) return null;

        string targetMsgId = subject[(open + 1)..close].Trim();
        if (string.IsNullOrEmpty(targetMsgId)) return null;

        string from = parts[2].Trim();
        string sender = "";
        string modulus = "";

        int fromOpen = from.IndexOf('<', StringComparison.Ordinal);
        int fromClose = from.IndexOf('>', StringComparison.Ordinal);
        if (fromOpen > 0)
        {
            sender = from[..fromOpen].Trim();
        }
        else if (fromOpen < 0)
        {
            sender = from.Trim();
        }

        if (fromOpen >= 0 && fromClose > fromOpen)
        {
            string rawKey = from.Substring(fromOpen + 1, fromClose - fromOpen - 1).Trim();
            var match = ModulusRegex.Match(rawKey);
            if (match.Success && match.Groups[1].Length == 10)
            {
                modulus = PosterIdentity.Unescape(match.Groups[2].Value);
            }
            else
            {
                int at = rawKey.IndexOf('@', StringComparison.Ordinal);
                string cred = at > 0 ? rawKey[..at] : rawKey;
                int dot = cred.IndexOf('.', StringComparison.Ordinal);
                if (dot > 0) cred = cred[..dot];
                modulus = PosterIdentity.Unescape(cred);
            }
        }

        if (string.IsNullOrEmpty(modulus))
        {
            modulus = PosterIdentity.ModulusFromSender(from);
        }

        long date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string dateStr = parts[3].Trim().Replace("(", "").Replace(")", "")
                                         .Replace("UTC", "").Replace("CET", "").Replace("CEST", "").Trim();
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            date = dto.ToUnixTimeSeconds();
        }
        else if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = new DateTimeOffset(dt).ToUnixTimeSeconds();
        }

        string reportMsgId = parts[4].Trim().Trim('<', '>');

        return new SpamReportItem
        {
            RowId = articleNum,
            MsgId = targetMsgId,
            Modulus = modulus,
            Date = date,
            ReportMsgId = reportMsgId,
            Sender = sender
        };
    }
}
