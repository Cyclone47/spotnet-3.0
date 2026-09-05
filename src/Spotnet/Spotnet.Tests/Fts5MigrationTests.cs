using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Spotnet.DAL;
using Spotnet.Extensions;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Migrates a database with the exact shape Spotnet 3.0.5 leaves on disk - an FTS4
    /// `search` index addressed by docid, filled through the FTS4 triggers - and proves
    /// the spots survive and the searches still answer the same.
    /// </summary>
    /// <remarks>
    /// The unit-level migration tests use a handful of rows. This one seeds a few
    /// thousand so the rebuild does real work, and it follows the order Connect() uses:
    /// upgrade first, then re-create the indexes and triggers.
    /// </remarks>
    public class Fts5MigrationTests : IDisposable
    {
        private const int Rows = 5000;

        /// <summary>The `search` index as 3.0.5 created it.</summary>
        private const string CreateSearchFts4 =
            "CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts4(content=\"spots\",cats TEXT, sender TEXT, tag TEXT, subject TEXT,order=desc,matchinfo=fts3)";

        /// <summary>The triggers as 3.0.5 created them, writing docid rather than rowid.</summary>
        private static readonly string[] SearchTriggersFts4 =
        {
            "CREATE TRIGGER IF NOT EXISTS search_bu BEFORE UPDATE ON spots BEGIN DELETE FROM search WHERE docid = old.rowid; END;",
            "CREATE TRIGGER IF NOT EXISTS search_bd BEFORE DELETE ON spots BEGIN DELETE FROM search WHERE docid = old.rowid; END;",
            "CREATE TRIGGER IF NOT EXISTS search_au AFTER UPDATE ON spots BEGIN INSERT INTO search(docid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;",
            "CREATE TRIGGER IF NOT EXISTS search_ai AFTER INSERT ON spots BEGIN INSERT INTO search(docid, cats, sender, tag, subject) VALUES(new.rowid, new.cats, new.sender, new.tag, new.subject); END;"
        };

        private readonly string _dbFile;

        public Fts5MigrationTests()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), "spotnet_fts5_" + Guid.NewGuid().ToString("N") + ".dbs");
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

        private static int Upgrade(ISqlDb db)
        {
            MethodInfo method = typeof(SpotProvider).GetMethod("DatabaseUpgrade",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var provider = (SpotProvider)FormatterServices.GetUninitializedObject(typeof(SpotProvider));
            return (int)method.Invoke(provider, new object[] { db });
        }

        /// <summary>Repeats the index and trigger step Connect() runs after the upgrade.</summary>
        private static void ApplyIndexesAndTriggers(ISqlDb db)
        {
            using ISqlDbTransaction transaction = db.BeginWriteTransaction(exclusive: true);
            foreach (string statement in SpotsSchema.Indexes)
            {
                db.ExecuteNonQuery(statement, transaction);
            }
            foreach (string statement in SpotsSchema.SearchTriggers)
            {
                db.ExecuteNonQuery(statement, transaction);
            }
            transaction.Commit();
        }

        /// <summary>Builds the 3.0.5 database, letting the FTS4 triggers fill the index.</summary>
        private void SeedVersionTwoDatabase()
        {
            using var db = new SQliteDb(_dbFile);
            db.ExecuteNonQuery(SpotsSchema.CreateSpots, null);
            db.ExecuteNonQuery(CreateSearchFts4, null);
            db.ExecuteNonQuery(SpotsSchema.CreateSpamReports, null);
            db.ExecuteNonQuery(SpotsSchema.CreateSpamGroup, null);
            db.ExecuteNonQuery(SpotsSchema.CreateUserInfo, null);
            db.ExecuteNonQuery(SpotsSchema.CreateUserKey, null);
            foreach (string statement in SearchTriggersFts4)
            {
                db.ExecuteNonQuery(statement, null);
            }

            using (ISqlDbTransaction transaction = db.BeginWriteTransaction())
            {
                using DbCommand command = db.CreateCommand(transaction);
                command.CommandText =
                    "INSERT INTO spots(rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus) " +
                    "VALUES(@id, 1, @cat, 0, 0, 1700000000, 1024, @cats, @sender, @tag, @subject, @msgid, 'AAAA')";
                foreach (string name in new[] { "@id", "@cat", "@cats", "@sender", "@tag", "@subject", "@msgid" })
                {
                    DbParameter parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    command.Parameters.Add(parameter);
                }
                for (int i = 1; i <= Rows; i++)
                {
                    command.Parameters["@id"].Value = i;
                    command.Parameters["@cat"].Value = (i % 3) + 1;
                    command.Parameters["@cats"].Value = ((i % 3) + 1) + " a0" + (i % 9);
                    command.Parameters["@sender"].Value = "sender" + (i % 50);
                    command.Parameters["@tag"].Value = (i % 7 == 0) ? "linux" : "misc";
                    command.Parameters["@subject"].Value = (i % 11 == 0) ? "Ubuntu release " + i : "Some other title " + i;
                    command.Parameters["@msgid"].Value = "msg" + i + "@test";
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            db.ExecuteNonQuery("INSERT INTO userkey(key) VALUES('the-signing-key')", null);
            db.ExecuteNonQuery("PRAGMA user_version = 2", null);
        }

        /// <summary>The searches the app actually issues, as row counts.</summary>
        private static Dictionary<string, long> SearchResults(ISqlDb db)
        {
            return new Dictionary<string, long>
            {
                ["subject"] = db.ExecuteScalar("SELECT COUNT(1) FROM search WHERE subject MATCH 'ubuntu'", null),
                ["tag"] = db.ExecuteScalar("SELECT COUNT(1) FROM search WHERE tag MATCH 'linux'", null),
                ["sender"] = db.ExecuteScalar("SELECT COUNT(1) FROM search WHERE sender MATCH 'sender7'", null),
                ["cats"] = db.ExecuteScalar("SELECT COUNT(1) FROM search WHERE cats MATCH 'a01'", null)
            };
        }

        [Fact]
        public void AVersionTwoDatabaseKeepsItsSpotsAndItsSearchResults()
        {
            SeedVersionTwoDatabase();

            Dictionary<string, long> before;
            using (var db = new SQliteDb(_dbFile))
            {
                before = SearchResults(db);
                // A seed that matched nothing would make the comparison meaningless.
                Assert.All(before, pair => Assert.True(pair.Value > 0, pair.Key + " matched nothing before the migration"));
            }

            using (var db = new SQliteDb(_dbFile))
            {
                Assert.Equal(SpotsSchema.CurrentUserVersion, Upgrade(db));
                ApplyIndexesAndTriggers(db);
            }

            using (var db = new SQliteDb(_dbFile))
            {
                Assert.Equal(SpotsSchema.CurrentUserVersion, db.ExecuteScalar("PRAGMA user_version", null));
                Assert.Equal(1L, db.ExecuteScalar(
                    "SELECT COUNT(*) FROM sqlite_master WHERE name='search' AND lower(sql) LIKE '%using fts5%'", null));
                Assert.Equal(Rows, db.ExecuteScalar("SELECT COUNT(1) FROM spots", null));
                Assert.Equal("the-signing-key", db.ExecuteCommand("SELECT key FROM userkey", null).Trim());
                Assert.Equal(before, SearchResults(db));
                // FTS5's own consistency check over the rebuilt index.
                db.ExecuteNonQuery("INSERT INTO search(search) VALUES('integrity-check')", null);
            }

            // Several read paths ask for a read-only connection. Registering a loadable
            // extension is a connection-level call, not a write, but the searches run
            // here so it is worth proving rather than assuming.
            using (var readOnly = new SQliteDb(_dbFile, bReadOnly: true))
            {
                Assert.Equal(before, SearchResults(readOnly));
            }
        }

        [Fact]
        public void TheMigratedTriggersKeepTheIndexInStepWithSpots()
        {
            SeedVersionTwoDatabase();
            using (var db = new SQliteDb(_dbFile))
            {
                Upgrade(db);
                ApplyIndexesAndTriggers(db);
            }

            using var spots = new SQliteDb(_dbFile);
            spots.ExecuteNonQuery(
                "INSERT INTO spots(rowid, key, cat, subcat, extcat, date, filesize, cats, sender, tag, subject, msgid, modulus) " +
                "VALUES(999001, 1, 3, 0, 0, 1700000000, 1024, '3 a01', 'newsender', 'linux', 'Debian bookworm', 'new@test', 'AAAA')", null);
            Assert.Equal(1L, spots.ExecuteScalar("SELECT COUNT(1) FROM search WHERE subject MATCH 'bookworm'", null));

            spots.ExecuteNonQuery("UPDATE spots SET subject = 'Debian trixie' WHERE rowid = 999001", null);
            Assert.Equal(0L, spots.ExecuteScalar("SELECT COUNT(1) FROM search WHERE subject MATCH 'bookworm'", null));
            Assert.Equal(1L, spots.ExecuteScalar("SELECT COUNT(1) FROM search WHERE subject MATCH 'trixie'", null));

            spots.ExecuteNonQuery("DELETE FROM spots WHERE rowid = 999001", null);
            Assert.Equal(0L, spots.ExecuteScalar("SELECT COUNT(1) FROM search WHERE subject MATCH 'trixie'", null));
            spots.ExecuteNonQuery("INSERT INTO search(search) VALUES('integrity-check')", null);
        }

        [Fact]
        public void TheMigrationAnnouncesItselfAndClearsItsFlagAfterwards()
        {
            SeedVersionTwoDatabase();
            string dutch = null;
            string english = null;
            bool flagWasSetWhileAnnouncing = false;
            SpotProvider.OnSchemaUpgradeMessage = delegate (string nl, string en)
            {
                dutch = nl;
                english = en;
                // Startup keys its extended wait off this flag, so it has to be set
                // before anything is announced, not after.
                flagWasSetWhileAnnouncing = SpotProvider.SchemaUpgradeInProgress;
            };
            try
            {
                using var db = new SQliteDb(_dbFile);
                Upgrade(db);
            }
            finally
            {
                SpotProvider.OnSchemaUpgradeMessage = null;
            }

            Assert.False(dutch.IsNullOrWhiteSpace());
            Assert.False(english.IsNullOrWhiteSpace());
            Assert.True(flagWasSetWhileAnnouncing);
            Assert.False(SpotProvider.SchemaUpgradeInProgress);
        }

        [Fact]
        public void AFailedMigrationStillClearsItsFlag()
        {
            SeedVersionTwoDatabase();
            using var db = new SQliteDb(_dbFile);
            // The index is external-content over `spots`. Without that table the rebuild
            // fails immediately, which is a far cheaper failure to stage than contending
            // for the write lock and waiting out the busy timeout.
            db.ExecuteNonQuery("DROP TABLE spots", null);
            db.ExecuteNonQuery("PRAGMA user_version = 2", null);

            Assert.ThrowsAny<Exception>(() => Upgrade(db));

            // Left set, every later startup would wait indefinitely for an upgrade that
            // is no longer running.
            Assert.False(SpotProvider.SchemaUpgradeInProgress);
        }

        [Fact]
        public void AnAlreadyMigratedDatabaseIsLeftAlone()
        {
            SeedVersionTwoDatabase();
            using (var db = new SQliteDb(_dbFile))
            {
                Upgrade(db);
                ApplyIndexesAndTriggers(db);
            }

            using var reopened = new SQliteDb(_dbFile);
            long indexRows = reopened.ExecuteScalar("SELECT COUNT(1) FROM search_data", null);
            Assert.Equal(SpotsSchema.CurrentUserVersion, Upgrade(reopened));
            Assert.Equal(indexRows, reopened.ExecuteScalar("SELECT COUNT(1) FROM search_data", null));
        }
    }
}
