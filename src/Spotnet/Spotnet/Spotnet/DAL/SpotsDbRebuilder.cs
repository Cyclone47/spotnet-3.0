using System;
using System.Data.SQLite;
using System.IO;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.DAL;

/// <summary>
/// Salvages a damaged spots database by copying every readable row into a fresh file.
/// </summary>
/// <remarks>
/// This sits between "checkpoint the journal" and "throw everything away". Most databases
/// reported as corrupt have only lost their FTS index, and because `search` is a
/// contentless index over `spots` it can always be regenerated from the rows themselves —
/// so that case recovers with nothing lost at all.
///
/// Rows are copied in rowid ranges so a single unreadable page costs that chunk rather
/// than the whole table.
/// </remarks>
internal static class SpotsDbRebuilder
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const int ChunkSize = 20000;

	internal sealed class RebuildResult
	{
		public bool Succeeded { get; set; }

		public int SpotsRecovered { get; set; }

		public int UnreadableChunks { get; set; }

		/// <summary>Where the damaged original was moved to, once the swap succeeded.</summary>
		public string BackupPath { get; set; }

		public string Error { get; set; }
	}

	/// <summary>
	/// Rebuilds <paramref name="sourceDbFile"/> in place, leaving the damaged original
	/// beside it as a timestamped .bak.
	/// </summary>
	internal static RebuildResult Rebuild(string sourceDbFile)
	{
		var result = new RebuildResult();
		if (string.IsNullOrWhiteSpace(sourceDbFile) || !File.Exists(sourceDbFile))
		{
			result.Error = "Database file not found: " + sourceDbFile;
			Log.Warn(result.Error);
			return result;
		}

		string rebuilt = sourceDbFile + ".rebuild";
		try
		{
			DeleteWithSidecars(rebuilt);

			using (var conn = new SQLiteConnection($"Data Source={rebuilt};Version=3;Journal Mode=WAL;BusyTimeout=5000;"))
			{
				conn.Open();
				// This connection creates and rebuilds the FTS5 `search` index itself,
				// so it needs the module registered like any other.
				Fts5Module.Register(conn);
				using var cmd = conn.CreateCommand();

				// page_size has to precede any write and cannot change after WAL is on.
				Execute(cmd, "PRAGMA page_size = 8192;");
				foreach (string statement in SpotsSchema.Tables)
				{
					Execute(cmd, statement);
				}
				Execute(cmd, "PRAGMA user_version = " + SpotsSchema.CurrentUserVersion + ";");

				cmd.CommandText = "ATTACH DATABASE @src AS damaged;";
				cmd.Parameters.AddWithValue("@src", sourceDbFile);
				cmd.ExecuteNonQuery();
				cmd.Parameters.Clear();

				if (!TryGetRowIdRange(cmd, out long minRowId, out long maxRowId))
				{
					result.Error = "The spots table could not be read at all.";
					return result;
				}

				CopySpots(cmd, minRowId, maxRowId, result);

				// Small tables, copied whole; a failure on one must not lose the others.
				TryExecute(cmd, "INSERT OR IGNORE INTO main.spamreports(" + SpotsSchema.SpamReportColumns + ") SELECT " + SpotsSchema.SpamReportColumns + " FROM damaged.spamreports;");
				TryExecute(cmd, "INSERT OR IGNORE INTO main.spamgroup(msgid, cnt) SELECT msgid, cnt FROM damaged.spamgroup;");
				TryExecute(cmd, "INSERT OR IGNORE INTO main.userinfo(field, value) SELECT field, value FROM damaged.userinfo;");
				// The signing key is the one thing here that cannot be re-downloaded.
				TryExecute(cmd, "INSERT OR IGNORE INTO main.userkey(key) SELECT key FROM damaged.userkey;");

				Execute(cmd, "DETACH DATABASE damaged;");

				// Indexes and triggers go on after the bulk copy: faster, and the triggers
				// would otherwise write the FTS index twice.
				foreach (string statement in SpotsSchema.Indexes)
				{
					Execute(cmd, statement);
				}
				Execute(cmd, SpotsSchema.RebuildSearchIndex);
				foreach (string statement in SpotsSchema.SearchTriggers)
				{
					Execute(cmd, statement);
				}

				Execute(cmd, "PRAGMA wal_checkpoint(TRUNCATE);");
			}

			SQLiteConnection.ClearAllPools();

			// Swap only after the rebuild has completed successfully.
			string backup = $"{sourceDbFile}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
			File.Move(sourceDbFile, backup);
			File.Move(rebuilt, sourceDbFile);
			DeleteSidecars(backup);
			DeleteSidecars(rebuilt);

			result.BackupPath = backup;
			result.Succeeded = true;
			Log.Info("Rebuild recovered {0} spots ({1} unreadable chunks); original kept at {2}", result.SpotsRecovered, result.UnreadableChunks, backup);
			return result;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			result.Error = ex.Message;
			try
			{
				SQLiteConnection.ClearAllPools();
				DeleteWithSidecars(rebuilt);
			}
			catch (IOException)
			{
				// A stray .rebuild file is not worth masking the real error.
			}
			return result;
		}
	}

	private static bool TryGetRowIdRange(SQLiteCommand cmd, out long minRowId, out long maxRowId)
	{
		minRowId = 0;
		maxRowId = 0;
		try
		{
			cmd.CommandText = "SELECT IFNULL(MIN(rowid), 0), IFNULL(MAX(rowid), 0) FROM damaged.spots;";
			using SQLiteDataReader reader = cmd.ExecuteReader();
			if (reader.Read())
			{
				minRowId = reader.GetInt64(0);
				maxRowId = reader.GetInt64(1);
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Error("Cannot read the spots table: {0}", ex.Message);
			return false;
		}
	}

	private static void CopySpots(SQLiteCommand cmd, long minRowId, long maxRowId, RebuildResult result)
	{
		for (long start = minRowId; start <= maxRowId; start += ChunkSize)
		{
			long end = start + ChunkSize;
			try
			{
				cmd.CommandText =
					"INSERT OR IGNORE INTO main.spots(" + SpotsSchema.SpotColumns + ") " +
					"SELECT " + SpotsSchema.SpotColumns + " FROM damaged.spots " +
					"WHERE rowid >= @start AND rowid < @end;";
				cmd.Parameters.AddWithValue("@start", start);
				cmd.Parameters.AddWithValue("@end", end);
				result.SpotsRecovered += cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				result.UnreadableChunks++;
				Log.Warn("Skipped spots rowid {0}-{1}: {2}", start, end, ex.Message);
			}
			finally
			{
				cmd.Parameters.Clear();
			}
		}
	}

	private static void Execute(SQLiteCommand cmd, string sql)
	{
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	private static void TryExecute(SQLiteCommand cmd, string sql)
	{
		try
		{
			Execute(cmd, sql);
		}
		catch (Exception ex)
		{
			Log.Warn("Could not salvage rows: {0} ({1})", sql, ex.Message);
		}
	}

	private static void DeleteWithSidecars(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		DeleteSidecars(path);
	}

	private static void DeleteSidecars(string path)
	{
		foreach (string suffix in new[] { "-wal", "-shm" })
		{
			if (File.Exists(path + suffix))
			{
				File.Delete(path + suffix);
			}
		}
	}
}
