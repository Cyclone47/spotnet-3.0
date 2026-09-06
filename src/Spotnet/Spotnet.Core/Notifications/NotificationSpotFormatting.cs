using System;
using System.Globalization;

namespace Spotnet.Notifications;

/// <summary>
/// The spot-text formatters the notification engine needs, ported from the
/// RemoteCatalogService helpers the Windows client uses for the same data.
/// </summary>
public static class NotificationSpotFormatting
{
    public static string GetCategoryName(int category)
    {
        return category switch
        {
            1 => "Films",
            2 => "Muziek",
            3 => "Spellen",
            4 => "Applicaties",
            5 => "Boeken",
            6 => "Series",
            9 => "Erotiek",
            _ => "Overig"
        };
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("F1", CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("F1", CultureInfo.InvariantCulture) + " MB";
        double gb = mb / 1024.0;
        return gb.ToString("F2", CultureInfo.InvariantCulture) + " GB";
    }

    public static string FormatDate(long unixEpoch)
    {
        try
        {
            DateTime dt = DateTimeOffset.FromUnixTimeSeconds(unixEpoch).LocalDateTime;
            DateTime now = DateTime.Now;
            if (dt.Date == now.Date)
            {
                return $"Vandaag ({dt:HH:mm})";
            }
            if (dt.Date == now.Date.AddDays(-1))
            {
                return $"Gisteren ({dt:HH:mm})";
            }
            return dt.ToString("dd-MM-yyyy HH:mm");
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Rewrites a filter expression from the bundled FiltersAdvanced XML into the
    /// plain SQL the engine's WHERE clause accepts — the same rewrite the Remote
    /// catalog does before querying.
    /// </summary>
    public static string CleanFilterQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "";
        string clean = query.Trim();
        // Rewrite legacy FTS docid -> rowid
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\bdocid\b", "rowid", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Replace special tags
        clean = clean.Replace("[SN:NEW]", "0");
        clean = clean.Replace("[SN:DATE]", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        clean = clean.Replace("spots.msgid in favorieten", "spots.cats LIKE '%f1%'");
        clean = clean.Replace("cat = 1 AND cats MATCH '1b4 OR 1d11'", "cat = 6");
        return clean;
    }
}
