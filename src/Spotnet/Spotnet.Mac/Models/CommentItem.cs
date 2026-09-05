using System;

namespace Spotnet.Mac.Models;

public sealed class CommentItem
{
    public long Id { get; set; }
    public string MsgId { get; set; } = string.Empty;
    public long Date { get; set; }
    public string Sender { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string SpotMsgId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>The comment as it reads on screen: UBB resolved, smileys as emoji.</summary>
    public string DisplayBody => SpotMarkup.ToPlainText(Body);

    /// <summary>Poster's RSA modulus from the article's X-User-Key header, if signed.</summary>
    public string Modulus { get; set; } = string.Empty;

    public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Date).LocalDateTime;

    /// <summary>Windows writes comment dates as "3 sep 2026 12:20".</summary>
    public string FormattedDate => DateTime.ToString("d MMM yyyy HH:mm", new System.Globalization.CultureInfo("nl-NL"));

    /// <summary>Sender as Windows shows it above a comment: "pzh (RtUpBA)".</summary>
    public string SenderWithId
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Sender)) return "";
            string id = PosterIdentity.MakeUnique(Modulus);
            return string.IsNullOrEmpty(Modulus) ? Sender : $"{Sender} ({id})";
        }
    }
}

/// <summary>One labelled row in the spot detail panel ("Bron" — "CD").</summary>
public sealed record SpotDetailField(string Label, string Value);
