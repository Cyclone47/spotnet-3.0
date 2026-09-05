using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spotnet.Mac.Models;

/// <summary>
/// Spotnet descriptions and comments are written in a small UBB dialect: "[br]" for a
/// line break, "[b]"/"[i]"/"[u]"/"[color=…]" for styling and "[img=name]" for one of the
/// bundled smileys. Windows renders it to HTML; this renders it to plain text, with the
/// smileys mapped onto their Unicode equivalents so a comment still reads the same.
/// </summary>
public static class SpotMarkup
{
    /// <summary>The bundled smiley set (Data/Images/smileys), mapped to emoji.</summary>
    private static readonly Dictionary<string, string> Smileys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["biggrin"] = "😃", ["bloos"] = "😊", ["buigen"] = "🙇", ["censored"] = "🤐",
        ["clown"] = "🤡",  ["confused"] = "😕", ["cool"] = "😎", ["exactly"] = "👌",
        ["frown"] = "🙁",  ["grijns"] = "😁", ["heh"] = "😅",   ["huh"] = "😐",
        ["klappen"] = "👏", ["knipoog"] = "😉", ["kwijl"] = "🤤", ["lollig"] = "😝",
        ["maf"] = "🤪",    ["ogen"] = "👀",   ["oops"] = "😬",  ["pijl"] = "➡️",
        ["redface"] = "😳", ["respekt"] = "🙌", ["schater"] = "😆", ["shiny"] = "✨",
        ["sleephappy"] = "😴", ["smile"] = "🙂", ["uitroepteken"] = "❗",
        ["vlag"] = "🚩",   ["vraagteken"] = "❓", ["wink"] = "😉"
    };

    /// <summary>
    /// The smiley set, in the order the Windows picker lists them, as (tag, emoji)
    /// pairs. Inserting one writes "[img=tag]" into the comment.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> SmileyList { get; } =
        Smileys.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();

    private static readonly Regex SmileyTag = new(@"\[img=(?:&quot;|"")?([a-zA-Z]+)(?:&quot;|"")?\]",
                                                  RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColorTag = new(@"\[/?color(?:=[^\]]*)?\]",
                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleTag = new(@"\[/?(?:b|i|u|quote|spot)\]",
                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlTag = new(@"\[url=[^\]]*\]|\[/url\]",
                                               RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ToPlainText(string? markup)
    {
        if (string.IsNullOrEmpty(markup)) return "";

        string text = markup
            .Replace("[br]", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("[aa]", "&#", StringComparison.OrdinalIgnoreCase);

        text = SmileyTag.Replace(text, m =>
            Smileys.TryGetValue(m.Groups[1].Value, out var emoji) ? emoji : m.Value);

        text = ColorTag.Replace(text, "");
        text = StyleTag.Replace(text, "");
        text = UrlTag.Replace(text, "");

        return text.Trim();
    }
}
