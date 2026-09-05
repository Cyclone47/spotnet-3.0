using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;

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
        string sortDirection = "DESC",
        string sortColumn = SpotSort.DefaultColumn,
        bool hideBlacklisted = false,
        bool showTrustedOnly = false,
        bool showErotica = false,
        int spamReportsThreshold = 0)
    {
        var spots = new List<SpotItem>();
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string order = sortDirection.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        string where = BuildFilterWhere(filterQuery, searchText, cmd, hideBlacklisted, showTrustedOnly, showErotica, spamReportsThreshold);

        cmd.CommandText =
            $"SELECT {FilterQueryBuilder.SpotColumns} FROM spots LEFT JOIN spamgroup s USING (msgid){where} ORDER BY {SpotSort.ToSqlColumn(sortColumn)} {order}, spots.rowid {order} LIMIT @take OFFSET @skip;";
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
    public async Task<int> CountByFilterAsync(
        string? filterQuery,
        string? searchText = null,
        bool hideBlacklisted = false,
        bool showTrustedOnly = false,
        bool showErotica = false,
        int spamReportsThreshold = 0)
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string where = BuildFilterWhere(filterQuery, searchText, cmd, hideBlacklisted, showTrustedOnly, showErotica, spamReportsThreshold);
        cmd.CommandText = $"SELECT COUNT(1) FROM spots LEFT JOIN spamgroup s USING (msgid){where};";

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
    public async Task<int> CountNewByFilterAsync(
        string? filterQuery,
        bool hideBlacklisted = false,
        bool showTrustedOnly = false,
        bool showErotica = false,
        int spamReportsThreshold = 0)
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();

        string where = BuildFilterWhere(filterQuery, null, cmd, hideBlacklisted, showTrustedOnly, showErotica, spamReportsThreshold);
        cmd.CommandText = $"SELECT COUNT(1) FROM spots LEFT JOIN spamgroup s USING (msgid){where} AND spots.rowid > @rowNew;";
        cmd.Parameters.AddWithValue("@rowNew", RowNew);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Synchronizes the in-memory blacklists and whitelists into SQLite tables.
    /// Uses a single transaction for maximum speed.
    /// </summary>
    public async Task SyncTrustListsAsync(
        IReadOnlyCollection<string> blackModuli,
        IReadOnlyCollection<string> blackMsgIds,
        IReadOnlyCollection<string> whiteModuli,
        IReadOnlyCollection<string> whiteMsgIds)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM blacklist; DELETE FROM whitelist;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (blackModuli.Count > 0 || blackMsgIds.Count > 0)
            {
                using var insertBlack = conn.CreateCommand();
                insertBlack.Transaction = tx;
                insertBlack.CommandText = "INSERT OR IGNORE INTO blacklist (key, type) VALUES (@key, @type);";
                var keyParam = insertBlack.Parameters.Add("@key", SqliteType.Text);
                var typeParam = insertBlack.Parameters.Add("@type", SqliteType.Integer);

                typeParam.Value = 1; // 1 = modulus
                foreach (var mod in blackModuli)
                {
                    if (string.IsNullOrWhiteSpace(mod)) continue;
                    keyParam.Value = mod;
                    await insertBlack.ExecuteNonQueryAsync();
                }

                typeParam.Value = 2; // 2 = msgid
                foreach (var msg in blackMsgIds)
                {
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    keyParam.Value = msg;
                    await insertBlack.ExecuteNonQueryAsync();
                }
            }

            if (whiteModuli.Count > 0 || whiteMsgIds.Count > 0)
            {
                using var insertWhite = conn.CreateCommand();
                insertWhite.Transaction = tx;
                insertWhite.CommandText = "INSERT OR IGNORE INTO whitelist (key, type) VALUES (@key, @type);";
                var keyParam = insertWhite.Parameters.Add("@key", SqliteType.Text);
                var typeParam = insertWhite.Parameters.Add("@type", SqliteType.Integer);

                typeParam.Value = 1; // 1 = modulus
                foreach (var mod in whiteModuli)
                {
                    if (string.IsNullOrWhiteSpace(mod)) continue;
                    keyParam.Value = mod;
                    await insertWhite.ExecuteNonQueryAsync();
                }

                typeParam.Value = 2; // 2 = msgid
                foreach (var msg in whiteMsgIds)
                {
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    keyParam.Value = msg;
                    await insertWhite.ExecuteNonQueryAsync();
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Log.Error(ex, "Failed to synchronize trust lists to database.");
            throw;
        }
    }

    /// <summary>
    /// Composes the WHERE clause shared by <see cref="QueryByFilterAsync"/> and
    /// <see cref="CountByFilterAsync"/>, binding every literal as a parameter on
    /// <paramref name="cmd"/>. Returns "" when nothing constrains the query.
    /// </summary>
    private string BuildFilterWhere(
        string? filterQuery,
        string? searchText,
        SqliteCommand cmd,
        bool hideBlacklisted = false,
        bool showTrustedOnly = false,
        bool showErotica = false,
        int spamReportsThreshold = 0)
    {
        var clauses = new List<string>();
        var values = new List<SqlValue>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (spamReportsThreshold > 0)
        {
            clauses.Add("spots.msgid NOT IN (SELECT msgid FROM spamgroup WHERE cnt >= @spamThreshold)");
            cmd.Parameters.AddWithValue("@spamThreshold", spamReportsThreshold);
        }

        if (hideBlacklisted)
        {
            clauses.Add("(spots.modulus NOT IN (SELECT key FROM blacklist WHERE type = 1) AND spots.msgid NOT IN (SELECT key FROM blacklist WHERE type = 2))");
        }

        if (showTrustedOnly)
        {
            clauses.Add("(spots.modulus IN (SELECT key FROM whitelist WHERE type = 1) OR spots.msgid IN (SELECT key FROM whitelist WHERE type = 2) OR spots.date < 1356998400)");
        }

        if (!string.IsNullOrWhiteSpace(filterQuery))
        {
            try
            {
                string? predicate = FilterQueryBuilder.BuildPredicate(filterQuery, now, RowNew, values, showErotica: showErotica);
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
        else if (!showErotica)
        {
            clauses.Add("spots.cat < 9");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            clauses.Add("spots.rowid IN (SELECT rowid FROM search WHERE search MATCH @fts)");
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
            SELECT spots.rowid, spots.key, spots.cat, spots.subcat, spots.extcat, spots.date, spots.filesize, spots.cats, spots.sender, spots.tag, spots.subject, spots.msgid, spots.modulus, IFNULL(s.cnt, 0)
            FROM spots
            LEFT JOIN spamgroup s USING (msgid)
            WHERE spots.msgid = @msgid
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

    /// <summary>Highest report-group article already indexed.</summary>
    public async Task<long> GetLastIndexedSpamReportAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM userinfo WHERE field='last_spamreports' LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync();
        return result != null && long.TryParse(result.ToString(), out var val) ? val : 0;
    }

    public async Task SetLastIndexedSpamReportAsync(long articleId)
    {
        using var conn = _db.OpenConnection(readOnly: false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM userinfo WHERE field='last_spamreports';
            INSERT INTO userinfo (field, value) VALUES ('last_spamreports', @val);";
        cmd.Parameters.AddWithValue("@val", articleId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts spam reports into the database, ignoring duplicate reports from the same modulus,
    /// and updates the aggregated report count in the spamgroup table.
    /// Matches Windows SpotSaver.UpdateSpamReportsDb.
    /// </summary>
    public async Task<int> InsertSpamReportsAsync(IEnumerable<SpamReportItem> reports)
    {
        int count = 0;
        using var conn = _db.OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();

        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT 1 FROM spamreports WHERE msgid = @msgid AND modulus = @modulus LIMIT 1;";
            var pCheckMsgId = checkCmd.Parameters.Add("@msgid", SqliteType.Text);
            var pCheckMod = checkCmd.Parameters.Add("@modulus", SqliteType.Text);

            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = "INSERT OR IGNORE INTO spamreports (rowid, msgid, modulus, date, reportmsgid, sender) VALUES (@rowid, @msgid, @modulus, @date, @reportmsgid, @sender);";
            var pRowId = insertCmd.Parameters.Add("@rowid", SqliteType.Integer);
            var pMsgId = insertCmd.Parameters.Add("@msgid", SqliteType.Text);
            var pMod = insertCmd.Parameters.Add("@modulus", SqliteType.Text);
            var pDate = insertCmd.Parameters.Add("@date", SqliteType.Integer);
            var pReportMsgId = insertCmd.Parameters.Add("@reportmsgid", SqliteType.Text);
            var pSender = insertCmd.Parameters.Add("@sender", SqliteType.Text);

            using var groupCmd = conn.CreateCommand();
            groupCmd.Transaction = tx;
            groupCmd.CommandText = @"
                INSERT INTO spamgroup (msgid, cnt) VALUES (@msgid, 1)
                ON CONFLICT(msgid) DO UPDATE SET cnt = cnt + 1;";
            var pGroupMsgId = groupCmd.Parameters.Add("@msgid", SqliteType.Text);

            foreach (var report in reports)
            {
                if (string.IsNullOrWhiteSpace(report.MsgId)) continue;

                pCheckMsgId.Value = report.MsgId;
                pCheckMod.Value = report.Modulus ?? "";
                var exists = await checkCmd.ExecuteScalarAsync();
                if (exists != null && Convert.ToInt64(exists) >= 1)
                {
                    continue;
                }

                pRowId.Value = report.RowId;
                pMsgId.Value = report.MsgId;
                pMod.Value = report.Modulus ?? "";
                pDate.Value = report.Date;
                pReportMsgId.Value = report.ReportMsgId ?? "";
                pSender.Value = report.Sender ?? "";
                await insertCmd.ExecuteNonQueryAsync();

                pGroupMsgId.Value = report.MsgId;
                await groupCmd.ExecuteNonQueryAsync();

                count++;
            }

            tx.Commit();
            return count;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Log.Error(ex, "Failed to insert spam reports");
            throw;
        }
    }

    /// <summary>Returns the aggregated spam report count for a spot from the spamgroup table.</summary>
    public async Task<int> GetSpamReportCountAsync(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId)) return 0;

        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cnt FROM spamgroup WHERE msgid = @msgid LIMIT 1;";
        cmd.Parameters.AddWithValue("@msgid", msgId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && int.TryParse(result.ToString(), out int c) ? c : 0;
    }

    /// <summary>Returns all individual spam reports recorded against a spot.</summary>
    public async Task<List<SpamReportItem>> GetSpamReportsAsync(string msgId)
    {
        var list = new List<SpamReportItem>();
        if (string.IsNullOrWhiteSpace(msgId)) return list;

        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rowid, msgid, modulus, date, reportmsgid, sender FROM spamreports WHERE msgid = @msgid ORDER BY date DESC;";
        cmd.Parameters.AddWithValue("@msgid", msgId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SpamReportItem
            {
                RowId = reader.GetInt64(0),
                MsgId = reader.GetString(1),
                Modulus = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Date = reader.GetInt64(3),
                ReportMsgId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Sender = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }
        return list;
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
            Modulus = reader.IsDBNull(12) ? "" : reader.GetString(12),
            NumberOfSpamReports = reader.FieldCount > 13 ? reader.GetInt32(13) : 0
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

    /// <summary>
    /// Computes exact database statistics: minimum rowid, maximum rowid, and total spot count.
    /// Matches Windows SpotSaver.UpdateDatabaseSettings.
    /// </summary>
    public async Task<(long min, long max, long count)> GetDatabaseStatsAsync()
    {
        using var conn = _db.OpenConnection(readOnly: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(MIN(rowid), 0), IFNULL(MAX(rowid), 0), COUNT(1) FROM spots;";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            long min = reader.GetInt64(0);
            long max = reader.GetInt64(1);
            long count = reader.GetInt64(2);
            return (min, max, count);
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// Refreshes database statistics and updates <see cref="UserPreferences"/> (DatabaseMin, DatabaseMax, DatabaseCount).
    /// </summary>
    public async Task<(long min, long max, long count)> UpdateDatabaseStatsAsync(UserPreferencesService? preferences = null)
    {
        var stats = await GetDatabaseStatsAsync();
        if (preferences != null)
        {
            var prefs = preferences.Current;
            prefs.DatabaseMin = stats.min;
            prefs.DatabaseMax = stats.max;
            prefs.DatabaseCount = stats.count;
            preferences.Save(prefs);
        }
        return stats;
    }

    /// <summary>
    /// Removes spots older than <paramref name="retentionDays"/> days in batches of 2000 rows.
    /// Matches Windows SpotSaver.RemoveOutOfRetentionSpots.
    /// Returns the total number of spots removed.
    /// </summary>
    public async Task<int> RemoveOutOfRetentionSpotsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays < 1) return 0;

        long cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeSeconds();
        int totalDeleted = 0;

        using var conn = _db.OpenConnection(readOnly: false);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM spots WHERE rowid IN (SELECT rowid FROM spots WHERE date < @cutoff LIMIT 2000);";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);

            int deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            tx.Commit();

            totalDeleted += deleted;
            if (deleted < 2000)
            {
                break;
            }

            // Yield briefly to keep SQLite and other operations responsive
            try
            {
                await Task.Delay(20, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (totalDeleted > 0)
        {
            Log.Info("Out of retention spots removed: {0}", totalDeleted);
        }

        return totalDeleted;
    }
}
