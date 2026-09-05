using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// Manages the Spotnet trust model (blacklists and whitelists for posters and spots).
/// Directly mirrors the logic, file formats and built-in keys of Windows Spotnet.Model.BlackAndWhite.
/// </summary>
public sealed class TrustService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly UserPreferencesService _prefsService;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    private HashSet<string> _whiteList = new(StringComparer.Ordinal);
    private HashSet<string> _blackList = new(StringComparer.Ordinal);
    private HashSet<string> _spotWhiteList = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _spotBlackList = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string User, string Modulus)> _serverWhiteList = new();

    private System.Threading.Timer? _autoUpdateTimer;
    private bool _disposed;

    public event Action? ListsChanged;

    public IReadOnlySet<string> BlacklistModuli => _blackList;
    public IReadOnlySet<string> BlacklistMsgIds => _spotBlackList;
    public IReadOnlySet<string> WhitelistModuli => _whiteList;
    public IReadOnlySet<string> WhitelistMsgIds => _spotWhiteList;

    public static TrustService? Instance { get; private set; }

    public TrustService(IAppPaths appPaths, UserPreferencesService prefsService, HttpClient? httpClient = null)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _prefsService = prefsService ?? throw new ArgumentNullException(nameof(prefsService));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        Instance = this;
        SpotItem.GlobalPosterIdentResolver = GetPosterIdent;

        EnsureFilesAndLoad();
        ConfigureAutoUpdateTimer();
    }

    public void EnsureFilesAndLoad()
    {
        lock (_lock)
        {
            string whitelistXml = Path.Combine(_appPaths.DataFolder, "whitelist.xml");
            if (!File.Exists(whitelistXml))
            {
                CreateDefaultWhitelist(whitelistXml);
            }

            string blacklistXml = Path.Combine(_appPaths.DataFolder, "blacklist.xml");
            if (!File.Exists(blacklistXml))
            {
                CreateEmptyXmlList(blacklistXml);
            }

            ReloadAllLists();
        }
    }

    public void ReloadAllLists()
    {
        lock (_lock)
        {
            _whiteList = new HashSet<string>(StringComparer.Ordinal);
            _blackList = new HashSet<string>(StringComparer.Ordinal);
            _spotWhiteList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _spotBlackList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _serverWhiteList.Clear();

            // 1. Poster whitelist (local XML)
            string whitelistXml = Path.Combine(_appPaths.DataFolder, "whitelist.xml");
            LoadXmlToList(whitelistXml, _whiteList);

            // 2. Poster blacklist (local XML)
            string blacklistXml = Path.Combine(_appPaths.DataFolder, "blacklist.xml");
            LoadXmlToList(blacklistXml, _blackList);

            // 3. Removed lists (exclusions)
            var blackRemoved = LoadXmlToSet(Path.Combine(_appPaths.DataFolder, "blacklist.srv.removed.xml"));
            var spotBlackRemoved = LoadXmlToSet(Path.Combine(_appPaths.DataFolder, "spot_blacklist.srv.removed.xml"));
            var spotWhiteRemoved = LoadXmlToSet(Path.Combine(_appPaths.DataFolder, "spot_whitelist.srv.removed.xml"));

            // 4. Server poster blacklist (CSV)
            string blackCsv = Path.Combine(_appPaths.DataFolder, "blacklist.srv.csv");
            LoadCsvToList(blackCsv, _blackList, blackRemoved);

            // 5. Server spot blacklist (CSV)
            string spotBlackCsv = Path.Combine(_appPaths.DataFolder, "spot_blacklist.srv.csv");
            LoadCsvToList(spotBlackCsv, _spotBlackList, spotBlackRemoved);

            // 6. Server spot whitelist (CSV)
            string spotWhiteCsv = Path.Combine(_appPaths.DataFolder, "spot_whitelist.srv.csv");
            LoadCsvToList(spotWhiteCsv, _spotWhiteList, spotWhiteRemoved);

            // 7. Server poster whitelist (CSV with username & modulus)
            string whiteCsv = Path.Combine(_appPaths.DataFolder, "whitelist.srv.csv");
            LoadServerWhitelistCsv(whiteCsv);

            Log.Info("Trust lists loaded: {0} whitelisted posters, {1} blacklisted posters, {2} whitelisted spots, {3} blacklisted spots, {4} verified server posters.",
                _whiteList.Count, _blackList.Count, _spotWhiteList.Count, _spotBlackList.Count, _serverWhiteList.Count);
        }

        ListsChanged?.Invoke();
    }

    /// <summary>
    /// Computes the poster identity type for a spot, mirroring Windows SpotEx.PosterIdent.
    /// </summary>
    public PosterIdentType GetPosterIdent(string? modulus, string? sender, string? msgId, long date)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(modulus) || modulus.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                // Spots before 2013-01-01 (1356998400 Unix time) lacked RSA keys and are verified by age
                return date > 0 && date < 1356998400L ? PosterIdentType.Verified : PosterIdentType.None;
            }

            if (_blackList.Contains(modulus))
            {
                return PosterIdentType.Black;
            }

            if (!string.IsNullOrWhiteSpace(msgId) && _spotBlackList.Contains(msgId))
            {
                return PosterIdentType.SpotBlack;
            }

            if (_whiteList.Contains(modulus))
            {
                return PosterIdentType.White;
            }

            if (date > 0 && date < 1356998400L)
            {
                return PosterIdentType.Verified;
            }

            if (IsModulusInServerWhitelist(modulus))
            {
                return PosterIdentType.Verified;
            }

            if (!string.IsNullOrWhiteSpace(msgId) && _spotWhiteList.Contains(msgId))
            {
                return PosterIdentType.SpotWhite;
            }

            if (!string.IsNullOrWhiteSpace(sender) && IsUsernameInServerWhitelist(sender))
            {
                return PosterIdentType.Fake;
            }

            return PosterIdentType.None;
        }
    }

    public bool IsModulusInServerWhitelist(string modulus)
    {
        return _serverWhiteList.Any(p => string.Equals(p.Modulus, modulus, StringComparison.Ordinal));
    }

    public bool IsUsernameInServerWhitelist(string poster)
    {
        string cleanPoster = CleanUsername(poster);
        return _serverWhiteList.Any(p => string.Equals(CleanUsername(p.User), cleanPoster, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanUsername(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        int bracket = name.IndexOf('<', StringComparison.Ordinal);
        if (bracket >= 0) name = name[..bracket];
        return System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9]", "").Trim();
    }

    public bool IsBlacklisted(string? modulus, string? msgId)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(modulus) && _blackList.Contains(modulus)) return true;
            if (!string.IsNullOrWhiteSpace(msgId) && _spotBlackList.Contains(msgId)) return true;
            return false;
        }
    }

    public bool IsWhitelisted(string? modulus, string? msgId)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(modulus) && (_whiteList.Contains(modulus) || IsModulusInServerWhitelist(modulus))) return true;
            if (!string.IsNullOrWhiteSpace(msgId) && _spotWhiteList.Contains(msgId)) return true;
            return false;
        }
    }

    public bool AddBlack(string name, string modulus)
    {
        if (string.IsNullOrWhiteSpace(modulus)) return false;
        lock (_lock)
        {
            _blackList.Add(modulus);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "blacklist.xml"), name, modulus);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "blacklist.srv.removed.xml"), modulus);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool RemoveBlack(string modulus)
    {
        if (string.IsNullOrWhiteSpace(modulus)) return false;
        lock (_lock)
        {
            _blackList.Remove(modulus);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "blacklist.xml"), modulus);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "blacklist.srv.removed.xml"), "", modulus);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool AddWhite(string name, string modulus)
    {
        if (string.IsNullOrWhiteSpace(modulus)) return false;
        lock (_lock)
        {
            _whiteList.Add(modulus);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "whitelist.xml"), name, modulus);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool RemoveWhite(string modulus)
    {
        if (string.IsNullOrWhiteSpace(modulus)) return false;
        lock (_lock)
        {
            _whiteList.Remove(modulus);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "whitelist.xml"), modulus);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool AddSpotBlack(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId)) return false;
        lock (_lock)
        {
            _spotBlackList.Add(msgId);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "spot_blacklist.xml"), "", msgId);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "spot_blacklist.srv.removed.xml"), msgId);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool RemoveSpotBlack(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId)) return false;
        lock (_lock)
        {
            _spotBlackList.Remove(msgId);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "spot_blacklist.xml"), msgId);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "spot_blacklist.srv.removed.xml"), "", msgId);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool AddSpotWhite(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId)) return false;
        lock (_lock)
        {
            _spotWhiteList.Add(msgId);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "spot_whitelist.xml"), "", msgId);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "spot_whitelist.srv.removed.xml"), msgId);
        }
        ListsChanged?.Invoke();
        return true;
    }

    public bool RemoveSpotWhite(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId)) return false;
        lock (_lock)
        {
            _spotWhiteList.Remove(msgId);
            RemoveKeyFromXmlFile(Path.Combine(_appPaths.DataFolder, "spot_whitelist.xml"), msgId);
            AddKeyToXmlFile(Path.Combine(_appPaths.DataFolder, "spot_whitelist.srv.removed.xml"), "", msgId);
        }
        ListsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Synchronizes the current blacklist and whitelist entries to SQLite tables.
    /// </summary>
    public async Task SyncToDatabaseAsync(SpotDatabaseService dbService)
    {
        ArgumentNullException.ThrowIfNull(dbService);
        List<string> blackMods;
        List<string> blackSpots;
        List<string> whiteMods;
        List<string> whiteSpots;

        lock (_lock)
        {
            blackMods = _blackList.ToList();
            blackSpots = _spotBlackList.ToList();
            whiteMods = _whiteList.Concat(_serverWhiteList.Select(s => s.Modulus)).Distinct().ToList();
            whiteSpots = _spotWhiteList.ToList();
        }

        await dbService.SyncTrustListsAsync(blackMods, blackSpots, whiteMods, whiteSpots);
    }

    public async Task UpdateExternalListsAsync(CancellationToken cancellationToken = default)
    {
        var prefs = _prefsService.Current;
        if (!prefs.DownloadExternalLists) return;

        Log.Info("Starting download of external blacklist and whitelist lists...");

        await DownloadAndReplaceAsync(prefs.WhitelistUrl, Path.Combine(_appPaths.DataFolder, "whitelist.srv.csv"), cancellationToken);
        await DownloadAndReplaceAsync(prefs.BlacklistUrl, Path.Combine(_appPaths.DataFolder, "blacklist.srv.csv"), cancellationToken);
        await DownloadAndReplaceAsync(prefs.SpotWhitelistUrl, Path.Combine(_appPaths.DataFolder, "spot_whitelist.srv.csv"), cancellationToken);
        await DownloadAndReplaceAsync(prefs.SpotBlacklistUrl, Path.Combine(_appPaths.DataFolder, "spot_blacklist.srv.csv"), cancellationToken);

        ReloadAllLists();
    }

    private async Task DownloadAndReplaceAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            string tempPath = targetPath + ".new";
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn("HTTP {0} while downloading external list from {1}", (int)response.StatusCode, url);
                return;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content)) return;

            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
            Log.Debug("Updated external list {0}", targetPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(ex, "Failed to download external list from {0}", url);
        }
    }

    private void ConfigureAutoUpdateTimer()
    {
        _autoUpdateTimer?.Dispose();
        _autoUpdateTimer = null;

        var prefs = _prefsService.Current;
        if (prefs.DownloadExternalLists && prefs.ExternalListsUpdateInterval > 0)
        {
            _autoUpdateTimer = new System.Threading.Timer(
                _ => _ = UpdateExternalListsAsync(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(prefs.ExternalListsUpdateInterval));
        }
    }

    #region XML & CSV Helpers

    private static void CreateEmptyXmlList(string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Keys>\n</Keys>\n", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not create empty XML list at {0}", file);
        }
    }

    private static void CreateDefaultWhitelist(string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Keys>");
            foreach (var (modulus, name) in DefaultTrustedPosters)
            {
                sb.AppendLine($"  <Key Name=\"{name}\">{modulus}</Key>");
            }
            sb.AppendLine("</Keys>");
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not create default whitelist at {0}", file);
        }
    }

    private static void LoadXmlToList(string file, HashSet<string> list)
    {
        if (!File.Exists(file)) return;
        try
        {
            var doc = XDocument.Load(file);
            var root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "keys", StringComparison.OrdinalIgnoreCase)) return;

            foreach (var elem in root.Elements())
            {
                string val = elem.Value.Trim();
                if (val.Length > 0)
                {
                    list.Add(val);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse XML list {0}", file);
        }
    }

    private static HashSet<string> LoadXmlToSet(string file)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LoadXmlToList(file, set);
        return set;
    }

    private static void LoadCsvToList(string file, HashSet<string> list, HashSet<string> exclusions)
    {
        if (!File.Exists(file)) return;
        try
        {
            foreach (string rawLine in File.ReadLines(file, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                int comma = line.IndexOf(',', StringComparison.Ordinal);
                string key = comma >= 0 ? line[(comma + 1)..].Trim() : line;

                if (key.Length > 0 && !exclusions.Contains(key))
                {
                    list.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse CSV list {0}", file);
        }
    }

    private void LoadServerWhitelistCsv(string file)
    {
        if (!File.Exists(file)) return;
        try
        {
            foreach (string rawLine in File.ReadLines(file, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                string[] parts = line.Split(',', 2);
                if (parts.Length == 2)
                {
                    string user = parts[0].Trim();
                    string modulus = parts[1].Trim();
                    if (user.Length > 0 && modulus.Length > 0)
                    {
                        _serverWhiteList.Add((user, modulus));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse server whitelist CSV {0}", file);
        }
    }

    private static void AddKeyToXmlFile(string file, string name, string key)
    {
        try
        {
            XDocument doc;
            if (File.Exists(file))
            {
                doc = XDocument.Load(file);
            }
            else
            {
                doc = new XDocument(new XElement("Keys"));
            }

            var root = doc.Root ?? new XElement("Keys");
            if (doc.Root == null) doc.Add(root);

            // Avoid duplicates
            foreach (var elem in root.Elements())
            {
                if (string.Equals(elem.Value.Trim(), key.Trim(), StringComparison.Ordinal))
                {
                    return;
                }
            }

            var keyElem = new XElement("Key", key.Trim());
            if (!string.IsNullOrWhiteSpace(name))
            {
                keyElem.SetAttributeValue("Name", name.Trim());
            }
            root.Add(keyElem);

            string tmp = file + ".tmp";
            doc.Save(tmp);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to add key to XML file {0}", file);
        }
    }

    private static void RemoveKeyFromXmlFile(string file, string key)
    {
        if (!File.Exists(file)) return;
        try
        {
            var doc = XDocument.Load(file);
            var root = doc.Root;
            if (root == null) return;

            bool removed = false;
            foreach (var elem in root.Elements().ToList())
            {
                if (string.Equals(elem.Value.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    elem.Remove();
                    removed = true;
                }
            }

            if (removed)
            {
                string tmp = file + ".tmp";
                doc.Save(tmp);
                File.Move(tmp, file, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to remove key from XML file {0}", file);
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoUpdateTimer?.Dispose();
        _autoUpdateTimer = null;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
            SpotItem.GlobalPosterIdentResolver = null;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Initial 30 trusted posters bundled with Windows Spotnet 3.0 (BlackAndWhite.cs lines 112-141).
    /// </summary>
    public static readonly (string Modulus, string Name)[] DefaultTrustedPosters =
    {
        ("1wt6jlePL/IADm4wL8lMqHaGVznPTiUvcovAtj3eCgvt3wTyM9Fd8ptx8+xzmAHL", "Albertina"),
        ("ynakBYOJnwLBuXQZvglD1N/uZ0mZqYad9dKX9KxyOe2mPoEZIE8Y/x93U8VL4tnv", "Bacoben1"),
        ("6ibY+eDYDwXOjV992fdCqhE0V0B2rRwqvxmoodPlpgjSshPCUgVjTHqpoC1AzbqR", "Boaz"),
        ("vaaHp9taPnRVbYZaa5etSK6y4Caft5aOrnzqfjPljgD2UE/89TBz6JbA/NeJpK+p", "BOB1961"),
        ("s7xw10e0wq6dZgrkD59T9F/lj0zSaht0Zv0gYVvS2gR7I4VPjo/TrqxhwSP3by//", "Biky"),
        ("zNOkGYubV87uJaL1KIqqHHs+nKWNwhD0yNEu0Mz4TKBVkDkxdTB8RvcAa79tMyaL", "Blowan"),
        ("0pGKk73HQkkj1waqHSjuMtpqAuAhItXNYXOQXHQL+rqORxzqMMoQeg523iJKUbvf", "Bradje"),
        ("snmypn4sZq+N4tn+UT6IFPn9Ii67iteD/T/weYVVQbQWvui4M1SSUxaqvIFQtQ8l", "CaptainSalvo"),
        ("58UoKbJ7JgNbRFJJqpdwO3MYKexHlkkUt6KfZvP7lykUNHRm/sZssM4o2jUm6TUh", "CaptainSalvo"),
        ("rCpFxtuo9ijYWTg4WpDnQQO2dVQGSlhGamuUmWCrpilfEbWKNLap+EFnNEqCHdbF", "Dick42"),
        ("z+U5teGdCtU0MVePPPZu1APEJfpSAPNh/RR1EyBXRD1G8d73M+qJZJqfJUL9smUF", "Falang01"),
        ("ru3rhWGBsx4dCglEwjE3bL9nVfH2gJVS0kb0OrXQTceeMXLDVb4rsuA+ty85M3If", "Hagenees1978"),
        ("xC2V+4i7J07fm6+ND+Mr5hvD359l2R/bkeOt2cGUpeFznxhItdMEVJKDNthKFNIb", "Hagenees1978"),
        ("ySv0wJaY8WQPb1KUkJeOVr4dGqR2UoxaOxsnqYmcgkbiPhigkb235eVvoIj4AVM7", "Hannes3"),
        ("z/4mkqzLE27ur8iNOTerBbFK37//itkNa5APDIRLTQ3gBJZORgOcqT+51lw2qnQx", "HendrikjeStoffel"),
        ("twJLKIJYDQvTGhk3hnLSWdgE9oXkH/RypTAI7Bo2rBHkH5FfL/FOJEvOp/MVRWFP", "Inge2222"),
        ("reZxfDPBE/Bxqa63PW4LFiDTh6xl7w1Sh3eoYmUbYbiI8AbmtWNmWAWjC6mHef+b", "Kaj7"),
        ("szsAIT5lVEzonnwg81DoU/44KTXkdIYrAdAFpoB/99Fw0VC6QVad7PRKgDPFeDW5", "kww"),
        ("qIxm7gFn8z6eIheHbstSa0vEhciwEMzNMjYlvBXJEBmivtcfrTXXz57VMfIDtKZB", "kww"),
        ("4ci3BuoC+JHlHVTxYacoEmk7rXGnrRlmgp1zuNO/wrtX0M0ixhK1MUlMMZIaVJ39", "Oldtimer"),
        ("rla55FY/Gm1DgPFwo4+HgMq8bElbjW9W8dIBUFun3ujfujp89p07LAQkS32FWQNb", "Ricardoo"),
        ("qp1ja8wjPDlh7aEssytHTflMCeKLF1TDoZlA41Qp9rkifx+qz9oY21FqZxgOiQgN", "Ricardoo"),
        ("+QIm6ZjUIY8Jgn0venbvGoik2hZyPZpNlXJrGlCbQgRndiN4apVb9awMsp2YGY5j", "SubmarinesSpot"),
        ("p2T1o0E6djKXSBqv8sRPLVsKxZnZOzuQgzY25QBdF5l5+5El81ziGD+5RBuUXSkT", "SubmarinesSpot"),
        ("0mqEBWp/z9l8W15lwntuXNxpcY04o96/MxGe4OCg6dFCjzQ6g8kSej/QoL9tkhB/", "Sophia1949"),
        ("9lfEiCusAUMTMqCOi6sc6P2IYoDslFbGEIYZ16ku6Nqrclc5oyLE7wz2fUI0RJvx", "Trein1600"),
        ("yf0oZC/mJLo0iHunzKn1YyvPCyI6r/ACTNAG3K53BzF4efYWe37EC9P4nRmHEDPJ", "xxxwebwatchers"),
        ("zxCiZ9F9yZ7DdEPj+1Ta/nQl679amRgc+BcmFuRpWvt9VjnHzY7dUTMPUavB8jUN", "Y0os"),
        ("u9bdM+NQl4OPvhi4GHiRvyvDRuTVBemAeAh70lpIWGRqiv03hDvI7W53FuQk3rDX", "Zoutoplossing"),
        ("uH19iDBeTjye6rhOi4uLR+T59MThUBQNL0ZgQhsX6BQqQxZNYflwccud9ZN64Rb5", "Zoutoplossing")
    };
}
