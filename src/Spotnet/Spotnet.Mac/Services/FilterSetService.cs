using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// The filter-set store: a port of Spotnet.Model.Filters. One directory per set under
/// Filters.v2, each holding a filters.xml in the Windows format, so a set can be copied
/// between the two clients. The four shipped sets are immutable; mutating while one of
/// them is active forks the tree into the mutable "Aangepast" set first, exactly as
/// Windows' AddFilter/UpdateFilterQuery/RemoveFilter/SwapFilter do.
/// </summary>
public sealed class FilterSetService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public const string CustomSetName = "Aangepast";
    public const string FiltersFileName = "filters.xml";

    public static readonly string[] ImmutableSetNames =
        { "Geavanceerd NL", "Advanced EN", "Eenvoudig NL", "Simple EN" };

    private readonly IAppPaths _appPaths;
    private readonly UserPreferencesService _prefsService;

    public FilterSetService(IAppPaths appPaths, UserPreferencesService prefsService)
    {
        _appPaths = appPaths;
        _prefsService = prefsService;
    }

    // ── Active set ────────────────────────────────────────────────────────────

    /// <summary>The set the sidebar shows; empty preference falls back to Aangepast.</summary>
    public string ActiveSet
    {
        get => string.IsNullOrWhiteSpace(_prefsService.Current.FilterSetName)
            ? CustomSetName
            : _prefsService.Current.FilterSetName;
        set
        {
            var prefs = _prefsService.Current;
            prefs.FilterSetName = value;
            _prefsService.Save(prefs);
        }
    }

    public bool IsActiveSetImmutable =>
        ImmutableSetNames.Contains(ActiveSet, StringComparer.OrdinalIgnoreCase);

    /// <summary>The four shipped sets first, then every directory holding a filters.xml.</summary>
    public IReadOnlyList<string> GetSetNames()
    {
        var names = new List<string>(ImmutableSetNames);
        try
        {
            if (Directory.Exists(_appPaths.FiltersFolder))
            {
                foreach (string dir in Directory.GetDirectories(_appPaths.FiltersFolder))
                {
                    string name = Path.GetFileName(dir);
                    if (File.Exists(Path.Combine(dir, FiltersFileName)) &&
                        !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not enumerate filter sets in {0}", _appPaths.FiltersFolder);
        }
        return names;
    }

    public bool SetExists(string name) => Directory.Exists(SetDir(name));

    private string SetDir(string setName) => Path.Combine(_appPaths.FiltersFolder, setName);

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the shipped sets on first run, like Windows' InitializeDefaultFilters:
    /// the four immutable ones plus Aangepast as a copy of the language's advanced set.
    /// </summary>
    public void InitializeDefaultSets()
    {
        foreach (string name in ImmutableSetNames)
        {
            if (!SetExists(name)) WriteSet(name, DefaultTree(name));
        }

        if (!SetExists(CustomSetName))
        {
            WriteSet(CustomSetName, DefaultTree("Geavanceerd NL"));
        }
    }

    // ── Load / save ───────────────────────────────────────────────────────────

    public List<FilterItem> LoadActiveTree() => LoadTree(ActiveSet);

    /// <summary>
    /// Reads a set's filters.xml, falling back to the shipped tree when the file is
    /// missing or unreadable — the same degradation Windows' LoadFilters applies.
    /// </summary>
    public List<FilterItem> LoadTree(string setName)
    {
        string path = Path.Combine(SetDir(setName), FiltersFileName);
        try
        {
            if (File.Exists(path))
            {
                var doc = XDocument.Load(path);
                if (doc.Root != null) return ParseTree(doc.Root);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not read filter set {0}, falling back to the shipped tree.", setName);
        }

        return DefaultTree(setName);
    }

    /// <summary>Writes the tree as the named set. Refuses the immutable sets.</summary>
    public bool SaveTree(string setName, IReadOnlyList<FilterItem> nodes)
    {
        if (ImmutableSetNames.Contains(setName, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warn("Refused to write the immutable filter set {0}.", setName);
            return false;
        }

        try
        {
            WriteSet(setName, nodes);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save filter set {0}", setName);
            return false;
        }
    }

    /// <summary>
    /// Windows' fork rule: mutating while an immutable set is active first saves the
    /// current tree over Aangepast and switches to it. Returns the set to save into.
    /// </summary>
    public string EnsureMutableSet(IReadOnlyList<FilterItem> currentTree)
    {
        if (!IsActiveSetImmutable) return ActiveSet;

        WriteSet(CustomSetName, currentTree);
        ActiveSet = CustomSetName;
        return CustomSetName;
    }

    private void WriteSet(string setName, IReadOnlyList<FilterItem> nodes)
    {
        string dir = SetDir(setName);
        Directory.CreateDirectory(dir);

        var root = new XElement("Spotnet");
        foreach (var node in nodes) root.Add(ToElement(node));

        new XDocument(root).Save(Path.Combine(dir, FiltersFileName));
    }

    // ── Set management ────────────────────────────────────────────────────────

    /// <summary>Saves the tree as a new set and makes it active, like Filters.SaveAs.</summary>
    public (bool Ok, string Message) SaveAs(string newName, IReadOnlyList<FilterItem> tree, bool force = false)
    {
        if (ImmutableSetNames.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            return (false, "Cannot override default filters list");
        }

        if (SetExists(newName) && !force)
        {
            return (false, "exists");
        }

        try
        {
            WriteSet(newName, tree);
            ActiveSet = newName;
            return (true, "");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save filter set as {0}", newName);
            return (false, ex.Message);
        }
    }

    /// <summary>Deletes a mutable set, falling back to the Dutch advanced set like Windows.</summary>
    public bool RemoveSet(string name)
    {
        if (ImmutableSetNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;

        string dir = SetDir(name);
        if (!Directory.Exists(dir)) return false;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete filter set {0}", name);
            return false;
        }

        if (string.Equals(ActiveSet, name, StringComparison.OrdinalIgnoreCase))
        {
            ActiveSet = "Geavanceerd NL";
        }
        return true;
    }

    // ── Expansion state ───────────────────────────────────────────────────────

    private string ExpandedPath => Path.Combine(_appPaths.DataFolder, "filters.expanded.txt");

    public HashSet<string> LoadExpanded()
    {
        try
        {
            return File.Exists(ExpandedPath)
                ? new HashSet<string>(File.ReadAllLines(ExpandedPath).Where(l => l.Length > 0), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not read the filter expansion state.");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public void SaveExpanded(IEnumerable<string> paths)
    {
        try
        {
            File.WriteAllLines(ExpandedPath, paths, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not write the filter expansion state.");
        }
    }

    // ── XML <-> tree ──────────────────────────────────────────────────────────

    private static XElement ToElement(FilterItem item)
    {
        var element = new XElement("Filter", new XAttribute("Name", item.Name));
        // Windows writes a single space for a blank query so the attribute is present.
        element.SetAttributeValue("Query", string.IsNullOrWhiteSpace(item.Query) ? " " : item.Query);
        if (!string.IsNullOrEmpty(item.ImageRef))
        {
            element.SetAttributeValue("Image", item.ImageRef);
        }

        foreach (var child in item.Children) element.Add(ToElement(child));
        return element;
    }

    internal static List<FilterItem> ParseTree(XElement root) =>
        root.Elements("Filter").Select(e => ConvertElement(e, "🔹", "")).ToList();

    internal static FilterItem ConvertElement(XElement element, string parentIcon, string parentPath)
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

        string path = parentPath.Length == 0 ? name : parentPath + "/" + name;

        var item = new FilterItem
        {
            Id = path,
            Kind = element.HasElements ? FilterKind.Category : FilterKind.Preset,
            Name = name,
            Icon = IconByName.TryGetValue(name, out string? mapped) ? mapped : parentIcon,
            ImageRef = element.Attribute("Image")?.Value ?? "",
            Query = RewriteQuery(query)
        };

        foreach (var child in element.Elements("Filter"))
        {
            item.Children.Add(ConvertElement(child, item.Icon, path));
        }

        return item;
    }

    /// <summary>
    /// The load-time query rewrites Windows applies in Filters.LoadFiltersTo: the legacy
    /// FTS4 docid spelling, one patched category expression, and prefix searches that
    /// older sets wrote as plain equality.
    /// </summary>
    internal static string RewriteQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "";

        string rewritten = Regex.Replace(query, @"\bdocid\b", "rowid", RegexOptions.IgnoreCase);
        if (rewritten.Trim() == "cat = 1 AND cats MATCH '1b4 OR 1d11'")
        {
            rewritten = "cat = 6";
        }

        if (!rewritten.Contains("scat =", StringComparison.Ordinal) &&
            !rewritten.Contains("topcat =", StringComparison.Ordinal) &&
            !rewritten.Contains("subcat in", StringComparison.Ordinal) &&
            !rewritten.Contains("subcat =", StringComparison.Ordinal) &&
            !rewritten.Contains("subcats like", StringComparison.Ordinal))
        {
            rewritten = rewritten.Replace("tag = '", "tag MATCH '", StringComparison.Ordinal)
                                 .Replace("sender = '", "sender MATCH '", StringComparison.Ordinal);
        }

        return rewritten;
    }

    // ── Shipped trees ─────────────────────────────────────────────────────────

    internal static List<FilterItem> DefaultTree(string setName)
    {
        string? resource = setName switch
        {
            "Geavanceerd NL" => "Spotnet.Mac.Resources.FiltersAdvanced.xml",
            "Advanced EN" => "Spotnet.Mac.Resources.FiltersAdvanced_en.xml",
            _ => null
        };

        if (resource != null)
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
                if (stream != null)
                {
                    var doc = XDocument.Load(stream);
                    if (doc.Root != null) return ParseTree(doc.Root);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read the bundled filter tree {0}", resource);
            }
            return new List<FilterItem>();
        }

        return setName is "Eenvoudig NL" or "Simple EN" ? SimpleTree(setName == "Eenvoudig NL") : new List<FilterItem>();
    }

    /// <summary>The nine flat nodes Windows generates for its two simple sets.</summary>
    private static List<FilterItem> SimpleTree(bool dutch)
    {
        (string Name, string Query, string Icon)[] nodes =
        {
            (dutch ? "Nieuw" : "New", "rowid > [SN:NEW]", "🆕"),
            (dutch ? "Laatste 24 uur" : "Last 24 hours", "date > ( [SN:DATE] - 86400 )", "🕐"),
            (dutch ? "Beeld" : "Movies", "cat = 1", "🎬"),
            ("Series", "cat = 6", "📺"),
            (dutch ? "Boeken" : "Books", "cat = 5", "📚"),
            (dutch ? "Muziek" : "Music", "cat = 2", "🎵"),
            (dutch ? "Spellen" : "Games", "cat = 3", "🎮"),
            (dutch ? "Applicaties" : "Software", "cat = 4", "💻"),
            (dutch ? "Erotiek" : "Erotica", "cat = 9", "🔞")
        };

        return nodes.Select(n => new FilterItem
        {
            Id = n.Name,
            Kind = FilterKind.Preset,
            Name = n.Name,
            Icon = n.Icon,
            Query = n.Query
        }).ToList();
    }

    /// <summary>
    /// Emoji stand-ins for the Windows bitmap icons, keyed by the filter name as it
    /// appears in the XML. Anything unlisted falls back to the parent's icon.
    /// </summary>
    private static readonly Dictionary<string, string> IconByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Favorieten"] = "⭐",
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
}
