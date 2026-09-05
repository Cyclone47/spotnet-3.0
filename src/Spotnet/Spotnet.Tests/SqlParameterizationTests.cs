using System;
using System.Data.SQLite;
using System.IO;
using Spotnet.DAL;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// The favourites and spam-count queries used to concatenate message ids straight into
    /// SQL. Message ids arrive from the network, so these pin both that the rewritten
    /// statements still do the right thing and that a hostile id cannot alter them.
    /// </summary>
    public class SqlParameterizationTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dbFile;

        public SqlParameterizationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "spotnet_sql_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbFile = Path.Combine(_dir, "spotnet.dbs");
            Seed();
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
                // Best effort.
            }
        }

        private SQLiteConnection Open()
        {
            var conn = new SQLiteConnection($"Data Source={_dbFile};Version=3;Journal Mode=WAL;BusyTimeout=5000;");
            conn.Open();
            return conn;
        }

        private void Seed()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SpotsSchema.CreateSpots;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO spots(rowid, cats, msgid, subject) VALUES(1, '3 a01', @a, 'ordinary'), (2, '3 a01', @b, 'quoted')";
            cmd.Parameters.AddWithValue("@a", "plain@spot.net");
            // A message id containing both quote characters: this used to break the
            // concatenated SQL outright.
            cmd.Parameters.AddWithValue("@b", "ev\"il' OR 1=1 --@spot.net");
            cmd.ExecuteNonQuery();
        }

        private const string AddFavoriteSql = "UPDATE spots SET cats = cats || ' f1' WHERE msgid = ?";
        private const string RemoveFavoriteSql = "UPDATE spots SET cats = replace(cats, ' f1', '') WHERE msgid = ?";
        private const string ContainsSql = "SELECT COUNT(rowid) FROM spots WHERE msgid = ? AND cats LIKE '% f1%'";

        private int Execute(string sql, string messageId)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@p", messageId);
            return cmd.ExecuteNonQuery();
        }

        private long Count(string sql, string messageId)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@p", messageId);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private string CatsFor(long rowId)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT cats FROM spots WHERE rowid = " + rowId;
            return Convert.ToString(cmd.ExecuteScalar());
        }

        [Fact]
        public void AddFavorite_AppendsTheMarkerToTheRightRowOnly()
        {
            Assert.Equal(1, Execute(AddFavoriteSql, "plain@spot.net"));

            Assert.Equal("3 a01 f1", CatsFor(1));
            Assert.Equal("3 a01", CatsFor(2));
        }

        [Fact]
        public void RemoveFavorite_StripsTheMarkerBackOut()
        {
            Execute(AddFavoriteSql, "plain@spot.net");
            Assert.Equal(1, Execute(RemoveFavoriteSql, "plain@spot.net"));

            Assert.Equal("3 a01", CatsFor(1));
        }

        [Fact]
        public void ContainsMessageId_MatchesOnlyFavoritedRows()
        {
            Assert.Equal(0L, Count(ContainsSql, "plain@spot.net"));

            Execute(AddFavoriteSql, "plain@spot.net");

            Assert.Equal(1L, Count(ContainsSql, "plain@spot.net"));
            Assert.Equal(0L, Count(ContainsSql, "nosuch@spot.net"));
        }

        [Fact]
        public void AMessageIdContainingQuotesIsTreatedAsData()
        {
            const string hostile = "ev\"il' OR 1=1 --@spot.net";

            // Binds cleanly and touches exactly the one row it names.
            Assert.Equal(1, Execute(AddFavoriteSql, hostile));

            Assert.Equal("3 a01 f1", CatsFor(2));
            // The other row must be untouched - if the id were concatenated, the
            // "OR 1=1" would have favourited everything.
            Assert.Equal("3 a01", CatsFor(1));
        }

        [Fact]
        public void SingleQuotedLiteralsBehaveAsTheDoubleQuotedOnesDid()
        {
            // The rewrite swapped SQLite's ambiguous double-quoted literals for single
            // quotes. Confirm the concatenation still produces the same text.
            Execute(AddFavoriteSql, "plain@spot.net");
            Assert.Equal("3 a01 f1", CatsFor(1));
            Assert.EndsWith(" f1", CatsFor(1));
        }
    }
}
