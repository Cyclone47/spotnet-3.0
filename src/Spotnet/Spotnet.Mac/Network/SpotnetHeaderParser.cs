using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Models;
using SpotnetEnc;

namespace Spotnet.Mac.Network;

public static class SpotnetHeaderParser
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly SpotnetDecoder Decoder = new();

    /// <summary>Decodes a yEnc article body to text (UTF-8).</summary>
    public static string DecodeYEncText(string rawBody)
    {
        byte[]? bytes = DecodeYEncBytes(rawBody);
        return bytes == null ? rawBody : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Decodes a yEnc-encoded article body to its raw bytes — used for the cover image,
    /// which is a JPEG rather than text.
    /// </summary>
    public static byte[]? DecodeYEncBytes(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody) || !rawBody.Contains("=ybegin", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            byte[] raw = Encoding.Latin1.GetBytes(rawBody);
            byte[] decoded = new byte[raw.Length];
            uint written = Decoder.Decode(raw, decoded, 0, (uint)raw.Length);
            if (written == 0) return null;

            var result = new byte[written];
            Array.Copy(decoded, result, (int)written);
            return result;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "yEnc image decoding failed: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses a Spotnet XML body from an article body (handling yEnc if present).
    /// </summary>
    public static (string title, string description, string? imageBase64, string? nzbSegment) ParseSpotBody(string bodyText)
    {
        string title = "";
        string description = "";
        string? imageBase64 = null;
        string? nzbSegment = null;

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return (title, description, imageBase64, nzbSegment);
        }

        string xmlContent = bodyText;

        // If body is yEnc encoded, decode it
        if (bodyText.Contains("=ybegin"))
        {
            try
            {
                byte[] raw = Encoding.Latin1.GetBytes(bodyText);
                byte[] decoded = new byte[raw.Length];
                uint written = Decoder.Decode(raw, decoded, 0, (uint)raw.Length);
                if (written > 0)
                {
                    xmlContent = Encoding.UTF8.GetString(decoded, 0, (int)written);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "yEnc decoding failed, attempting raw XML parse.");
            }
        }

        // Search for <Spot>...</Spot>
        int spotStart = xmlContent.IndexOf("<Spot", StringComparison.OrdinalIgnoreCase);
        int spotEnd = xmlContent.IndexOf("</Spot>", StringComparison.OrdinalIgnoreCase);

        if (spotStart >= 0 && spotEnd > spotStart)
        {
            string spotXml = xmlContent.Substring(spotStart, spotEnd + 7 - spotStart);
            try
            {
                var doc = XDocument.Parse(spotXml);
                var root = doc.Root;
                if (root != null)
                {
                    title = root.Element("Title")?.Value ?? "";
                    description = root.Element("Description")?.Value ?? "";
                    imageBase64 = root.Element("Image")?.Element("Segment")?.Value;
                    nzbSegment = root.Element("NZB")?.Element("Segment")?.Value;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to parse Spot XML element: {0}", ex.Message);
            }
        }

        if (string.IsNullOrEmpty(description))
        {
            description = xmlContent;
        }

        return (title, description, imageBase64, nzbSegment);
    }

    /// <summary>
    /// Parses an NNTP overview header into a <see cref="SpotItem"/>.
    ///
    /// A Spotnet header carries everything in the From address, not in the Subject:
    ///
    ///     Poster &lt;BASE64KEY@CATS.FILESIZE.?.STAMP.?.TAG.HASH&gt;
    ///
    /// CATS is "&lt;category digit&gt;&lt;key id digit&gt;&lt;subcategory tokens&gt;", e.g.
    /// "17a09b03b11c06z00" = category 1, key 7, subcats a09 b03 b11 c06 z00. STAMP is the
    /// post time as a unix timestamp — the Date header is the server's, not the spot's.
    ///
    /// This mirrors the Windows client's Worker/SpotSaver pair, including the shape of
    /// the <c>cats</c> column ("1 1a9 1b3 1b11 1c6 1z0"), which every bundled
    /// <c>cats MATCH</c> filter depends on. Headers that do not fit the format fall back
    /// to the Subject/Date headers so nothing is dropped outright.
    /// </summary>
    public static SpotItem ParseHeader(string subject, string from, string dateStr, string messageId, long bytes = 0)
    {
        var spot = new SpotItem
        {
            Subject = subject.Trim(),
            Sender = from.Trim(),
            MsgId = messageId.Trim().Trim('<', '>'),
            Filesize = bytes,
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        if (DateTimeOffset.TryParse(dateStr, out var dto))
        {
            spot.Date = dto.ToUnixTimeSeconds();
        }

        if (!TryParseSpotnetFrom(from, spot))
        {
            // Not a Spotnet-format From header. Keep the older subject-tag heuristic so
            // hand-made and test articles still land in a category.
            var match = Regex.Match(subject, @"^\[?([0-9])([a-z0-9]*)\]?\s*(.*)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var catId))
            {
                spot.Category = catId;
                spot.Cats = match.Groups[1].Value + match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(match.Groups[3].Value))
                {
                    spot.Subject = match.Groups[3].Value.Trim();
                }
            }
            else
            {
                spot.Category = 1; // Default to Beeld if unspecified
            }
        }

        // "Title|Tag" is how posters attach a tag to a spot.
        int bar = spot.Subject.IndexOf('|');
        if (bar >= 0)
        {
            spot.Tag = spot.Subject[(bar + 1)..].Trim();
            spot.Subject = spot.Subject[..bar].Trim();
        }

        return spot;
    }

    /// <summary>
    /// Fills category, subcategories, size, tag and timestamp from a Spotnet From
    /// header. Returns false — leaving <paramref name="spot"/> untouched — when the
    /// header is not in that format.
    /// </summary>
    private static bool TryParseSpotnetFrom(string from, SpotItem spot)
    {
        if (string.IsNullOrWhiteSpace(from)) return false;

        int at = from.IndexOf('@', StringComparison.Ordinal);
        int open = from.IndexOf('<', StringComparison.Ordinal);
        int close = from.IndexOf('>', StringComparison.Ordinal);
        if (at < 1 || open < 1 || open > at || close < at) return false;

        // Between "<" and "@" sits "<modulus>.<signature>", both in Spotnet's URL-safe
        // base64. The modulus is what the poster's short id is derived from.
        string credentials = from.Substring(open + 1, at - open - 1);
        if (credentials.Length > 50)
        {
            int dot = credentials.IndexOf('.', StringComparison.Ordinal);
            spot.Modulus = PosterIdentity.Unescape(dot < 0 ? credentials : credentials[..dot]);
        }

        string[] fields = from.Substring(at + 1, close - at - 1).Split('.');
        if (fields.Length < 7 || fields[0].Length < 2) return false;

        // fields[0] = category + key id + subcategory tokens
        if (!int.TryParse(fields[0].AsSpan(0, 1), out int category) || category < 1) return false;
        if (!byte.TryParse(fields[0].AsSpan(1, 1), out byte keyId) || keyId < 1) return false;

        string? subCats = ParseSubCats(fields[0][2..].ToLowerInvariant(), keyId, out int subCat);
        if (subCats == null) return false;

        // fields[3] is the spot's own timestamp; the Date header is the server's clock.
        if (!long.TryParse(fields[3], out long stamp) || stamp < 1218171600) return false;

        if (long.TryParse(fields[1], out long filesize) && filesize > 0)
        {
            spot.Filesize = filesize;
        }

        // Category 1 covers TV, erotica and e-books until the subcategories say otherwise.
        if (category == 1)
        {
            if (IsTv(subCats)) category = 6;
            else if (IsEro(subCats)) category = 9;
            else if (IsEbook(subCats)) category = 5;
        }

        spot.Key = keyId;
        spot.Category = category;
        spot.Subcat = category * 100 + subCat;
        spot.Date = stamp;
        spot.Cats = FormatCatsColumn(category, subCats);
        spot.Extcat = category * 100 + SpotCategories.PickGenreCode(category, spot.Cats);
        spot.Tag = fields[5].Trim();

        return true;
    }

    /// <summary>
    /// Turns the subcategory run ("a09b03b11z00") into the Windows client's pipe form
    /// ("a9|b3|b11|z0|"), stripping the leading zeros the header pads with. Key 1 uses
    /// variable-width tokens; every later key uses fixed three-character ones.
    /// Returns null when the run cannot be read.
    /// </summary>
    private static string? ParseSubCats(string run, byte keyId, out int subCat)
    {
        subCat = 100;
        if (run.Length < 3) return null;

        var tokens = new System.Collections.Generic.List<string>();
        if (keyId == 1)
        {
            string current = "";
            foreach (char c in run)
            {
                if (!char.IsDigit(c) && current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }
                current += c;
            }
            if (current.Length > 0) tokens.Add(current);
        }
        else
        {
            if (run.Length % 3 != 0) return null;
            for (int i = 0; i < run.Length; i += 3)
            {
                tokens.Add(run.Substring(i, 3));
            }
        }

        var builder = new StringBuilder();
        foreach (string token in tokens)
        {
            char letter = token[0];
            if (letter < 'a' || letter > 'z') continue;
            if (!byte.TryParse(token.AsSpan(1), out byte value)) continue;

            builder.Append(letter).Append(value).Append('|');
            if (letter == 'a' && subCat == 100) subCat = value;
        }

        return builder.Length >= 3 ? builder.ToString() : null;
    }

    /// <summary>
    /// Builds the <c>cats</c> column the FTS filters match against: the bare category
    /// followed by every subcategory prefixed with it, e.g. "1 1a9 1b3 1z0".
    /// </summary>
    private static string FormatCatsColumn(int category, string subCats)
    {
        if (string.IsNullOrWhiteSpace(subCats)) return category.ToString();

        var builder = new StringBuilder().Append(category);
        foreach (string token in subCats.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(' ').Append(category).Append(token);
        }
        return builder.ToString();
    }

    // Category-1 spots that are really something else. Ported from SpotHelper.
    private static bool IsTv(string subCats)
        => subCats.Contains("b4|", StringComparison.Ordinal)
        || subCats.Contains("d11|", StringComparison.Ordinal)
        || subCats.Contains("z1|", StringComparison.Ordinal);

    private static bool IsEbook(string subCats)
        => subCats.Contains("a5|", StringComparison.Ordinal)
        || subCats.Contains("z2|", StringComparison.Ordinal);

    private static bool IsEro(string subCats)
        => subCats.Contains("d23|", StringComparison.Ordinal)
        || subCats.Contains("d24|", StringComparison.Ordinal)
        || subCats.Contains("d25|", StringComparison.Ordinal)
        || subCats.Contains("d26|", StringComparison.Ordinal)
        || subCats.Contains("d72|", StringComparison.Ordinal)
        || subCats.Contains("d73|", StringComparison.Ordinal)
        || subCats.Contains("d74|", StringComparison.Ordinal)
        || subCats.Contains("d75|", StringComparison.Ordinal)
        || subCats.Contains("z3|", StringComparison.Ordinal);

    /// <summary>
    /// Parses an NNTP XOVER/OVER tab-delimited overview line into a SpotItem.
    /// </summary>
    public static SpotItem? ParseOverviewLine(string overviewLine, out long articleNumber)
    {
        articleNumber = 0;
        if (string.IsNullOrWhiteSpace(overviewLine)) return null;

        var parts = overviewLine.Split('\t');
        if (parts.Length < 5) return null;

        if (!long.TryParse(parts[0], out articleNumber))
        {
            return null;
        }

        string subject = parts[1];
        string from = parts[2];
        string date = parts[3];
        string msgId = parts[4];
        long bytes = 0;
        if (parts.Length > 6)
        {
            long.TryParse(parts[6], out bytes);
        }

        var spot = ParseHeader(subject, from, date, msgId, bytes);

        // Only headers that actually parsed as Spotnet spots belong in the database.
        // free.pt also carries moderation ("delete <id>@spot.net") and update-only
        // posts, which Windows drops the same way; Key is set only on the Spotnet path.
        return spot.Key > 0 ? spot : null;
    }
}
