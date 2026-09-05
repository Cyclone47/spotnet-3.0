using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NLog;

namespace Spotnet.Mac.DAL;

/// <summary>
/// Manages SQLite connection lifecycle on macOS using Microsoft.Data.Sqlite and SQLitePCLRaw bundle_e_sqlite3.
/// </summary>
public sealed class MacSqliteDb : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static bool _sqliteInitialized;
    private static readonly object InitLock = new();

    private readonly string _databasePath;
    private SqliteConnection? _connection;

    public string DatabasePath => _databasePath;
    public bool IsOpen => _connection != null && _connection.State == System.Data.ConnectionState.Open;

    public MacSqliteDb(string databasePath)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        if (_sqliteInitialized) return;
        lock (InitLock)
        {
            if (_sqliteInitialized) return;
            // bundle_e_sqlite3 registers the dynamic C SQLite provider with FTS5 compiled in
            SQLitePCL.Batteries_V2.Init();
            _sqliteInitialized = true;
            Log.Info("SQLitePCLRaw bundle_e_sqlite3 initialized with native FTS5 support.");
        }
    }

    public SqliteConnection OpenConnection(bool readOnly = false)
    {
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout = 5000;";
            cmd.ExecuteNonQuery();

            if (!readOnly)
            {
                cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                cmd.ExecuteNonQuery();
            }
        }

        return connection;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review CA2100 query string", Justification = "Internal constant DDL schema statements")]
    public void InitializeSchema()
    {
        using var conn = OpenConnection(readOnly: false);
        using var tx = conn.BeginTransaction();

        // 1. Create tables
        foreach (var sql in MacSpotsSchema.Tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // 2. Create indexes
        foreach (var sql in MacSpotsSchema.Indexes)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // 3. Create triggers
        foreach (var sql in MacSpotsSchema.SearchTriggers)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // 4. Set user_version
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA user_version = {MacSpotsSchema.CurrentUserVersion};";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        Log.Info("Spots schema initialized successfully at {0}", _databasePath);
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            _connection.Dispose();
            _connection = null;
        }
    }
}
