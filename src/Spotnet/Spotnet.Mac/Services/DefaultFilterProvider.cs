using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.Services;

/// <summary>
/// Loads the bundled advanced-filter tree — the same <c>FiltersAdvanced</c> XML the
/// Windows client ships — and turns it into <see cref="FilterItem"/> nodes.
/// </summary>
public static class DefaultFilterProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string ResourceName = "Spotnet.Mac.Resources.FiltersAdvanced.xml";

    /// <summary>
    /// Emoji stand-ins for the Windows bitmap icons, keyed by the filter name as it
    /// appears in the XML. Anything unlisted falls back to the parent's icon.
    /// </summary>
    private static readonly Dictionary<string, string> IconByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nieuw"] = "🆕",
        ["Overzicht"] = "📋",
        ["Laatste 24 uur"] = "🕐",
        ["Beeld"] = "🎬",
        ["Beeld - Genres"] = "🎭",
        ["Beeld - TV Series"] = "📺",
        ["Boeken"] = "📚",
        ["Muziek"] = "🎵",
        ["Muziek - Genres"] = "🎼",
        ["Spellen"] = "🎮",
        ["Spellen - Console"] = "🕹️",
        ["Spellen - Mobile"] = "📱",
        ["Applicaties"] = "💻",
        ["Applicaties - Mobile"] = "📲",
        ["Erotiek"] = "🔞",
        ["Films"] = "🎬",
        ["Series"] = "📺",
        ["Windows"] = "🪟",
        ["Windows Mobile"] = "🪟",
        ["Mac"] = "🍎",
        ["Iphone"] = "🍎",
        ["Ipad"] = "🍎",
        ["Linux"] = "🐧",
        ["Linux/OS2"] = "🐧",
        ["Android"] = "🤖",
        ["Android Tablet"] = "🤖",
        ["Blackberry"] = "📱",
        ["Symbian"] = "📱"
    };

    /// <summary>
    /// Reads the embedded filter tree. Returns the top-level filters in document order;
    /// an empty list (and a logged warning) if the resource cannot be read.
    /// </summary>
    public static List<FilterItem> Load()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Log.Warn("Embedded filter resource {0} was not found.", ResourceName);
                return new List<FilterItem>();
            }

            var doc = XDocument.Load(stream);
            return doc.Root == null
                ? new List<FilterItem>()
                : doc.Root.Elements("Filter").Select(e => Convert(e, "🔹")).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the bundled filter tree: {0}", ex.Message);
            return new List<FilterItem>();
        }
    }

    private static FilterItem Convert(XElement element, string parentIcon)
    {
        // Names in the XML carry a leading space that the Windows tree trims for display.
        string name = (element.Attribute("Name")?.Value ?? "").Trim();

        // A node's own query is the Query attribute when it has children, otherwise its
        // CDATA body. Group nodes carry both; the attribute is what clicking them runs.
        string query = element.Attribute("Query")?.Value?.Trim() ?? "";
        if (string.IsNullOrEmpty(query) && !element.HasElements)
        {
            query = element.Value.Trim();
        }

        string icon = IconByName.TryGetValue(name, out var mapped) ? mapped : parentIcon;

        var item = new FilterItem
        {
            Id = "def_" + name.Replace(' ', '_'),
            Kind = element.HasElements ? FilterKind.Category : FilterKind.Preset,
            Name = name,
            Icon = icon,
            Query = query
        };

        foreach (var child in element.Elements("Filter"))
        {
            item.Children.Add(Convert(child, icon));
        }

        return item;
    }
}
