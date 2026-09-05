using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Remote;

public class RemoteCatalogService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<RemoteCatalogService> InstanceHolder = new Lazy<RemoteCatalogService>(() => new RemoteCatalogService());
    public static RemoteCatalogService Instance => InstanceHolder.Value;

    private readonly ConcurrentDictionary<string, string> _filterQueries = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<SpotCommentDto>> _localUserComments = new ConcurrentDictionary<string, List<SpotCommentDto>>(StringComparer.OrdinalIgnoreCase);

    public static string GetCategoryName(int category)
    {
        return category switch
        {
            1 => "Films",
            2 => "Muziek",
            3 => "Spellen",
            4 => "Applicaties",
            5 => "Boeken",
            6 => "Series",
            9 => "Erotiek",
            _ => "Overig"
        };
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        double gb = mb / 1024.0;
        return gb.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    }

    public static string FormatDate(long unixEpoch)
    {
        try
        {
            DateTime dt = DateTimeOffset.FromUnixTimeSeconds(unixEpoch).LocalDateTime;
            DateTime now = DateTime.Now;
            if (dt.Date == now.Date)
            {
                return $"Vandaag ({dt:HH:mm})";
            }
            if (dt.Date == now.Date.AddDays(-1))
            {
                return $"Gisteren ({dt:HH:mm})";
            }
            return dt.ToString("dd-MM-yyyy HH:mm");
        }
        catch
        {
            return "";
        }
    }

    private static SpotDto ReadSpotRow(DbDataReader reader)
    {
        long rowId = reader.GetInt64(0);
        string msgId = reader.IsDBNull(1) ? "" : reader.GetString(1);
        string subject = reader.IsDBNull(2) ? "" : reader.GetString(2);
        string sender = reader.IsDBNull(3) ? "" : reader.GetString(3);
        string tag = reader.IsDBNull(4) ? "" : reader.GetString(4);
        int cat = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
        long filesize = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
        long date = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
        string cats = reader.IsDBNull(8) ? "" : reader.GetString(8);

        bool isFav = !string.IsNullOrEmpty(cats) && cats.Contains("f1");

        return new SpotDto
        {
            Id = rowId,
            MessageId = msgId,
            Title = subject,
            Poster = sender,
            Tag = tag,
            Category = cat,
            CategoryName = GetCategoryName(cat),
            FileSize = filesize,
            FormattedSize = FormatFileSize(filesize),
            Date = date,
            FormattedDate = FormatDate(date),
            IsFavorite = isFav
        };
    }

    public List<SpotDto> GetSpots(string search, int? category, string filterId, int page, int pageSize, string sort)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 200) pageSize = 200;
        int offset = (page - 1) * pageSize;

        var list = new List<SpotDto>();

        try
        {
            using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
            using ISqlDbTransaction tx = db.BeginReadTransaction();
            using DbCommand cmd = db.CreateCommand(tx);

            var clauses = new List<string>();
            // Key != 2 and Key != 5 matches desktop Spotnet (excludes spam/deleted spots, includes all user spots)
            clauses.Add("spots.key != 2 AND spots.key != 5");

            // Filter logic: custom filter from desktop or category
            if (!string.IsNullOrWhiteSpace(filterId))
            {
                if (_filterQueries.Count == 0)
                {
                    GetFilters(); // Populate queries
                }

                if (_filterQueries.TryGetValue(filterId, out var fQuery) && !string.IsNullOrWhiteSpace(fQuery))
                {
                    clauses.Add($"({fQuery})");
                }
                else if (filterId.StartsWith("cat_", StringComparison.OrdinalIgnoreCase) && int.TryParse(filterId.Substring(4), out int cId))
                {
                    clauses.Add($"spots.cat = {cId}");
                }
            }
            else if (category.HasValue && category.Value > 0)
            {
                clauses.Add($"spots.cat = {category.Value}");
            }
            else
            {
                // Default hide erotica unless explicitly requested
                clauses.Add("spots.cat != 9");
            }

            string orderClause = "spots.rowid DESC";
            if (!string.IsNullOrWhiteSpace(sort))
            {
                switch (sort.ToLowerInvariant())
                {
                    case "date_desc": orderClause = "spots.rowid DESC"; break;
                    case "filesize_desc": orderClause = "spots.filesize DESC"; break;
                    case "filesize_asc": orderClause = "spots.filesize ASC"; break;
                    case "date_asc": orderClause = "spots.rowid ASC"; break;
                    case "subject_asc": orderClause = "spots.subject COLLATE NOCASE ASC"; break;
                    default: orderClause = "spots.rowid DESC"; break;
                }
            }

            var searchWords = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                searchWords = Regex.Matches(search, @"[\p{L}\p{N}]+")
                                   .Cast<Match>()
                                   .Select(m => m.Value)
                                   .Where(w => w.Length > 0)
                                   .ToList();
            }

            bool querySuccess = false;
            if (searchWords.Count > 0)
            {
                // Attempt 1: FTS5 MATCH on search virtual table
                try
                {
                    string ftsQuery = string.Join(" AND ", searchWords.Select(w => $"\"{w.Replace("\"", "")}\"*"));
                    cmd.CommandText = $@"
                        SELECT spots.rowid, spots.msgid, spots.subject, spots.sender, spots.tag,
                               spots.cat, spots.filesize, spots.date, spots.cats
                        FROM search
                        JOIN spots ON search.rowid = spots.rowid
                        WHERE search MATCH @searchMatch AND {string.Join(" AND ", clauses)}
                        ORDER BY {orderClause}
                        LIMIT @limit OFFSET @offset";

                    var pMatch = cmd.CreateParameter();
                    pMatch.ParameterName = "@searchMatch";
                    pMatch.Value = ftsQuery;
                    cmd.Parameters.Add(pMatch);

                    var pLimit = cmd.CreateParameter();
                    pLimit.ParameterName = "@limit";
                    pLimit.Value = pageSize;
                    cmd.Parameters.Add(pLimit);

                    var pOffset = cmd.CreateParameter();
                    pOffset.ParameterName = "@offset";
                    pOffset.Value = offset;
                    cmd.Parameters.Add(pOffset);

                    using (DbDataReader reader = db.ExecuteReader(cmd))
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadSpotRow(reader));
                        }
                    }
                    querySuccess = true;
                }
                catch (Exception ftsEx)
                {
                    Log.Warn("FTS search failed, falling back to LIKE: {0}", ftsEx.Message);
                    list.Clear();
                    cmd.Parameters.Clear();
                    querySuccess = false;
                }
            }

            if (!querySuccess)
            {
                cmd.Parameters.Clear();
                if (searchWords.Count > 0)
                {
                    for (int i = 0; i < searchWords.Count; i++)
                    {
                        clauses.Add($"spots.subject LIKE @like{i}");
                        var pLike = cmd.CreateParameter();
                        pLike.ParameterName = $"@like{i}";
                        pLike.Value = $"%{searchWords[i]}%";
                        cmd.Parameters.Add(pLike);
                    }
                }

                cmd.CommandText = $@"
                    SELECT spots.rowid, spots.msgid, spots.subject, spots.sender, spots.tag,
                           spots.cat, spots.filesize, spots.date, spots.cats
                    FROM spots
                    WHERE {string.Join(" AND ", clauses)}
                    ORDER BY {orderClause}
                    LIMIT @limit OFFSET @offset";

                var pLimit = cmd.CreateParameter();
                pLimit.ParameterName = "@limit";
                pLimit.Value = pageSize;
                cmd.Parameters.Add(pLimit);

                var pOffset = cmd.CreateParameter();
                pOffset.ParameterName = "@offset";
                pOffset.Value = offset;
                cmd.Parameters.Add(pOffset);

                using DbDataReader reader = db.ExecuteReader(cmd);
                while (reader.Read())
                {
                    list.Add(ReadSpotRow(reader));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetSpots query failed: {0}", ex.Message);
        }

        return list;
    }

    public List<SpotDto> GetFavorites(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        int offset = (page - 1) * pageSize;

        var list = new List<SpotDto>();
        try
        {
            using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
            using ISqlDbTransaction tx = db.BeginReadTransaction();
            using DbCommand cmd = db.CreateCommand(tx);

            cmd.CommandText = @"
                SELECT spots.rowid, spots.msgid, spots.subject, spots.sender, spots.tag,
                       spots.cat, spots.filesize, spots.date, spots.cats
                FROM spots
                WHERE spots.cats LIKE '%f1%' AND spots.key != 2 AND spots.key != 5
                ORDER BY spots.rowid DESC
                LIMIT @limit OFFSET @offset";

            var pLimit = cmd.CreateParameter();
            pLimit.ParameterName = "@limit";
            pLimit.Value = pageSize;
            cmd.Parameters.Add(pLimit);

            var pOffset = cmd.CreateParameter();
            pOffset.ParameterName = "@offset";
            pOffset.Value = offset;
            cmd.Parameters.Add(pOffset);

            using DbDataReader reader = db.ExecuteReader(cmd);
            while (reader.Read())
            {
                list.Add(ReadSpotRow(reader));
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetFavorites failed: {0}", ex.Message);
        }
        return list;
    }

    public SpotDetailDto GetSpotDetail(long id)
    {
        SpotDetailDto dto = null;
        try
        {
            using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
            using ISqlDbTransaction tx = db.BeginReadTransaction();
            using DbCommand cmd = db.CreateCommand(tx);

            cmd.CommandText = @"
                SELECT spots.rowid, spots.msgid, spots.subject, spots.sender, spots.tag,
                       spots.cat, spots.filesize, spots.date, spots.cats
                FROM spots
                WHERE spots.rowid = @rowid LIMIT 1";

            var pId = cmd.CreateParameter();
            pId.ParameterName = "@rowid";
            pId.Value = id;
            cmd.Parameters.Add(pId);

            using DbDataReader reader = db.ExecuteReader(cmd);
            if (reader.Read())
            {
                long rowId = reader.GetInt64(0);
                string msgId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string subject = reader.IsDBNull(2) ? "" : reader.GetString(2);
                string sender = reader.IsDBNull(3) ? "" : reader.GetString(3);
                string tag = reader.IsDBNull(4) ? "" : reader.GetString(4);
                int cat = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                long filesize = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                long date = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                string cats = reader.IsDBNull(8) ? "" : reader.GetString(8);

                dto = new SpotDetailDto
                {
                    Id = rowId,
                    MessageId = msgId,
                    Title = subject,
                    Poster = sender,
                    Tag = tag,
                    Category = cat,
                    CategoryName = GetCategoryName(cat),
                    FileSize = filesize,
                    FormattedSize = FormatFileSize(filesize),
                    Date = date,
                    FormattedDate = FormatDate(date),
                    IsFavorite = !string.IsNullOrEmpty(cats) && cats.Contains("f1"),
                    HasNzb = true,
                    HasImage = true
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetSpotDetail DB query failed: {0}", ex.Message);
            return null;
        }

        if (dto == null) return null;

        // Try getting description from cache or network
        try
        {
            SpotEx cached = FileCacheManager.Get(dto.MessageId);
            if (cached != null && !string.IsNullOrEmpty(cached.Body))
            {
                dto.Description = SanitizeDescriptionToHtml(cached.Body);
                dto.HasImage = cached.ImageSource != null && cached.ImageSource.Length > 0;
            }
            else
            {
                // Fetch spot metadata via NNTP on background task
                string errorMsg = "";
                SpotEx spotOut = null;
                if (Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, dto.Id, dto.MessageId, ref spotOut, AppHelper.HeaderSettings(false), ref errorMsg))
                {
                    if (spotOut != null)
                    {
                        dto.Description = SanitizeDescriptionToHtml(spotOut.Body);
                        dto.HasImage = spotOut.ImageSource != null && spotOut.ImageSource.Length > 0;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Fetching spot body failed for {0}: {1}", dto.MessageId, ex.Message);
        }

        if (string.IsNullOrEmpty(dto.Description))
        {
            dto.Description = "<p><em>Geen omschrijving beschikbaar of nog niet opgehaald van Usenet.</em></p>";
        }

        return dto;
    }

    public byte[] GetSpotImage(long id, string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId) && id > 0)
            {
                using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
                using ISqlDbTransaction tx = db.BeginReadTransaction();
                using DbCommand cmd = db.CreateCommand(tx);
                cmd.CommandText = "SELECT msgid FROM spots WHERE rowid = @id LIMIT 1";
                var p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = id;
                cmd.Parameters.Add(p);
                messageId = cmd.ExecuteScalar()?.ToString();
            }

            if (!string.IsNullOrEmpty(messageId))
            {
                SpotEx cached = FileCacheManager.Get(messageId);
                if (cached?.ImageSource != null && cached.ImageSource.Length > 0)
                {
                    return cached.ImageSource;
                }
                if (!string.IsNullOrEmpty(cached?.Image) && File.Exists(cached.Image))
                {
                    return File.ReadAllBytes(cached.Image);
                }
                if (!string.IsNullOrEmpty(cached?.PreviewImage) && File.Exists(cached.PreviewImage))
                {
                    return File.ReadAllBytes(cached.PreviewImage);
                }
            }

            // Fetch on demand via Spotnet NNTP engine or cache
            string errorMsg = "";
            SpotEx spotOut = null;
            if (Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, id, messageId, ref spotOut, AppHelper.HeaderSettings(false), ref errorMsg))
            {
                if (spotOut != null)
                {
                    if (spotOut.ImageSource != null && spotOut.ImageSource.Length > 0)
                    {
                        return spotOut.ImageSource;
                    }
                    if (!string.IsNullOrEmpty(spotOut.Image) && File.Exists(spotOut.Image))
                    {
                        return File.ReadAllBytes(spotOut.Image);
                    }
                    if (!string.IsNullOrEmpty(spotOut.PreviewImage) && File.Exists(spotOut.PreviewImage))
                    {
                        return File.ReadAllBytes(spotOut.PreviewImage);
                    }
                    try
                    {
                        byte[] fullImg = ImageHelper.LoadSpotFullImage(spotOut);
                        if (fullImg != null && fullImg.Length > 0)
                        {
                            FileCacheManager.Save(spotOut, fullImg);
                            return fullImg;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to get image for spot {0}: {1}", id, ex.Message);
        }
        return null;
    }

    public void ToggleFavorite(string messageId, bool isFavorite)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return;
        if (isFavorite)
        {
            Favorites.Add(messageId);
        }
        else
        {
            Favorites.Remove(messageId);
        }
    }

    public List<FilterDto> GetFilters()
    {
        var result = new List<FilterDto>();
        try
        {
            // 1. Try reading from active in-memory FilterRoot if available
            var winVm = ((ViewModelLocator)System.Windows.Application.Current?.Resources["Locator"])?.MainWindow;
            if (winVm?.FiltersDb?.FiltersRoot != null && winVm.FiltersDb.FiltersRoot.Children.Count > 0)
            {
                foreach (var f in winVm.FiltersDb.FiltersRoot.Children)
                {
                    if (f.IsVisible)
                    {
                        var dto = MapFilterViewModel(f);
                        if (dto != null) result.Add(dto);
                    }
                }
                if (result.Count > 0) return result;
            }
        }
        catch { }

        // 2. Fallback to parsing filters.xml
        try
        {
            string filterFolder = Settings.Default.Filter.IsNullOrEmpty() ? "Aangepast" : Settings.Default.Filter;
            string filtersXmlPath = Path.Combine(AppHelper.FiltersFolder, filterFolder, "filters.xml");
            if (!File.Exists(filtersXmlPath))
            {
                filtersXmlPath = Path.Combine(AppHelper.FiltersFolder, "Aangepast", "filters.xml");
            }
            if (!File.Exists(filtersXmlPath))
            {
                filtersXmlPath = Path.Combine(AppHelper.FiltersFolder, "Geavanceerd NL", "filters.xml");
            }

            if (File.Exists(filtersXmlPath))
            {
                var doc = new XmlDocument();
                doc.Load(filtersXmlPath);
                var root = doc.DocumentElement;
                if (root != null)
                {
                    foreach (XmlNode node in root.ChildNodes)
                    {
                        if (node is XmlElement el && el.Name.Equals("Filter", StringComparison.OrdinalIgnoreCase))
                        {
                            var dto = ParseFilterXmlNode(el, "");
                            if (dto != null) result.Add(dto);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to parse filters.xml: {0}", ex.Message);
        }

        // If still empty, return standard categories
        if (result.Count == 0)
        {
            result = GetDefaultFilterDtos();
        }

        return result;
    }

    private FilterDto MapFilterViewModel(FilterViewModel vm, string parentPath = "")
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? vm.Name : $"{parentPath}/{vm.Name}";
        string id = vm.Id.IsNullOrEmpty() ? AppHelper.MakeMd5(currentPath) : vm.Id;
        string query = vm.Query ?? "";
        if (!string.IsNullOrWhiteSpace(query))
        {
            _filterQueries[id] = CleanFilterQuery(query);
        }

        var dto = new FilterDto
        {
            Id = id,
            Name = vm.Name,
            Query = query,
            Icon = GetFilterIcon(vm.Name)
        };

        if (vm.Children != null)
        {
            foreach (var child in vm.Children)
            {
                if (child.IsVisible)
                {
                    var childDto = MapFilterViewModel(child, currentPath);
                    if (childDto != null) dto.Children.Add(childDto);
                }
            }
        }
        return dto;
    }

    private FilterDto ParseFilterXmlNode(XmlElement el, string parentPath)
    {
        string name = el.GetAttribute("Name");
        if (string.IsNullOrWhiteSpace(name) || el.GetAttribute("Visible").Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string currentPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
        string id = AppHelper.MakeMd5(currentPath);

        string query = el.GetAttribute("Query");
        if (string.IsNullOrWhiteSpace(query))
        {
            query = el.InnerText?.Trim() ?? "";
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            _filterQueries[id] = CleanFilterQuery(query);
        }

        var dto = new FilterDto
        {
            Id = id,
            Name = name,
            Query = query,
            Icon = GetFilterIcon(name)
        };

        foreach (XmlNode childNode in el.ChildNodes)
        {
            if (childNode is XmlElement childEl && childEl.Name.Equals("Filter", StringComparison.OrdinalIgnoreCase))
            {
                var childDto = ParseFilterXmlNode(childEl, currentPath);
                if (childDto != null) dto.Children.Add(childDto);
            }
        }

        return dto;
    }

    public static string CleanFilterQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "";
        string clean = query.Trim();
        // Rewrite legacy FTS docid -> rowid
        clean = Regex.Replace(clean, @"\bdocid\b", "rowid", RegexOptions.IgnoreCase);
        // Replace special tags
        clean = clean.Replace("[SN:NEW]", "0");
        clean = clean.Replace("[SN:DATE]", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        clean = clean.Replace("spots.msgid in favorieten", "spots.cats LIKE '%f1%'");
        clean = clean.Replace("cat = 1 AND cats MATCH '1b4 OR 1d11'", "cat = 6");
        return clean;
    }

    private static string GetFilterIcon(string name)
    {
        string lower = (name ?? "").ToLowerInvariant();
        if (lower.Contains("film") || lower.Contains("movie") || lower.Contains("beeld")) return "🎬";
        if (lower.Contains("serie") || lower.Contains("tv")) return "📺";
        if (lower.Contains("boek") || lower.Contains("book")) return "📚";
        if (lower.Contains("muziek") || lower.Contains("audio") || lower.Contains("geluid") || lower.Contains("mp3") || lower.Contains("flac")) return "🎵";
        if (lower.Contains("spel") || lower.Contains("game")) return "🎮";
        if (lower.Contains("app") || lower.Contains("software")) return "💻";
        if (lower.Contains("erotiek") || lower.Contains("xxx")) return "🔞";
        if (lower.Contains("favoriet")) return "⭐";
        if (lower.Contains("nieuw")) return "✨";
        if (lower.Contains("24 uur") || lower.Contains("vandaag")) return "🕒";
        return "📁";
    }

    private static List<FilterDto> GetDefaultFilterDtos()
    {
        return new List<FilterDto>
        {
            new FilterDto { Id = "cat_1", Name = "Films", Query = "cat = 1", Icon = "🎬" },
            new FilterDto { Id = "cat_6", Name = "Series", Query = "cat = 6", Icon = "📺" },
            new FilterDto { Id = "cat_5", Name = "Boeken", Query = "cat = 5", Icon = "📚" },
            new FilterDto { Id = "cat_2", Name = "Muziek", Query = "cat = 2", Icon = "🎵" },
            new FilterDto { Id = "cat_3", Name = "Spellen", Query = "cat = 3", Icon = "🎮" },
            new FilterDto { Id = "cat_4", Name = "Applicaties", Query = "cat = 4", Icon = "💻" },
            new FilterDto { Id = "cat_9", Name = "Erotiek", Query = "cat = 9", Icon = "🔞" }
        };
    }

    public static void AddLocalComment(string spotMessageId, SpotCommentDto comment)
    {
        if (string.IsNullOrEmpty(spotMessageId) || comment == null) return;
        _localUserComments.AddOrUpdate(spotMessageId,
            new List<SpotCommentDto> { comment },
            (key, existing) => { existing.Add(comment); return existing; });
    }

    public List<SpotCommentDto> GetSpotComments(long spotId, string messageId)
    {
        var list = new List<SpotCommentDto>();
        try
        {
            if (string.IsNullOrEmpty(messageId) && spotId > 0)
            {
                using ISqlDb dbSpots = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
                using ISqlDbTransaction txSpots = dbSpots.BeginReadTransaction();
                using DbCommand cmdSpots = dbSpots.CreateCommand(txSpots);
                cmdSpots.CommandText = "SELECT msgid FROM spots WHERE rowid = @id LIMIT 1";
                var p = cmdSpots.CreateParameter();
                p.ParameterName = "@id";
                p.Value = spotId;
                cmdSpots.Parameters.Add(p);
                messageId = cmdSpots.ExecuteScalar()?.ToString();
            }

            if (string.IsNullOrEmpty(messageId)) return list;

            // Include local user comments posted in this session
            if (_localUserComments.TryGetValue(messageId, out var userComments))
            {
                list.AddRange(userComments);
            }

            string full = SpotHelper.MakeMsg(messageId, tag: false);
            string prefix = full.Contains("@") ? full.Substring(0, full.IndexOf("@", StringComparison.Ordinal)) : full;
            prefix = prefix.Replace("'", "").Trim();

            var articleIds = new List<long>();
            try
            {
                using ISqlDb dbComments = SqlDbFactory.CreateSqlDbComments(isReadOnly: true);
                using ISqlDbTransaction txComments = dbComments.BeginReadTransaction();
                using DbCommand cmdComments = dbComments.CreateCommand(txComments);
                cmdComments.CommandText = $"SELECT rowid FROM comments WHERE spot MATCH '{prefix}' ORDER BY rowid ASC LIMIT 100";
                using DbDataReader reader = dbComments.ExecuteReader(cmdComments);
                while (reader != null && reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        articleIds.Add(reader.GetInt64(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Reading comments DB for {0}: {1}", prefix, ex.Message);
            }

            if (articleIds.Count > 0)
            {
                var loaded = new ConcurrentBag<Comment>();
                var targetArticles = articleIds.TakeLast(35).ToList();
                var nntpSettings = Sys.MainWindow != null
                    ? Sys.MainWindow.CommentSettings(bIncludeLast: false)
                    : new NntpSettings
                    {
                        BlackList = BlackAndWhite.BlackList(),
                        WhiteList = BlackAndWhite.WhiteList(),
                        TrustedKeys = AppHelper.LoadKeys(),
                        GroupName = Settings.Default.ReplyGroup,
                        CheckSignatures = Settings.Default.CheckSignatures
                    };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var task = Comments.StartLoadCommentsBody(AppHelper.HeaderPhuse, targetArticles, nntpSettings,
                    null, (c) => loaded.Add(c), cts.Token);
                task.Wait(TimeSpan.FromSeconds(3));

                foreach (var c in loaded.OrderBy(x => x.Article))
                {
                    if (list.Any(x => x.RawBody == c.Body && x.Sender == c.From)) continue;

                    list.Add(new SpotCommentDto
                    {
                        Id = c.Article,
                        SpotMessageId = messageId,
                        Sender = c.From ?? "Spotnetter",
                        DateFormatted = c.Created != default ? FormatDate(new DateTimeOffset(c.Created).ToUnixTimeSeconds()) : "",
                        BodyHtml = SanitizeDescriptionToHtml(c.Body),
                        RawBody = c.Body ?? "",
                        IsAuthor = false,
                        IsVerified = c.User?.ValidSignature ?? false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetSpotComments failed: {0}", ex.Message);
        }
        return list;
    }

    public (bool success, string error, SpotCommentDto comment) PostSpotComment(long spotId, string messageId, string nickname, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, "Reactie mag niet leeg zijn.", null);
        }

        try
        {
            string spotTitle = "Spot";
            if (string.IsNullOrEmpty(messageId) && spotId > 0)
            {
                using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
                using ISqlDbTransaction tx = db.BeginReadTransaction();
                using DbCommand cmd = db.CreateCommand(tx);
                cmd.CommandText = "SELECT msgid, subject FROM spots WHERE rowid = @id LIMIT 1";
                var p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = spotId;
                cmd.Parameters.Add(p);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    messageId = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    spotTitle = reader.IsDBNull(1) ? "Spot" : reader.GetString(1);
                }
            }

            if (string.IsNullOrEmpty(messageId))
            {
                return (false, "Spot niet gevonden.", null);
            }

            if (string.IsNullOrWhiteSpace(nickname))
            {
                nickname = Settings.Default.Nickname.IsNullOrEmpty() ? "Spotnetter" : Settings.Default.Nickname;
            }

            nickname = AppHelper.StripNonAlphaNumericCharacters(nickname);
            if (!string.IsNullOrEmpty(nickname))
            {
                Settings.Default.Nickname = nickname;
                Settings.Default.Save();
            }

            string articleId = AppHelper.CreateMsgId(messageId.Split('@')[0].Replace(".", "").Replace("<", ""));
            string error = "";

            bool posted = Spots.CreateComment(
                AppHelper.UploadPhuse,
                nickname,
                body,
                Settings.Default.ReplyGroup,
                messageId,
                spotTitle,
                AppHelper.GetAvatar(),
                UserKeyHelper.GetKey(),
                articleId,
                ref error);

            if (posted)
            {
                var newComment = new SpotCommentDto
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    SpotMessageId = messageId,
                    Sender = nickname,
                    DateFormatted = "Zojuist",
                    BodyHtml = SanitizeDescriptionToHtml(body),
                    RawBody = body,
                    IsAuthor = true,
                    IsVerified = true
                };

                AddLocalComment(messageId, newComment);
                return (true, "", newComment);
            }
            else
            {
                return (false, string.IsNullOrEmpty(error) ? "Plaatsen van reactie mislukt." : error, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error("PostSpotComment failed: {0}", ex.Message);
            return (false, ex.Message, null);
        }
    }

    public static string SanitizeDescriptionToHtml(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return "";

        // 1. Normalize linebreaks and [br] before encoding
        string text = rawBody.Replace("\r\n", "\n").Replace("\r", "\n");
        text = Regex.Replace(text, @"\[br\s*/?\]", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[/br\]", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // 2. HTML encode everything to prevent XSS injection
        string html = WebUtility.HtmlEncode(text);

        // 3. Convert BBCode to safe HTML elements
        html = Regex.Replace(html, @"\[b\](.*?)\[/b\]", "<strong>$1</strong>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[i\](.*?)\[/i\]", "<em>$1</em>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[u\](.*?)\[/u\]", "<u>$1</u>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[s\](.*?)\[/s\]", "<s>$1</s>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[center\](.*?)\[/center\]", "<div class=\"text-center\">$1</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[quote=([^\]]+)\](.*?)\[/quote\]", "<blockquote><cite>$1 schreef:</cite>$2</blockquote>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"\[quote\](.*?)\[/quote\]", "<blockquote>$1</blockquote>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert color tags (only allow safe hex or standard names)
        html = Regex.Replace(html, @"\[color=(#[0-9a-fA-F]{3,6}|[a-zA-Z]+)\](.*?)\[/color\]", "<span style=\"color:$1\">$2</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert size tags
        html = Regex.Replace(html, @"\[size=(\d+)\](.*?)\[/size\]", "<span style=\"font-size:$1pt\">$2</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert links (safe URLs only)
        html = Regex.Replace(html, @"\[url=(https?://[^\]]+)\](.*?)\[/url\]", "<a href=\"$1\" target=\"_blank\" rel=\"noopener noreferrer\">$2</a>", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\[url\](https?://[^\]]+)\[/url\]", "<a href=\"$1\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>", RegexOptions.IgnoreCase);

        // Convert images (ensure safe https or http src)
        html = Regex.Replace(html, @"\[img\](https?://[^\]]+)\[/img\]", "<img src=\"$1\" class=\"spot-content-image\" loading=\"lazy\" alt=\"Afbeelding\" />", RegexOptions.IgnoreCase);

        // 4. Smileys and Emojis replacement
        var smileys = new (string pattern, string emoji)[]
        {
            (@"\[img=smile\]", "😊"),
            (@"\[img=wink\]", "😉"),
            (@"\[img=biggrin\]", "😃"),
            (@"\[img=tongue\]", "😜"),
            (@"\[img=sad\]", "🙁"),
            (@"\[img=shock\]", "😮"),
            (@"\[img=thumb\]", "👍"),
            (@"\[img=heart\]", "❤️"),
            (@"\[img=beer\]", "🍺"),
            (@"\[img=clap\]", "👏"),
            (@":-\)", "😊"),
            (@":\)", "😊"),
            (@":-D", "😃"),
            (@":D", "😃"),
            (@";-\)", "😉"),
            (@";\)", "😉"),
            (@":-P", "😜"),
            (@":P", "😜"),
            (@":-p", "😜"),
            (@":p", "😜"),
            (@":-\(", "🙁"),
            (@":\(", "🙁"),
            (@":-o", "😮"),
            (@":o", "😮"),
            (@":-O", "😮"),
            (@":O", "😮"),
            (@"\(Y\)", "👍"),
            (@"\(y\)", "👍")
        };

        foreach (var (pattern, emoji) in smileys)
        {
            html = Regex.Replace(html, pattern, emoji, RegexOptions.IgnoreCase);
        }

        // 5. Linebreaks: convert \n to <br/>
        html = html.Replace("\n", "<br/>");

        // 6. Collapse 3 or more consecutive <br/> into 2
        html = Regex.Replace(html, @"(<br/>\s*){3,}", "<br/><br/>");

        return html;
    }
}
