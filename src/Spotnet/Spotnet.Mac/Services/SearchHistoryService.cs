using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// Zoekgeschiedenis, de port van Windows' <c>Spotnet.Utilities.History</c>: één
/// zoekterm per regel in <c>history.dat</c> in de instellingenmap, dubbele termen
/// worden niet opgeslagen, en bij meer dan 1000 regels wordt het bestand ingekort
/// tot de laatste 500 (Windows' exacte inkort-logiek).
/// </summary>
public sealed class SearchHistoryService
{
    private const string HistoryFileName = "history.dat";
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _historyFilePath;
    private readonly List<string> _items = new();

    public SearchHistoryService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _historyFilePath = Path.Combine(appPaths.DataFolder, HistoryFileName);
        LoadHistory();
    }

    public IReadOnlyList<string> HistoryItems => _items;

    private void LoadHistory()
    {
        _items.Clear();
        try
        {
            if (!File.Exists(_historyFilePath))
            {
                return;
            }

            _items.AddRange(File.ReadAllLines(_historyFilePath, Encoding.UTF8));
            // Windows inkort bij meer dan 1000 regels naar de laatste 500.
            if (_items.Count > 1000)
            {
                using var writer = new StreamWriter(_historyFilePath, append: false, Encoding.UTF8);
                for (int i = _items.Count - 500; i < _items.Count; i++)
                {
                    writer.WriteLine(_items[i]);
                }
                _items.RemoveRange(0, _items.Count - 500);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load search history from {0}", _historyFilePath);
        }
    }

    /// <summary>Slaat een zoekterm op, zoals Windows' SaveHistory: negeer leeg en dubbel.</summary>
    public bool SaveHistory(string term)
    {
        try
        {
            if (string.IsNullOrEmpty(term) || _items.Contains(term))
            {
                return true;
            }

            using var writer = new StreamWriter(_historyFilePath, append: true, Encoding.UTF8);
            writer.WriteLine(term);
            _items.Add(term);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to save search history to {0}", _historyFilePath);
            return false;
        }
    }

    /// <summary>Verwijdert history.dat, zoals Windows' ClearHistory.</summary>
    public bool ClearHistory()
    {
        try
        {
            File.Delete(_historyFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to clear search history at {0}", _historyFilePath);
            return false;
        }
    }
}
