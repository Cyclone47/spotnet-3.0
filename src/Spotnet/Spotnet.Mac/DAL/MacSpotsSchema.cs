namespace Spotnet.Mac.DAL;

/// <summary>
/// Database schema definitions binary-compatible with Spotnet Windows databases.
/// </summary>
public static class MacSpotsSchema
{
    public const int CurrentUserVersion = 3;
    public const int SpotsPageSize = 8192;
    public const int CommentsPageSize = 16384;

    public const string CreateSpots =
        "CREATE TABLE IF NOT EXISTS spots(rowid INTEGER PRIMARY KEY, key INT, cat INT, subcat INT, extcat INT, date INT, filesize INTEGER, cats TEXT, sender TEXT, tag TEXT, subject TEXT, msgid TEXT, modulus TEXT);";

    public const string CreateSearch =
        "CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(cats, sender, tag, subject, content='spots', content_rowid='rowid');";

    public const string CreateSpamReports =
        "CREATE TABLE IF NOT EXISTS spamreports(rowid INTEGER PRIMARY KEY, msgid TEXT, modulus TEXT, date INT, reportmsgid TEXT, sender TEXT);";

    public const string CreateSpamGroup =
        "CREATE TABLE IF NOT EXISTS spamgroup(msgid TEXT PRIMARY KEY NOT NULL, cnt INT DEFAULT 0);";

    public const string CreateUserInfo =
        "CREATE TABLE IF NOT EXISTS userinfo(field TEXT, value TEXT);";

    public const string CreateUserKey =
        "CREATE TABLE IF NOT EXISTS userkey(key TEXT);";

    public const string CreateComments =
        "CREATE TABLE IF NOT EXISTS comments(rowid INTEGER PRIMARY KEY, msgid TEXT, date INT, sender TEXT, rating INT, spotmsgid TEXT, body TEXT);";

    /// <summary>
    /// Index of the reply group: rowid is the article number in free.usenet, msgid the
    /// comment's own Message-ID. A Spotnet comment's Message-ID embeds the prefix of the
    /// spot it replies to, so a full-text MATCH on that prefix finds a spot's comments.
    /// This is the same trick the Windows client's separate comments database uses.
    /// </summary>
    public const string CreateCommentIndex =
        "CREATE VIRTUAL TABLE IF NOT EXISTS commentindex USING fts5(msgid);";

    public static readonly string[] Tables =
    {
        CreateSpots,
        CreateSearch,
        CreateSpamReports,
        CreateSpamGroup,
        CreateUserInfo,
        CreateUserKey,
        CreateComments,
        CreateCommentIndex
    };

    public static readonly string[] Indexes =
    {
        "CREATE INDEX IF NOT EXISTS dateidx ON spots(date);",
        "CREATE INDEX IF NOT EXISTS catidx ON spots(cat);",
        "CREATE INDEX IF NOT EXISTS msgidx ON spots(msgid);",
        "CREATE INDEX IF NOT EXISTS subjidx ON spots(subject);",
        "CREATE INDEX IF NOT EXISTS spammsgidx ON spamreports(msgid);",
        "CREATE INDEX IF NOT EXISTS spammodidx ON spamreports(modulus);",
        "CREATE INDEX IF NOT EXISTS commentspotidx ON comments(spotmsgid);"
    };

    public static readonly string[] SearchTriggers =
    {
        "CREATE TRIGGER IF NOT EXISTS search_bd BEFORE DELETE ON spots BEGIN INSERT INTO search(search, rowid, cats, sender, tag, subject) VALUES('delete', old.rowid, old.cats, old.sender, old.tag, old.subject); END;",
        "CREATE TRIGGER IF NOT EXISTS search_bu BEFORE UPDATE ON spots BEGIN INSERT INTO search(search, rowid, cats, sender, tag, subject) VALUES('delete', old.rowid, old.cats, old.sender, old.tag, old.subject); END;",
        "CREATE TRIGGER IF NOT EXISTS search_au AFTER UPDATE ON spots BEGIN INSERT INTO search(rowid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;",
        "CREATE TRIGGER IF NOT EXISTS search_ai AFTER INSERT ON spots BEGIN INSERT INTO search(rowid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;"
    };

    public const string RebuildSearchIndex = "INSERT INTO search(search) VALUES('rebuild');";
}
