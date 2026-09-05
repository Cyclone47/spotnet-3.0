using System;
using System.Data.SQLite;
using System.IO;
using Spotnet.DAL;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// The rebuild path exists to salvage a user's spots when the database is damaged, so
    /// it is worth proving it actually keeps the rows rather than trusting that it does.
    /// </summary>
    public class SpotsDbRebuilderTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dbFile;

        public SpotsDbRebuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "spotnet_rebuild_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbFile = Path.Combine(_dir, "spotnet.dbs");
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // Temp dir cleanup is best effort.
            }
        }

        private SQLiteConnection Open(string path)
        {
            var conn = new SQLiteConnection($"Data Source={path};Version=3;Journal Mode=WAL;BusyTimeout=5000;");
            conn.Open();
            // The fixture builds the FTS5 `search` index by hand, so it needs the module
            // registered exactly as SQliteDb does for the app's own connections.
            Fts5Module.Register(conn);
            return conn;
        }

        /// <summary>Builds a well-formed spots database holding <paramref name="rows"/> spots.</summary>
        private void SeedDatabase(int rows)
        {
            using var conn = Open(_dbFile);
            using var cmd = conn.CreateCommand();

            foreach (string statement in SpotsSchema.Tables)
            {
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }
            foreach (string statement in SpotsSchema.SearchTriggers)
            {
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }
            cmd.CommandText = "PRAGMA user_version = " + SpotsSchema.CurrentUserVersion + ";";
            cmd.ExecuteNonQuery();

            using (var tx = conn.BeginTransaction())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO spots(rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus) " +
                    "VALUES(@id, 1, 3, 0, 0, 1700000000, 1024, '3 a01', @sender, 'tag', @subject, @msgid, 'AAAA')";
                for (int i = 1; i <= rows; i++)
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@id", i);
                    cmd.Parameters.AddWithValue("@sender", "poster" + (i % 7));
                    cmd.Parameters.AddWithValue("@subject", "Ubuntu release " + i);
                    cmd.Parameters.AddWithValue("@msgid", "msg" + i + "@spot.net");
                    cmd.ExecuteNonQuery();
                }
                cmd.Parameters.Clear();

                cmd.CommandText = "INSERT INTO userkey(key) VALUES('the-users-signing-key')";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO spamgroup(msgid, cnt) VALUES('msg3@spot.net', 4)";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            cmd.Transaction = null;

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        private long ScalarLong(string path, string sql)
        {
            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Fact]
        public void Rebuild_PreservesEverySpot()
        {
            SeedDatabase(rows: 500);
            SQLiteConnection.ClearAllPools();

            SpotsDbRebuilder.RebuildResult result = SpotsDbRebuilder.Rebuild(_dbFile);

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(500, result.SpotsRecovered);
            Assert.Equal(0, result.UnreadableChunks);
            Assert.Equal(500L, ScalarLong(_dbFile, "SELECT COUNT(1) FROM spots"));
        }

        [Fact]
        public void Rebuild_KeepsTheUserSigningKeyAndSpamCounts()
        {
            SeedDatabase(rows: 10);
            SQLiteConnection.ClearAllPools();

            Assert.True(SpotsDbRebuilder.Rebuild(_dbFile).Succeeded);

            using var conn = Open(_dbFile);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key FROM userkey LIMIT 1";
            Assert.Equal("the-users-signing-key", Convert.ToString(cmd.ExecuteScalar()));

            cmd.CommandText = "SELECT cnt FROM spamgroup WHERE msgid = 'msg3@spot.net'";
            Assert.Equal(4L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        [Fact]
        public void Rebuild_RegeneratesAWorkingSearchIndex()
        {
            SeedDatabase(rows: 50);
            SQLiteConnection.ClearAllPools();

            // Wipe the FTS index the way a torn index would leave it, then rebuild.
            using (var conn = Open(_dbFile))
            {
                using var wipe = conn.CreateCommand();
                wipe.CommandText = "DELETE FROM search";
                wipe.ExecuteNonQuery();
            }
            SQLiteConnection.ClearAllPools();

            Assert.True(SpotsDbRebuilder.Rebuild(_dbFile).Succeeded);

            using var reopened = Open(_dbFile);
            using var cmd = reopened.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM search WHERE search MATCH 'Ubuntu'";
            Assert.Equal(50L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        [Fact]
        public void Rebuild_LeavesTheDamagedOriginalAsABackup()
        {
            SeedDatabase(rows: 20);
            SQLiteConnection.ClearAllPools();

            SpotsDbRebuilder.RebuildResult result = SpotsDbRebuilder.Rebuild(_dbFile);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath), "the original database should be kept as a .bak");
            Assert.EndsWith(".bak", result.BackupPath);
            // No stray working file left behind.
            Assert.False(File.Exists(_dbFile + ".rebuild"));
        }

        [Fact]
        public void Rebuild_ReportsFailureOnAFileThatIsNotADatabase()
        {
            File.WriteAllText(_dbFile, "this is not a SQLite database, it is just text");

            SpotsDbRebuilder.RebuildResult result = SpotsDbRebuilder.Rebuild(_dbFile);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Error);
            // The unreadable original must be left exactly where it was, not consumed.
            Assert.True(File.Exists(_dbFile));
            Assert.False(File.Exists(_dbFile + ".rebuild"));
        }

        [Fact]
        public void Rebuild_ReportsFailureWhenTheFileIsMissing()
        {
            SpotsDbRebuilder.RebuildResult result = SpotsDbRebuilder.Rebuild(Path.Combine(_dir, "does-not-exist.dbs"));

            Assert.False(result.Succeeded);
            Assert.Equal(0, result.SpotsRecovered);
        }
    }
}
