using System;
using System.IO;
using System.Data.SQLite;
using Spotnet.DAL;
using Xunit;

namespace Spotnet.Tests
{
    public class SpotDbTests
    {
        [Fact]
        public void SQLite_InMemoryDatabaseOperations()
        {
            using var conn = new SQLiteConnection("Data Source=:memory:;Version=3;");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE spots (
                    spotid INTEGER PRIMARY KEY,
                    messageid TEXT UNIQUE,
                    title TEXT,
                    tag TEXT,
                    cat INTEGER,
                    subcat TEXT,
                    size INTEGER,
                    created INTEGER,
                    poster TEXT,
                    spamcount INTEGER DEFAULT 0
                );
                CREATE INDEX idx_spots_cat ON spots(cat);
            ";
            cmd.ExecuteNonQuery();

            // Insert test record
            cmd.CommandText = @"
                INSERT INTO spots (spotid, messageid, title, tag, cat, subcat, size, created, poster)
                VALUES (1, 'msg123@spotnet', 'Ubuntu 24.04 LTS', 'Linux', 3, '03a01', 2500000000, 1700000000, 'Canonical');
            ";
            int rows = cmd.ExecuteNonQuery();
            Assert.Equal(1, rows);

            // Query test record
            cmd.CommandText = "SELECT title, poster, size FROM spots WHERE spotid = 1;";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Ubuntu 24.04 LTS", reader.GetString(0));
            Assert.Equal("Canonical", reader.GetString(1));
            Assert.Equal(2500000000L, reader.GetInt64(2));
        }
    }
}
