using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using NLog;
using System.IO;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Model;

internal static class Favorites
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const string FavCatsIdentifier = "f1";

	private static bool _isMigrationCalledAlready;

	internal static void MigrateFromFileToDatabase()
	{
		if (_isMigrationCalledAlready)
		{
			return;
		}
		_isMigrationCalledAlready = true;
		string path = Path.Combine(AppHelper.SettingsFolder, "favorites.csv");
		if (!File.Exists(path))
		{
			return;
		}
		Log.Debug("Start fav migration");
		List<string> list = new List<string>();
		try
		{
			if (File.Exists(path))
			{
				list = File.ReadAllLines(path).ToList();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return;
		}
		using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots())
		{
			List<string> list2 = new List<string>();
			using (ISqlDbTransaction transaction = sqlDb.BeginReadTransaction())
			{
				string sQuery = "SELECT msgid FROM spots WHERE cats LIKE \"% f1%\"";
				using DbDataReader dbDataReader = sqlDb.ExecuteReader(sQuery, transaction);
				if (dbDataReader != null)
				{
					while (dbDataReader.Read())
					{
						string item = RuntimeHelpers.GetObjectValue(dbDataReader[0]) as string;
						list2.Add(item);
					}
				}
			}
			using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
			// Message ids are imported from a user-supplied favourites file, so they are
			// bound rather than quoted into the statement.
			using (DbCommand dbCommand = sqlDb.CreateCommand(sqlDbTransaction))
			{
				dbCommand.CommandText = AddFavoriteSql;
				DbParameter msgIdParameter = dbCommand.CreateParameter();
				dbCommand.Parameters.Add(msgIdParameter);
				foreach (string item2 in list)
				{
					string text = SpotHelper.MakeMsg(item2.Trim(), tag: false);
					if (!list2.Contains(text))
					{
						msgIdParameter.Value = text;
						sqlDb.ExecuteNonQuery(dbCommand);
					}
				}
			}
			sqlDbTransaction.Commit();
		}
		try
		{
			File.WriteAllText(path, "");
			File.Delete(path);
		}
		catch (Exception)
		{
		}
		Log.Debug("Fav migration finished");
	}

	internal static bool IsFavoritesQuery(string query)
	{
		query = query.ToLower();
		if (!query.StartsWith("favorites") && !query.Contains("spots.msgid in favorieten"))
		{
			return query.Contains("cats match 'f1'");
		}
		return true;
	}

	internal static void Add(string messageId)
	{
		if (messageId.IsNullOrWhiteSpace())
		{
			return;
		}
		messageId = SpotHelper.MakeMsg(messageId.Trim(), tag: false);
		if (ContainsMessageId(messageId))
		{
			Log.Debug("Spot is in fav already: " + messageId);
			return;
		}
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
		ExecuteWithMessageId(sqlDb, sqlDbTransaction, AddFavoriteSql, messageId);
		sqlDbTransaction.Commit();
	}

	internal static void Remove(string messageId)
	{
		if (messageId.IsNullOrWhiteSpace())
		{
			return;
		}
		messageId = SpotHelper.MakeMsg(messageId.Trim(), tag: false);
		if (!ContainsMessageId(messageId))
		{
			Log.Debug("Spot is not in fav: " + messageId);
			return;
		}
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
		ExecuteWithMessageId(sqlDb, sqlDbTransaction, RemoveFavoriteSql, messageId);
		sqlDbTransaction.Commit();
	}

	internal static bool ContainsInCats(string cats)
	{
		return cats.EndsWith(" f1");
	}

	internal static bool ContainsMessageId(string messageId)
	{
		if (messageId.IsNullOrWhiteSpace())
		{
			return false;
		}
		messageId = SpotHelper.MakeMsg(messageId.Trim(), tag: false);
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
		using DbCommand dbCommand = sqlDb.CreateCommand(transaction);
		dbCommand.CommandText = "SELECT COUNT(rowid) FROM spots WHERE msgid = ? AND cats LIKE '% f1%'";
		DbParameter msgIdParameter = dbCommand.CreateParameter();
		msgIdParameter.Value = messageId;
		dbCommand.Parameters.Add(msgIdParameter);
		return sqlDb.ExecuteScalar(dbCommand) >= 1;
	}

	private const string AddFavoriteSql = "UPDATE spots SET cats = cats || ' f1' WHERE msgid = ?";

	private const string RemoveFavoriteSql = "UPDATE spots SET cats = replace(cats, ' f1', '') WHERE msgid = ?";

	/// <summary>
	/// Runs a single-parameter statement with the message id bound rather than quoted
	/// into the SQL. Message ids come from spots and from the user's favourites file, so
	/// they can legitimately contain quote characters.
	/// </summary>
	private static void ExecuteWithMessageId(ISqlDb sqlDb, ISqlDbTransaction transaction, string sql, string messageId)
	{
		using DbCommand dbCommand = sqlDb.CreateCommand(transaction);
		dbCommand.CommandText = sql;
		DbParameter msgIdParameter = dbCommand.CreateParameter();
		msgIdParameter.Value = messageId;
		dbCommand.Parameters.Add(msgIdParameter);
		sqlDb.ExecuteNonQuery(dbCommand);
	}

	internal static string ReplaceWithFavoritesQuery(string rowFilter)
	{
		string result = rowFilter;
		string text = "cats MATCH 'f1'";
		if (rowFilter.StartsWith("favorites"))
		{
			result = text + rowFilter.Substring("favorites".Length);
		}
		if (rowFilter.Contains("spots.msgid in favorieten"))
		{
			result = rowFilter.Replace("spots.msgid in favorieten", text);
		}
		return result;
	}
}
