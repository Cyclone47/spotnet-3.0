using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Spotnet.Mac.Network;

/// <summary>
/// A spot's full article. Everything the detail panel needs beyond the header lives in
/// the article's <c>X-XML</c> headers, not in its body — that is where the poster's
/// description, the cover image and the NZB segment are. The body carries only a plain
/// text copy of the description.
/// </summary>
public static class SpotArticle
{
    /// <summary>
    /// Splits an article into unfolded headers and body. RFC 5322 folding means a header
    /// continues on any following line that starts with a space or tab.
    /// </summary>
    public static (List<KeyValuePair<string, string>> Headers, string Body) Split(string article)
    {
        var headers = new List<KeyValuePair<string, string>>();

        int split = article.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string headerBlock = split < 0 ? article : article[..split];
        string body = split < 0 ? "" : article[(split + 4)..];

        string name = "";
        var value = new StringBuilder();

        void Flush()
        {
            if (name.Length > 0) headers.Add(new KeyValuePair<string, string>(name, value.ToString()));
            name = "";
            value.Clear();
        }

        foreach (string line in headerBlock.Split("\r\n"))
        {
            if (line.Length == 0) continue;

            if (line[0] is ' ' or '\t')
            {
                value.Append(line.TrimStart());
                continue;
            }

            Flush();
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;

            name = line[..colon];
            value.Append(line[(colon + 1)..].TrimStart());
        }
        Flush();

        return (headers, body);
    }

    /// <summary>
    /// Concatenates every X-XML header into the spot's XML document. Posters split it
    /// over several headers, so order matters and all of them count.
    /// </summary>
    public static string ExtractXml(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var xml = new StringBuilder();
        foreach (var header in headers)
        {
            if (header.Key.Equals("X-XML", StringComparison.OrdinalIgnoreCase))
            {
                xml.Append(header.Value);
            }
        }
        return xml.ToString();
    }

    /// <summary>
    /// The Posting element of a spot. Image and NZB are lists because posters split both
    /// over as many articles as they need — Worker.ParseNzbNode joins every Segment
    /// child, and taking only the first truncates the payload into something unusable.
    /// </summary>
    public sealed record Posting(
        string Description,
        IReadOnlyList<string> ImageSegments,
        string? ImageUrl,
        IReadOnlyList<string> NzbSegments,
        string? Website,
        string? Poster)
    {
        public string? ImageSegment => ImageSegments.Count > 0 ? ImageSegments[0] : null;
        public bool HasNzb => NzbSegments.Count > 0;
    }

    /// <summary>
    /// Reads the Posting element out of a spot's XML. Image is either a URL in the
    /// element's own text or a Segment child naming a second article that holds the
    /// image bytes — Worker.ParseSpotXML makes the same distinction.
    /// </summary>
    public static Posting? ParsePosting(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var posting = doc.Root?.Element("Posting");
            if (posting == null) return null;

            string description = posting.Element("Description")?.Value.Trim() ?? "";

            var imageSegments = new List<string>();
            string? imageUrl = null;
            var image = posting.Element("Image");
            if (image != null)
            {
                imageSegments.AddRange(Segments(image));
                if (imageSegments.Count == 0 && image.Value.Trim().Length > 0)
                {
                    imageUrl = image.Value.Trim();
                }
            }

            var nzbSegments = Segments(posting.Element("NZB")).ToList();

            string? website = posting.Element("Website")?.Value.Trim();
            string? poster = posting.Element("Poster")?.Value.Trim();

            return new Posting(
                description,
                imageSegments,
                imageUrl,
                nzbSegments,
                string.IsNullOrWhiteSpace(website) ? null : website,
                string.IsNullOrWhiteSpace(poster) ? null : poster);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Every Segment child of a node, in document order, without angle brackets.</summary>
    private static IEnumerable<string> Segments(XElement? node)
    {
        if (node == null) yield break;

        foreach (var segment in node.Elements("Segment"))
        {
            string id = segment.Value.Trim().Trim('<', '>').Replace("\"", "", StringComparison.Ordinal);
            if (id.Length > 0) yield return id;
        }
    }

    /// <summary>
    /// Inflates an NZB payload. Unlike the cover image, the NZB is raw-deflate
    /// compressed on top of the escaping — SpotHelper.UnzipStr reads it back through a
    /// DeflateStream as Latin-1. Returns null when the bytes are not deflate data.
    /// </summary>
    public static string? InflateNzb(byte[] payload)
    {
        if (payload.Length == 0) return null;

        try
        {
            using var input = new MemoryStream(payload);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.Latin1);
            string xml = reader.ReadToEnd();
            return xml.Length == 0 ? null : xml;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes a Spotnet binary article body (cover images, NZBs). Spotnet does not use
    /// yEnc here: line breaks are formatting and every byte is literal except four
    /// escapes — SpotHelper.GetBinary does exactly this.
    /// </summary>
    public static byte[] DecodeBinary(string body)
    {
        if (string.IsNullOrEmpty(body)) return Array.Empty<byte>();

        if (body.StartsWith("..", StringComparison.Ordinal))
        {
            body = body[1..];
        }

        string decoded = body
            .Replace("\r\n..", "\r\n.", StringComparison.Ordinal)
            .Replace("\n..", "\n.", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("=C", "\n", StringComparison.Ordinal)
            .Replace("=B", "\r", StringComparison.Ordinal)
            .Replace("=A", "\0", StringComparison.Ordinal)
            .Replace("=D", "=", StringComparison.Ordinal);

        return Encoding.Latin1.GetBytes(decoded);
    }

    /// <summary>
    /// Re-reads a Latin-1 decoded string as UTF-8. The NNTP stream is read byte-for-byte
    /// as Latin-1, so text that was posted as UTF-8 — accents, and the emoji people put
    /// in comments — arrives mangled until it is decoded again. Text that is not valid
    /// UTF-8 is left exactly as it was.
    /// </summary>
    public static string ReinterpretUtf8(string latin1)
    {
        if (string.IsNullOrEmpty(latin1)) return latin1;

        try
        {
            byte[] bytes = Encoding.Latin1.GetBytes(latin1);
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return latin1;
        }
    }
}
