namespace Spotnet.DAL;

/// <summary>
/// The DDL for the spots database, in one place.
/// </summary>
/// <remarks>
/// Both <see cref="SpotProvider"/> (creating a fresh database) and the recovery window
/// (rebuilding a damaged one into a fresh file) need this schema. Keeping the statements
/// here means a rebuilt database cannot silently differ from a created one.
/// </remarks>
internal static class SpotsSchema
{
	/// <summary>Schema version written to PRAGMA user_version by a fresh create.</summary>
	internal const int CurrentUserVersion = 3;

	/// <summary>
	/// Page size for a newly created spots database. Must be applied before the first
	/// write and before WAL is enabled, and cannot be changed afterwards.
	/// </summary>
	internal const int SpotsPageSize = 8192;

	/// <summary>
	/// Page size for a newly created comments database. Larger than the spots store
	/// because it holds whole comment bodies in an FTS table.
	/// </summary>
	internal const int CommentsPageSize = 16384;

	internal const string CreateSpots =
		"CREATE TABLE IF NOT EXISTS spots(rowid INTEGER PRIMARY KEY, key INT, cat INT, subcat INT, extcat INT, date INT, filesize INTEGER, cats TEXT, sender TEXT, tag TEXT, subject TEXT, msgid TEXT, modulus TEXT)";

	/// <summary>
	/// External-content FTS5 index over `spots`; rows are addressed by rowid.
	/// </summary>
	internal const string CreateSearch =
		"CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(cats, sender, tag, subject, content='spots', content_rowid='rowid')";

	internal const string CreateSpamReports =
		"CREATE TABLE IF NOT EXISTS spamreports(rowid INTEGER PRIMARY KEY, msgid TEXT, modulus TEXT, date INT, reportmsgid TEXT, sender TEXT)";

	internal const string CreateSpamGroup =
		"CREATE TABLE IF NOT EXISTS spamgroup(msgid TEXT PRIMARY KEY NOT NULL, cnt INT DEFAULT 0)";

	internal const string CreateUserInfo =
		"CREATE TABLE IF NOT EXISTS userinfo(field TEXT, value TEXT)";

	internal const string CreateUserKey =
		"CREATE TABLE IF NOT EXISTS userkey(key TEXT)";

	/// <summary>Tables a fresh or rebuilt database must contain, in dependency order.</summary>
	internal static readonly string[] Tables =
	{
		CreateSpots,
		CreateSearch,
		CreateSpamReports,
		CreateSpamGroup,
		CreateUserInfo,
		CreateUserKey
	};

	internal static readonly string[] Indexes =
	{
		"CREATE INDEX IF NOT EXISTS dateidx ON spots(date)",
		"CREATE INDEX IF NOT EXISTS catidx ON spots(cat)",
		"CREATE INDEX IF NOT EXISTS msgidx ON spots(msgid)",
		"CREATE INDEX IF NOT EXISTS subjidx ON spots(subject)",
		"CREATE INDEX IF NOT EXISTS msgidx ON spamreports(msgid)",
		"CREATE INDEX IF NOT EXISTS modidx ON spamreports(modulus)"
	};

	/// <summary>Triggers that keep the FTS index in step with `spots`.</summary>
	internal static readonly string[] SearchTriggers =
	{
		"CREATE TRIGGER IF NOT EXISTS search_bd BEFORE DELETE ON spots BEGIN INSERT INTO search(search, rowid, cats, sender, tag, subject) VALUES('delete', old.rowid, old.cats, old.sender, old.tag, old.subject); END;",
		"CREATE TRIGGER IF NOT EXISTS search_bu BEFORE UPDATE ON spots BEGIN INSERT INTO search(search, rowid, cats, sender, tag, subject) VALUES('delete', old.rowid, old.cats, old.sender, old.tag, old.subject); END;",
		"CREATE TRIGGER IF NOT EXISTS search_au AFTER UPDATE ON spots BEGIN INSERT INTO search(rowid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;",
		"CREATE TRIGGER IF NOT EXISTS search_ai AFTER INSERT ON spots BEGIN INSERT INTO search(rowid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;"
	};

	/// <summary>
	/// Regenerates the FTS index from the `spots` table it shadows. Because `search` is a
	/// external-content table it holds no source data of its own, so this recovers a damaged index in
	/// full with no data loss.
	/// </summary>
	internal const string RebuildSearchIndex = "INSERT INTO search(search) VALUES('rebuild')";

	internal const string CreateComments =
		"CREATE VIRTUAL TABLE IF NOT EXISTS comments USING fts5(spot)";

	/// <summary>Columns of `spots`, for an explicit column list when copying rows.</summary>
	internal const string SpotColumns =
		"rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus";

	internal const string SpamReportColumns =
		"rowid, msgid, modulus, date, reportmsgid, sender";
}
