using System;
using System.IO;
using System.Data.SQLite;
using Spotnet.DAL;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Guards the journalling configuration that keeps the spots database from being
    /// corrupted by an interrupted bulk import. These assert the pragmas the DAL applies,
    /// against a real on-disk file, because the failure mode they protect against only
    /// exists on disk.
    /// </summary>
    public class DbDurabilityTests : IDisposable
    {
        private readonly string _dbFile;

        public DbDurabilityTests()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), "spotnet_durability_" + Guid.NewGuid().ToString("N") + ".dbs");
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    if (File.Exists(_dbFile + suffix))
                    {
                        File.Delete(_dbFile + suffix);
                    }
                }
                catch (IOException)
                {
                    // A lingering handle on a temp file is not worth failing a test over.
                }
            }
        }

        private SQLiteConnection OpenWritable()
        {
            // Mirrors the connection string built by SQliteDb for a writable connection.
            var conn = new SQLiteConnection(
                "DataSource=" + _dbFile + ";Synchronous=Normal;Temp Store=Memory;Cache Size=72500;BusyTimeout=5000;Journal Mode=WAL;");
            conn.Open();
            return conn;
        }

        private static string Scalar(SQLiteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar());
        }

        [Fact]
        public void WritableConnection_UsesWalJournal()
        {
            using var conn = OpenWritable();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE spots(rowid INTEGER PRIMARY KEY, subject TEXT)";
                cmd.ExecuteNonQuery();
            }

            Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode").ToLowerInvariant());
        }

        [Fact]
        public void ImportPragmas_LeaveDatabaseCrashSafe()
        {
            using var conn = OpenWritable();
            using (var cmd = conn.CreateCommand())
            {
                // The exact sequence SpotSaver.SetDbSettingsForInsertionImprove applies.
                cmd.CommandText = "PRAGMA journal_mode = WAL";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA synchronous = NORMAL";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA wal_autocheckpoint = 4000";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA cache_size = -65536";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA temp_store = MEMORY";
                cmd.ExecuteNonQuery();
            }

            Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode").ToLowerInvariant());
            // synchronous must not be 0 (OFF) - that is the setting that allowed the
            // database to be left torn when an import was interrupted.
            Assert.NotEqual("0", Scalar(conn, "PRAGMA synchronous"));
            Assert.Equal("4000", Scalar(conn, "PRAGMA wal_autocheckpoint"));
        }

        [Fact]
        public void ImportPragmas_AreAcceptedThroughTheDalStartupPath()
        {
            using var db = new SQliteDb(_dbFile);

            // This is the exact method called while Spotnet initializes the comments
            // database. Current System.Data.SQLite returns -1 (not 0) for successful
            // PRAGMA assignments, so the DAL must verify values rather than row counts.
            SpotSaver.SetDbSettingsForInsertionImprove(db);

            Assert.Equal(1L, db.ExecuteScalar("PRAGMA synchronous", null));
            Assert.Equal(4000L, db.ExecuteScalar("PRAGMA wal_autocheckpoint", null));
            Assert.Equal(-65536L, db.ExecuteScalar("PRAGMA cache_size", null));
            Assert.Equal(2L, db.ExecuteScalar("PRAGMA temp_store", null));
        }

        [Fact]
        public void PageSize_IsSettableBeforeWalAndStickyAfter()
        {
            // Documents the ordering constraint in CreateSpotsTablesOnEmptyDatabase:
            // page_size has to be applied before the database switches to WAL.
            using (var conn = new SQLiteConnection("DataSource=" + _dbFile + ";"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA page_size = 8192";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA journal_mode = WAL";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE TABLE spots(rowid INTEGER PRIMARY KEY, subject TEXT)";
                cmd.ExecuteNonQuery();
            }

            SQLiteConnection.ClearAllPools();

            using var reopened = OpenWritable();
            Assert.Equal("8192", Scalar(reopened, "PRAGMA page_size"));
            Assert.Equal("wal", Scalar(reopened, "PRAGMA journal_mode").ToLowerInvariant());
        }

        [Fact]
        public void FreshInstallerProfile_CanCreateItsFirstSpotsDatabase()
        {
            using var db = new SQliteDb(_dbFile);
            // Opening SQliteDb already creates a non-empty SQLite header. This is the
            // real first-run order that regressed in 3.0.4.
            Assert.True(File.Exists(_dbFile));
            Assert.True(new FileInfo(_dbFile).Length > 0);
            var method = typeof(SpotProvider).GetMethod("EnsureSpotsSchema",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            // The schema method does not need UI/settings state from the constructor.
            var provider = (SpotProvider)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SpotProvider));
            Assert.True((bool)method.Invoke(provider, new object[] { db }));
            Assert.Equal(SpotsSchema.CurrentUserVersion, db.ExecuteScalar("PRAGMA user_version", null));
            Assert.Equal(SpotsSchema.SpotsPageSize, db.ExecuteScalar("PRAGMA page_size", null));
            Assert.Equal("wal", db.ExecuteCommand("PRAGMA journal_mode", null).Trim().ToLowerInvariant());
            Assert.Equal(1L, db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='spots'", null));
        }

        [Fact]
        public void IncompleteFirstRunDatabase_IsRepairedWithoutDeletingUpgradeTables()
        {
            using var db = new SQliteDb(_dbFile);
            db.ExecuteNonQuery("CREATE TABLE spamreports(rowid INTEGER PRIMARY KEY, msgid TEXT, modulus TEXT, date INT, reportmsgid TEXT, sender TEXT)", null);
            db.ExecuteNonQuery("CREATE TABLE spamgroup(msgid TEXT PRIMARY KEY NOT NULL, cnt INT DEFAULT 0)", null);
            db.ExecuteNonQuery("INSERT INTO spamgroup(msgid, cnt) VALUES('preserve-me', 3)", null);
            db.ExecuteNonQuery("PRAGMA user_version = 2", null);

            var method = typeof(SpotProvider).GetMethod("EnsureSpotsSchema",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var provider = (SpotProvider)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SpotProvider));
            Assert.True((bool)method.Invoke(provider, new object[] { db }));

            Assert.Equal(1L, db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='spots'", null));
            Assert.Equal(3L, db.ExecuteScalar("SELECT cnt FROM spamgroup WHERE msgid='preserve-me'", null));
            Assert.Equal(SpotsSchema.CurrentUserVersion, db.ExecuteScalar("PRAGMA user_version", null));
        }

        [Fact]
        public void FreshDatabaseInitializerRefusesExistingUserTables()
        {
            using var db = new SQliteDb(_dbFile);
            db.ExecuteNonQuery("CREATE TABLE personal(value TEXT)", null);
            db.ExecuteNonQuery("INSERT INTO personal VALUES('keep')", null);
            var method = typeof(SpotProvider).GetMethod("EnsureSpotsSchema",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var provider = (SpotProvider)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SpotProvider));
            Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(provider, new object[] { db }));
            Assert.Equal("keep", db.ExecuteCommand("SELECT value FROM personal", null).Trim());
        }

        [Fact]
        public void VersionTwoSpotsDatabase_MigratesItsSearchIndexToFts5()
        {
            using var db = new SQliteDb(_dbFile);
            db.ExecuteNonQuery(SpotsSchema.CreateSpots, null);
            db.ExecuteNonQuery("CREATE VIRTUAL TABLE search USING fts4(content='spots', cats TEXT, sender TEXT, tag TEXT, subject TEXT)", null);
            db.ExecuteNonQuery(SpotsSchema.CreateSpamReports, null);
            db.ExecuteNonQuery(SpotsSchema.CreateSpamGroup, null);
            db.ExecuteNonQuery(SpotsSchema.CreateUserInfo, null);
            db.ExecuteNonQuery(SpotsSchema.CreateUserKey, null);
            db.ExecuteNonQuery("INSERT INTO spots(rowid,key,cat,subcat,extcat,date,filesize,cats,sender,tag,subject,msgid,modulus) VALUES(1,1,3,0,0,1,1,'3 a01','tester','linux','Ubuntu release','one@test','key')", null);
            db.ExecuteNonQuery("INSERT INTO search(docid,cats,sender,tag,subject) VALUES(1,'3 a01','tester','linux','Ubuntu release')", null);
            db.ExecuteNonQuery("PRAGMA user_version = 2", null);

            var method = typeof(SpotProvider).GetMethod("DatabaseUpgrade",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var provider = (SpotProvider)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SpotProvider));
            Assert.Equal(SpotsSchema.CurrentUserVersion, (int)method.Invoke(provider, new object[] { db }));

            Assert.Equal(SpotsSchema.CurrentUserVersion, db.ExecuteScalar("PRAGMA user_version", null));
            Assert.Equal(1L, db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE name='search' AND lower(sql) LIKE '%using fts5%'", null));
            Assert.Equal(1L, db.ExecuteScalar("SELECT COUNT(*) FROM search WHERE search MATCH 'Ubuntu'", null));
        }

        [Fact]
        public void ExistingCommentsDatabase_MigratesRowsToFts5()
        {
            using var db = new SQliteDb(_dbFile);
            db.ExecuteNonQuery("CREATE VIRTUAL TABLE comments USING fts4(spot TEXT)", null);
            db.ExecuteNonQuery("INSERT INTO comments(docid, spot) VALUES(42, 'comment-message-id')", null);

            SpotSaver.EnsureCommentsFts5(db);

            Assert.Equal(1L, db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE name='comments' AND lower(sql) LIKE '%using fts5%'", null));
            Assert.Equal(42L, db.ExecuteScalar("SELECT rowid FROM comments WHERE comments MATCH 'comment'", null));
        }
    }
}
