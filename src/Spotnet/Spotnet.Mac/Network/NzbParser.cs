using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Spotnet.Mac.Network;

/// <summary>One segment of one file in an NZB.</summary>
public sealed record NzbSegment(int Number, string MessageId, long Bytes);

/// <summary>One file entry in an NZB (typically one RAR volume or a PAR2 block).</summary>
public sealed record NzbFile(
    string Subject,
    string Poster,
    string Group,
    IReadOnlyList<NzbSegment> Segments);

/// <summary>
/// Parses Newzbin-format NZB documents into a flat object graph.
/// Handles both the bare namespace and the fully-qualified
/// "http://www.newzbin.com/DTD/2003/nzb" namespace that some posters use.
/// </summary>
public static class NzbParser
{
    private static readonly XNamespace NzbNs = "http://www.newzbin.com/DTD/2003/nzb";

    /// <summary>Parses NZB XML from a string. Returns an empty list on any error.</summary>
    public static IReadOnlyList<NzbFile> Parse(string nzbXml)
    {
        if (string.IsNullOrWhiteSpace(nzbXml)) return Array.Empty<NzbFile>();

        try
        {
            var doc = XDocument.Parse(nzbXml);
            return ParseDoc(doc);
        }
        catch
        {
            return Array.Empty<NzbFile>();
        }
    }

    /// <summary>Parses NZB XML from a file. Returns an empty list on any error.</summary>
    public static IReadOnlyList<NzbFile> ParseFile(string path)
    {
        if (!File.Exists(path)) return Array.Empty<NzbFile>();

        try
        {
            var doc = XDocument.Load(path);
            return ParseDoc(doc);
        }
        catch
        {
            return Array.Empty<NzbFile>();
        }
    }

    private static IReadOnlyList<NzbFile> ParseDoc(XDocument doc)
    {
        var root = doc.Root;
        if (root == null) return Array.Empty<NzbFile>();

        // Support both namespaced and plain elements
        var ns = root.Name.Namespace;

        var files = new List<NzbFile>();
        foreach (var fileEl in root.Elements(ns + "file"))
        {
            string subject = (string?)fileEl.Attribute("subject") ?? "";
            string poster  = (string?)fileEl.Attribute("poster")  ?? "";

            // First newsgroup listed
            string group = "";
            var groupsEl = fileEl.Element(ns + "groups");
            if (groupsEl != null)
            {
                var firstGroup = groupsEl.Element(ns + "group");
                if (firstGroup != null) group = firstGroup.Value.Trim();
            }

            var segments = new List<NzbSegment>();
            var segmentsEl = fileEl.Element(ns + "segments");
            if (segmentsEl != null)
            {
                foreach (var segEl in segmentsEl.Elements(ns + "segment"))
                {
                    int number = (int?)segEl.Attribute("number") ?? 0;
                    long bytes = (long?)segEl.Attribute("bytes") ?? 0;
                    string msgId = segEl.Value.Trim().Trim('<', '>');
                    if (msgId.Length > 0)
                    {
                        segments.Add(new NzbSegment(number, msgId, bytes));
                    }
                }
                // Ensure segments are in number order regardless of document order
                segments.Sort((a, b) => a.Number.CompareTo(b.Number));
            }

            files.Add(new NzbFile(subject, poster, group, segments));
        }

        return files;
    }
}
