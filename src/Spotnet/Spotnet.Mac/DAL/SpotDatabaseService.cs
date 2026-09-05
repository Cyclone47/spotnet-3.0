using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NLog;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.DAL;

/// <summary>
/// High-performance data service for Spotnet SQLite database using Microsoft.Data.Sqlite and FTS5.
/// </summary>
public sealed class SpotDatabaseService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly MacSqliteDb _db;

    public SpotDatabaseService(MacSqliteDb db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task EnsureCreatedAsync()
    {
        return Task.Run(() => _db.InitializeSchema());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Dynamic WHERE clause with parameterized values")]
    public async Task<List<SpotItem>> QuerySpotsAsync(
        string? ftsQuery = null,
        int? categoryId = null,
        string? subcatTag = null,
        long? afterDate = null,
        int skip = 0,
        int take = 50,
        string sortDirection = "DESC")
    {
        var spots = new List<SpotItem>();
        using var conn = _db.OpenConnection(readOnly: true);

        string sql;
        bool hasFts = !string.IsNullOrWhiteSpace(ftsQuery);
        bool hasCat = categoryId.HasValue && categoryId.Value > 0;
        bool hasSubcat = !string.IsNullOrWhiteSpace(subcatTag);
        bool hasAfter = afterDate.HasValue;

        string order = sortDirection.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        if (hasFts)
        {
            // FTS5 join query
            sql = @"
                SELECT s.rowid, s.key, s.cat, s.subcat, s.extcat, s.date, s.filesize, s.cats, s.sender, s.tag, s.subject, s.msgid, s.modulus
                FROM spots s
                INNER JOIN search ON s.rowid = search.rowid
                WHERE search MATCH @ftsQuery";

            if (hasCat)   sql += " AND s.cat = @cat";
            if (hasSubcat) sql += " AND s.cats LIKE @subcat";
            if (hasAfter)  sql += " AND s.date >= @afterDate";

            sql += $" ORDER BY s.date {order} LIMIT @take OFFSET @skip;";
        }
        else
        {
            // Regular query
            sql = @"
                SELECT rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus
                FROM spots
                WHERE 1=1";

            if (hasCat)   sql += " AND cat = @cat";
            if (hasSubcat) sql += " AND cats LIKE @subcat";
            if (hasAfter)  sql += " AND date >= @afterDate";

            sql += $" ORDER BY date {order} LIMIT @take OFFSET @skip;";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (hasFts)
        {
            // Sanitize query for FTS5 syntax
            string sanitized = SanitizeFtsQuery(ftsQuery!);
            cmd.Parameters.AddWithValue("@ftsQuery", sanitized);
        }

        if (hasCat)
        {
            cmd.Parameters.AddWithValue("@cat", categoryId!.Value);
        }

        if (hasSubcat)
        {
            cmd.Parameters.AddWithValue("@subcat", $"%{subcatTag}%");
        }

        if (hasAfter)
        {
            cmd.Parameters.AddWithValue("@afterDate", afterDate!.Value);
        }

        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@skip", skip);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            spots.Add(MapSpotRow(reader));
        }

        return spots;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Dynamic WHERE clause with parameterized values")]
    public async Task<int> CountSpotsAsync(string? ftsQuery = null, int? categoryId = null, string? subcatTag = null, long? afterDate = null)
    {
        using var conn = _db.OpenConnection(readOnly: true);

        string sql;
        bool hasFts = !string.IsNullOrWhiteSpace(ftsQuery);
        bool hasCat = categoryId.HasValue && categoryId.Value > 0;
        bool hasSubcat = !string.IsNullOrWhiteSpace(subcatTag);
        bool hasAfter = afterDate.HasValue;

        if (hasFts)
        {
            sql = @"
                SELECT COUNT(*)
                FROM spots s
                INNER JOIN search ON s.rowid = search.rowid
                WHERE search MATCH @ftsQuery";

            if (hasCat)    sql += " AND s.cat = @cat";
            if (hasSubcat) sql += " AND s.cats LIKE @subcat";
            if (hasAfter)  sql += " AND s.date >= @afterDate";
        }
        else
        {
            sql = "SELECT COUNT(*) FROM spots WHERE 1=1";
            if (hasCat)    sql += " AND cat = @cat";
            if (hasSubcat) sql += " AND cats LIKE @subcat";
            if (hasAfter)  sql += " AND date >= @afterDate";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (hasFts)
        {
            cmd.Parameters.AddWithValue("@ftsQuery", SanitizeFtsQuery(ftsQuery!));
        }

        if (hasCat)
        {
            cmd.Parameters.AddWithValue("@cat", categoryId!.Value);
        }

        if (hasSubcat)
        {
            cmd.Parameters.AddWithValue("@subcat", $"%{subcatTag}%");
        }

        if (hasAfter)
        {
            cmd.Parameters.AddWithValue("@afterDate", afterDate!.Value);
        }

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Runs a Spotnet filter expression (the bundled advanced-filter mini-language),
    /// optionally intersected with a free-text FTS query from the search box.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Filter text is compiled by FilterExpressionCompiler; literals are parameterized")]
    public async Task<List<SpotItem>> QueryByFilterAsync(
        string? filterQuery,
        string? searchText = null,
        int skip = 0,
        int take = 100,
        string sortDirection = "DESC")
    {
        var spots = new List<SpotItem>();
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string order = sortDirection.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        string where = BuildFilterWhere(filterQuery, searchText, cmd);

        cmd.CommandText =
            $"SELECT {FilterQueryBuilder.SpotColumns} FROM spots{where} ORDER BY date {order} LIMIT @take OFFSET @skip;";
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@skip", skip);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            spots.Add(MapSpotRow(reader));
        }
        return spots;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Filter text is compiled by FilterExpressionCompiler; literals are parameterized")]
    public async Task<int> CountByFilterAsync(string? filterQuery, string? searchText = null)
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string where = BuildFilterWhere(filterQuery, searchText, cmd);
        cmd.CommandText = $"SELECT COUNT(1) FROM spots{where};";

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Counts only the spots a filter matches that arrived after the last sync — the
    /// number the sidebar badges show. Windows counts the same way
    /// (SpotProvider.CreateQueryCountNew), which is why its badges read 3 where the
    /// filter itself holds thousands of spots.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Filter text is compiled by FilterExpressionCompiler; literals are parameterized")]
    public async Task<int> CountNewByFilterAsync(string? filterQuery)
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string where = BuildFilterWhere(filterQuery, null, cmd);
        cmd.CommandText = $"SELECT COUNT(1) FROM spots{where} AND rowid > @rowNew;";
        cmd.Parameters.AddWithValue("@rowNew", RowNew);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Composes the WHERE clause shared by <see cref="QueryByFilterAsync"/> and
    /// <see cref="CountByFilterAsync"/>, binding every literal as a parameter on
    /// <paramref name="cmd"/>. Returns "" when nothing constrains the query.
    /// </summary>
    private string BuildFilterWhere(string? filterQuery, string? searchText, SqliteCommand cmd)
    {
        var clauses = new List<string>();
        var values = new List<SqlValue>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (!string.IsNullOrWhiteSpace(filterQuery))
        {
            try
            {
                string? predicate = FilterQueryBuilder.BuildPredicate(filterQuery, now, RowNew, values);
                if (predicate != null)
                {
                    clauses.Add(predicate);
                }
            }
            catch (FormatException ex)
            {
                // A filter the user edited by hand can be malformed. Windows logs and
                // falls back to an unfiltered list rather than failing the window.
                Log.Warn("Ignoring unsupported filter expression '{0}': {1}", filterQuery, ex.Message);
                values.Clear();
            }
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            clauses.Add("rowid IN (SELECT rowid FROM search WHERE search MATCH @fts)");
            cmd.Parameters.AddWithValue("@fts", SanitizeFtsQuery(searchText));
        }

        foreach (var value in values)
        {
            cmd.Parameters.AddWithValue(value.Name, value.Value);
        }

        clauses.Add(FilterQueryBuilder.KeyGuard);
        return " WHERE " + string.Join(" AND ", clauses);
    }

    /// <summary>
    /// Highest rowid at the end of the previous sync; spots above it are what the
    /// "Nieuw" filter ([SN:NEW]) selects. Cached after the first read.
    /// </summary>
    public long RowNew { get; private set; }

    public async Task<long> LoadRowNewAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM userinfo WHERE field='rownew' LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync();
        RowNew = result != null && long.TryParse(result.ToString(), out var val) ? val : 0;
        return RowNew;
    }

    /// <summary>Marks the current end of the table as the "already seen" watermark.</summary>
    public async Task MarkSpotsSeenAsync()
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM userinfo WHERE field='rownew';
            INSERT INTO userinfo (field, value) SELECT 'rownew', IFNULL(MAX(rowid), 0) FROM spots;";
        await cmd.ExecuteNonQueryAsync();
        await LoadRowNewAsync();
    }

    public async Task<SpotItem?> GetSpotByMsgIdAsync(string msgId)
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus
            FROM spots
            WHERE msgid = @msgid
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@msgid", msgId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapSpotRow(reader);
        }
        return null;
    }

    public async Task<List<CommentItem>> GetCommentsAsync(string spotMsgId)
    {
        var comments = new List<CommentItem>();
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rowid, msgid, date, sender, rating, spotmsgid, body
            FROM comments
            WHERE spotmsgid = @spotmsgid
            ORDER BY date ASC;";
        cmd.Parameters.AddWithValue("@spotmsgid", spotMsgId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            comments.Add(new CommentItem
            {
                Id = reader.GetInt64(0),
                MsgId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Date = reader.GetInt64(2),
                Sender = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                SpotMsgId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Body = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }
        return comments;
    }

    /// <summary>
    /// Adds reply-group articles to the comment index. Duplicate article numbers are
    /// skipped, so a re-scan of an overlapping range is harmless.
    /// </summary>
    public async Task<int> IndexCommentArticlesAsync(IEnumerable<(long article, string msgId)> articles)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO commentindex(rowid, msgid) SELECT @rowid, @msgid " +
                          "WHERE NOT EXISTS (SELECT 1 FROM commentindex WHERE rowid = @rowid);";
        var pRow = cmd.Parameters.Add("@rowid", SqliteType.Integer);
        var pMsg = cmd.Parameters.Add("@msgid", SqliteType.Text);

        int inserted = 0;
        foreach (var (article, msgId) in articles)
        {
            if (article <= 0 || string.IsNullOrWhiteSpace(msgId)) continue;
            pRow.Value = article;
            pMsg.Value = msgId;
            inserted += await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
        return inserted;
    }

    /// <summary>
    /// Reply-group article numbers whose Message-ID carries this spot's prefix — the
    /// comments on that spot. Mirrors SpotWebView2Page.GetCommentsFromDb.
    /// </summary>
    public async Task<List<long>> FindCommentArticlesAsync(string spotMsgId)
    {
        var articles = new List<long>();

        string full = spotMsgId.Trim('<', '>');
        int at = full.IndexOf('@', StringComparison.Ordinal);
        string prefix = at > 0 ? full[..at] : full;
        // FTS5 would read punctuation as syntax; the prefix is alphanumeric anyway.
        prefix = new string(prefix.Where(char.IsLetterOrDigit).ToArray());
        if (prefix.Length == 0) return articles;

        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rowid FROM commentindex WHERE msgid MATCH @prefix ORDER BY rowid ASC;";
        cmd.Parameters.AddWithValue("@prefix", prefix);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            articles.Add(reader.GetInt64(0));
        }
        return articles;
    }

    /// <summary>Highest reply-group article already indexed.</summary>
    public async Task<long> GetLastIndexedCommentAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM userinfo WHERE field='last_comments' LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync();
        return result != null && long.TryParse(result.ToString(), out var val) ? val : 0;
    }

    public async Task SetLastIndexedCommentAsync(long articleId)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM userinfo WHERE field='last_comments';
            INSERT INTO userinfo (field, value) VALUES ('last_comments', @val);";
        cmd.Parameters.AddWithValue("@val", articleId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertSpotsAsync(IEnumerable<SpotItem> spots)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();

        int inserted = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO spots (key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus)
            VALUES (@key, @cat, @subcat, @extcat, @date, @filesize, @cats, @sender, @tag, @subject, @msgid, @modulus);";

        var pKey = cmd.Parameters.Add("@key", SqliteType.Integer);
        var pCat = cmd.Parameters.Add("@cat", SqliteType.Integer);
        var pSubcat = cmd.Parameters.Add("@subcat", SqliteType.Integer);
        var pExtcat = cmd.Parameters.Add("@extcat", SqliteType.Integer);
        var pDate = cmd.Parameters.Add("@date", SqliteType.Integer);
        var pFilesize = cmd.Parameters.Add("@filesize", SqliteType.Integer);
        var pCats = cmd.Parameters.Add("@cats", SqliteType.Text);
        var pSender = cmd.Parameters.Add("@sender", SqliteType.Text);
        var pTag = cmd.Parameters.Add("@tag", SqliteType.Text);
        var pSubject = cmd.Parameters.Add("@subject", SqliteType.Text);
        var pMsgid = cmd.Parameters.Add("@msgid", SqliteType.Text);
        var pModulus = cmd.Parameters.Add("@modulus", SqliteType.Text);

        foreach (var spot in spots)
        {
            pKey.Value = spot.Key;
            pCat.Value = spot.Category;
            pSubcat.Value = spot.Subcat;
            pExtcat.Value = spot.Extcat;
            pDate.Value = spot.Date;
            pFilesize.Value = spot.Filesize;
            pCats.Value = (object?)spot.Cats ?? DBNull.Value;
            pSender.Value = (object?)spot.Sender ?? DBNull.Value;
            pTag.Value = (object?)spot.Tag ?? DBNull.Value;
            pSubject.Value = (object?)spot.Subject ?? DBNull.Value;
            pMsgid.Value = (object?)spot.MsgId ?? DBNull.Value;
            pModulus.Value = (object?)spot.Modulus ?? DBNull.Value;

            inserted += await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
        return inserted;
    }

    public async Task<int> InsertCommentsAsync(IEnumerable<CommentItem> comments)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();

        int inserted = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO comments (msgid, date, sender, rating, spotmsgid, body)
            VALUES (@msgid, @date, @sender, @rating, @spotmsgid, @body);";

        var pMsgid = cmd.Parameters.Add("@msgid", SqliteType.Text);
        var pDate = cmd.Parameters.Add("@date", SqliteType.Integer);
        var pSender = cmd.Parameters.Add("@sender", SqliteType.Text);
        var pRating = cmd.Parameters.Add("@rating", SqliteType.Integer);
        var pSpotMsgid = cmd.Parameters.Add("@spotmsgid", SqliteType.Text);
        var pBody = cmd.Parameters.Add("@body", SqliteType.Text);

        foreach (var comment in comments)
        {
            pMsgid.Value = (object?)comment.MsgId ?? DBNull.Value;
            pDate.Value = comment.Date;
            pSender.Value = (object?)comment.Sender ?? DBNull.Value;
            pRating.Value = comment.Rating;
            pSpotMsgid.Value = (object?)comment.SpotMsgId ?? DBNull.Value;
            pBody.Value = (object?)comment.Body ?? DBNull.Value;

            inserted += await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
        return inserted;
    }

    public async Task<Dictionary<int, int>> GetCategoryCountsAsync()
    {
        var counts = new Dictionary<int, int>();
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cat, COUNT(*) FROM spots GROUP BY cat;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetInt32(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    private static SpotItem MapSpotRow(DbDataReader reader)
    {
        return new SpotItem
        {
            Id = reader.GetInt64(0),
            Key = reader.GetInt32(1),
            Category = reader.GetInt32(2),
            Subcat = reader.GetInt32(3),
            Extcat = reader.GetInt32(4),
            Date = reader.GetInt64(5),
            Filesize = reader.GetInt64(6),
            Cats = reader.IsDBNull(7) ? "" : reader.GetString(7),
            Sender = reader.IsDBNull(8) ? "" : reader.GetString(8),
            Tag = reader.IsDBNull(9) ? "" : reader.GetString(9),
            Subject = reader.IsDBNull(10) ? "" : reader.GetString(10),
            MsgId = reader.IsDBNull(11) ? "" : reader.GetString(11),
            Modulus = reader.IsDBNull(12) ? "" : reader.GetString(12)
        };
    }

    public async Task<long> GetLastSyncedArticleAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM userinfo WHERE field='last_headers' LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync();
        if (result != null && long.TryParse(result.ToString(), out var val))
        {
            return val;
        }
        return 0;
    }

    public async Task SetLastSyncedArticleAsync(long articleId)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM userinfo WHERE field='last_headers';
            INSERT INTO userinfo (field, value) VALUES ('last_headers', @val);";
        cmd.Parameters.AddWithValue("@val", articleId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetUserKeyXmlAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key FROM userkey LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task SetUserKeyXmlAsync(string keyXml)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM userkey;
            INSERT INTO userkey (key) VALUES (@key);";
        cmd.Parameters.AddWithValue("@key", keyXml);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string SanitizeFtsQuery(string input)
    {
        // Quote terms or clean up FTS5 special operators
        var words = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var terms = new List<string>();
        foreach (var word in words)
        {
            string clean = word.Replace("\"", "").Replace("'", "").Replace("*", "");
            if (!string.IsNullOrWhiteSpace(clean))
            {
                terms.Add($"\"{clean}\"*");
            }
        }
        return terms.Count > 0 ? string.Join(" ", terms) : "\"\"";
    }

    /// <summary>
    /// Performs database maintenance: checkpoints WAL, reindexes, validates integrity,
    /// rebuilds FTS5 search index, and vacuums.
    /// </summary>
    public async Task<(bool success, string message)> QuickRepairAsync()
    {
        try
        {
            using var conn = _db.OpenConnection(readOnly: false);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "REINDEX;";
                await cmd.ExecuteNonQueryAsync();
            }

            string checkResult = "ok";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check(1);";
                var result = await cmd.ExecuteScalarAsync();
                checkResult = Convert.ToString(result) ?? "ok";
            }

            // Rebuild FTS5 search table
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO search(search) VALUES('rebuild');";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "VACUUM;";
                await cmd.ExecuteNonQueryAsync();
            }

            Log.Info("Database quick repair completed with status: {0}", checkResult);
            return (true, $"Database succesvol geoptimaliseerd en hersteld. Status: {checkResult}");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Database quick repair failed");
            return (false, $"Fout tijdens databaseherstel: {ex.Message}");
        }
    }
}
