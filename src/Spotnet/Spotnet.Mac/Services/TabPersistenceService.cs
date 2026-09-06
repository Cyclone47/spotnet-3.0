using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>Eén opgeslagen tabblad: message-id en titel.</summary>
public sealed record SavedTab(string MessageId, string Title);

/// <summary>
/// Opslag van geopende spot-tabbladen, de Mac-tegenhanger van Windows'
/// <c>TabStorer</c>: één regel per tabblad als <c>msgid\ttitle</c> in
/// <c>tabs.dat</c> in de instellingenmap, alleen als SaveTabs aan staat.
/// </summary>
public sealed class TabPersistenceService
{
    private const string TabsFileName = "tabs.dat";
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _tabsFilePath;

    public TabPersistenceService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _tabsFilePath = Path.Combine(appPaths.DataFolder, TabsFileName);
    }

    /// <summary>Slaat de open spot-tabbladen op, zoals Windows' SaveTabs.</summary>
    public void SaveTabs(IReadOnlyList<SavedTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        try
        {
            var lines = new List<string>(tabs.Count);
            foreach (var tab in tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.MessageId))
                {
                    continue;
                }
                // Windows maakt de titel tab-vrij; het bericht-id is de sleutel.
                lines.Add($"{tab.MessageId}\t{tab.Title?.Replace("\t", "")}");
            }

            File.WriteAllLines(_tabsFilePath, lines, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to save open tabs to {0}", _tabsFilePath);
        }
    }

    /// <summary>Leest de opgeslagen tabbladen; een ontbrekend bestand is er gewoon niet.</summary>
    public IReadOnlyList<SavedTab> LoadTabs()
    {
        var tabs = new List<SavedTab>();
        try
        {
            if (!File.Exists(_tabsFilePath))
            {
                return tabs;
            }

            foreach (string line in File.ReadAllLines(_tabsFilePath, Encoding.UTF8))
            {
                int split = line.IndexOf('\t');
                if (split <= 0 || split == line.Length - 1)
                {
                    continue;
                }
                tabs.Add(new SavedTab(line[..split], line[(split + 1)..]));
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load saved tabs from {0}", _tabsFilePath);
        }
        return tabs;
    }

    /// <summary>Verwijdert de opslag, zoals Windows' ClearSavedTabs.</summary>
    public void ClearTabs()
    {
        try
        {
            File.Delete(_tabsFilePath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to clear saved tabs at {0}", _tabsFilePath);
        }
    }
}
