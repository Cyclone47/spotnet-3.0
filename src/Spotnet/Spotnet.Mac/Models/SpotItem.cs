using System;

namespace Spotnet.Mac.Models;

public sealed class SpotItem
{
    public long Id { get; set; }
    public int Key { get; set; }
    public int Category { get; set; }
    public int Subcat { get; set; }
    public int Extcat { get; set; }
    public long Date { get; set; }
    public long Filesize { get; set; }
    public string Cats { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string MsgId { get; set; } = string.Empty;
    public string Modulus { get; set; } = string.Empty;

    // Computed display properties
    public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Date).LocalDateTime;
    public string FormattedDate => DateTime.ToString("dd-MM-yyyy HH:mm");

    /// <summary>
    /// The "Leeftijd" column, formatted exactly as the Windows client formats it
    /// (Spotnet.Extensions.DateTimeExtension.ToAge): "vandaag (HH:mm)",
    /// "gisteren (HH:mm)", a weekday name for the rest of the past week, and
    /// "N dagen (HH:mm)" beyond that.
    /// </summary>
    public string Age => FormatAge(DateTime, System.DateTime.Now);

    private static readonly string[] DutchDayNames =
    {
        "zondag", "maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag"
    };

    public static string FormatAge(DateTime stamp, DateTime now)
    {
        if (stamp < new DateTime(2000, 1, 1))
        {
            return string.Empty;
        }

        DateTime midnight = now.Date;
        int days = (int)(midnight - stamp.Date).TotalDays;
        double seconds = (stamp - midnight).TotalSeconds;

        if (days < 7)
        {
            if (seconds > 0)
            {
                return $"vandaag ({stamp:HH:mm})";
            }
            if (seconds > -86400)
            {
                return $"gisteren ({stamp:HH:mm})";
            }
            return $"{DutchDayNames[(int)stamp.DayOfWeek]} ({stamp:HH:mm})";
        }

        // Windows reports the day count one higher here, so a spot that is 7 whole
        // days old reads "8 dagen". Kept identical rather than corrected.
        return $"{days + 1} dagen ({stamp:HH:mm})";
    }

    /// <summary>
    /// The "Afzender" column: the poster's display name only, without the
    /// "&lt;key@spot.net&gt;" identity that follows it in the raw From header.
    /// Matches Windows, which shows Spot.Poster run through
    /// AppHelper.StripNonAlphaNumericCharacters.
    /// </summary>
    /// <summary>
    /// The short poster id Windows shows in parentheses after the sender, e.g. "5I54zQ".
    /// </summary>
    public string PosterId => PosterIdentity.MakeUnique(
        Modulus.Length > 0 ? Modulus : PosterIdentity.ModulusFromSender(Sender));

    /// <summary>
    /// Sender as the spot detail panel shows it on Windows: "Paaldanser (5I54zQ)".
    /// </summary>
    public string SenderWithId
    {
        get
        {
            string name = SenderName;
            if (name.Length == 0) return "";
            string id = PosterId;
            return id.Length == 0 ? name : $"{name} ({id})";
        }
    }

    public string SenderName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Sender)) return string.Empty;

            string poster = Sender;
            int bracket = poster.IndexOf('<');
            if (bracket >= 0) poster = poster.Substring(0, bracket);

            return System.Text.RegularExpressions.Regex.Replace(poster, "[^A-Za-z0-9]", "").Trim();
        }
    }

    public string CategoryName => Category switch
    {
        1 => "Beeld",
        2 => "Geluid",
        3 => "Spellen",
        4 => "Applicaties",
        9 => "Erotiek",
        _ => "Overig"
    };

    public string CategoryIcon => Category switch
    {
        1 => "🎬",
        2 => "🎵",
        3 => "🎮",
        4 => "💻",
        9 => "🔞",
        _ => "📄"
    };

    /// <summary>
    /// The "Formaat" column: the short name of the spot's a-subcategory — x264, DivX,
    /// Bluray, MP3, WAV, Win, Android … Windows leaves this blank when the spot carries
    /// no a-subcategory, and so does this.
    /// </summary>
    public string FormatLabel => SpotCategories.FormatFromSubcat(Subcat);

    /// <summary>
    /// The "Genre" column: the spot's first named genre subcategory — Televisie,
    /// Waargebeurd, Komedie, Systeem, Kantoor … Blank when the spot has none.
    /// </summary>
    public string GenreLabel => SpotCategories.GenreFromCats(Category, Cats);

    /// <summary>
    /// The "Omvang" column, formatted the way the Windows client formats it
    /// (AppHelper.ConvertSize): whole bytes and KB, one decimal for MB, GB and TB, and
    /// Dutch number formatting, so 3382278521 reads "3,1 GB" and 512906 reads "501 KB".
    /// </summary>
    public string FormattedSize => FormatSize(Filesize);

    private static readonly System.Globalization.CultureInfo DutchCulture = new("nl-NL");

    public static string FormatSize(long size)
    {
        const long kb = 1024L;
        const long mb = 1048576L;
        const long gb = 1073741824L;
        const long tb = 1099511627776L;
        const long pb = 1125899906842624L;

        if (size < 0) return "";
        if (size < kb) return Math.Round((double)size).ToString(DutchCulture) + " bytes";
        if (size < mb) return Math.Round((double)size / kb).ToString(DutchCulture) + " KB";
        if (size < gb) return Math.Round((double)size / mb, 1).ToString(DutchCulture) + " MB";
        if (size < tb) return Math.Round((double)size / gb, 1).ToString(DutchCulture) + " GB";
        if (size < pb) return Math.Round((double)size / tb, 1).ToString(DutchCulture) + " TB";
        return "";
    }
}
