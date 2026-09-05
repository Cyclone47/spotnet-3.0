using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.DAL;

internal class SQliteDb : ISqlDb, IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object LockConnectionsPool = new object();

	internal static List<SQliteDb> ConnectionsPool = new List<SQliteDb>();

	private DbConnection _connection;

	private readonly int _threadId;

	private readonly string _dbFile;

	public string Filename => _dbFile;

	public bool Connected => _connection != null;

	public SQliteDb(string dbFile, bool bReadOnly = false)
	{
		_dbFile = dbFile;
		try
		{
			_threadId = -1;
			SQLiteFactory sQLiteFactory = new SQLiteFactory();
			if (_dbFile.IsNullOrEmpty())
			{
				throw new Exception("No database specified!");
			}
			_connection = sQLiteFactory.CreateConnection();
			if (_connection == null)
			{
				throw new Exception("Failed to create db connection");
			}
			// BusyTimeout makes a connection wait for a write lock instead of failing
			// immediately, which is what surfaced as spurious errors while an import ran.
			// Journal Mode is only asserted on writable connections: a read-only
			// connection cannot create the -wal file and would fail trying.
			_connection.ConnectionString = string.Format("DataSource={0};Synchronous=Normal;Temp Store=Memory;Cache Size={1};BusyTimeout=5000;{2}", _dbFile, Settings.Default.DatabaseCache, bReadOnly ? "Read Only=True;" : "Journal Mode=WAL;");
			if (!SqlDbTransaction.WaitForRelease(_dbFile))
			{
				throw new Exception("Failed to release db connection, it's blocked by other expensive operation");
			}
			_connection.Open();
			_threadId = Thread.CurrentThread.ManagedThreadId;
			if (_connection.State != ConnectionState.Open)
			{
				throw new Exception(Words.CannotConnectToDatabase);
			}
			// `search` and `comments` are FTS5 tables, and the module is registered per
			// connection rather than compiled in. Read-only connections need it too:
			// they are the ones running the searches.
			Fts5Module.Register(_connection);
			lock (LockConnectionsPool)
			{
				ConnectionsPool.Add(this);
			}
		}
		catch (Exception ex)
		{
			ProcessMalformedDbState(ex);
			throw;
		}
	}

	public ISqlDbTransaction BeginWriteTransaction(bool exclusive = false)
	{
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		return SqlDbTransaction.BeginWriteTransaction(_connection, exclusive);
	}

	public ISqlDbTransaction BeginReadTransaction()
	{
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		return SqlDbTransaction.BeginReadTransaction(_connection);
	}

	public DbCommand CreateCommand(ISqlDbTransaction transaction = null)
	{
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		DbCommand dbCommand = _connection.CreateCommand();
		if (transaction != null)
		{
			dbCommand.Transaction = transaction.Transaction;
		}
		return dbCommand;
	}

	public void Dispose()
	{
		Close();
	}

	public string ExecuteCommand(string sQuery, ISqlDbTransaction transaction)
	{
		if (sQuery.IsNullOrEmpty())
		{
			throw new Exception("No query");
		}
		DbCommand dbCommand = _connection.CreateCommand();
		dbCommand.CommandText = sQuery;
		if (transaction != null)
		{
			dbCommand.Transaction = transaction.Transaction;
		}
		return ExecuteCommand(dbCommand);
	}

	public string ExecuteCommand(DbCommand command)
	{
		if (command == null)
		{
			throw new Exception("Command is null");
		}
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		StringBuilder stringBuilder = new StringBuilder();
		using (DbDataReader dbDataReader = ExecuteReader(command))
		{
			if (dbDataReader == null)
			{
				throw new Exception("Error during query executing.");
			}
			while (dbDataReader.Read())
			{
				bool flag = true;
				for (int i = 0; i < dbDataReader.FieldCount; i++)
				{
					if (flag)
					{
						flag = false;
					}
					else
					{
						stringBuilder.Append(' ');
					}
					if (!(dbDataReader[i] is DBNull))
					{
						stringBuilder.Append(dbDataReader[i]);
					}
				}
				stringBuilder.Append("\r\n");
			}
		}
		return stringBuilder.ToString();
	}

	public int ExecuteNonQuery(string sQuery, ISqlDbTransaction transaction)
	{
		if (sQuery.IsNullOrEmpty())
		{
			throw new Exception("No query");
		}
		DbCommand dbCommand = _connection.CreateCommand();
		dbCommand.CommandText = sQuery;
		if (transaction != null)
		{
			dbCommand.Transaction = transaction.Transaction;
		}
		return ExecuteNonQuery(dbCommand);
	}

	public int ExecuteNonQuery(DbCommand command)
	{
		if (command == null)
		{
			throw new Exception("Command is null");
		}
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		try
		{
			return command.ExecuteNonQuery();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			ProcessMalformedDbState(ex);
			return -2;
		}
	}

	/// <summary>
	/// Returns true when an exception means the database file itself is damaged, as
	/// opposed to an ordinary query or locking failure.
	/// </summary>
	/// <remarks>
	/// This used to substring-match "image is malformed" on the exception message, which
	/// missed the other corruption codes and would break on a reworded SQLite build.
	/// SQLite reports the reason in the result code, so read that instead, walking the
	/// inner exceptions because our own code rethrows plain exceptions around these.
	/// </remarks>
	internal static bool IsCorruptionError(Exception error)
	{
		for (Exception current = error; current != null; current = current.InnerException)
		{
			if (current is SQLiteException sqliteException)
			{
				// Extended result codes pack the primary code into the low byte.
				switch ((SQLiteErrorCode)((int)sqliteException.ResultCode & 0xFF))
				{
				case SQLiteErrorCode.Corrupt:
				case SQLiteErrorCode.NotADb:
				case SQLiteErrorCode.IoErr:
					return true;
				}
			}
		}
		// Fall back to the message for exceptions raised by our own wrappers, which do
		// not carry a result code.
		string message = error?.Message;
		if (message == null)
		{
			return false;
		}
		return message.IndexOf("image is malformed", StringComparison.OrdinalIgnoreCase) >= 0
			|| message.IndexOf("file is not a database", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public void ProcessMalformedDbState(Exception exception)
	{
		if (IsCorruptionError(exception))
		{
			bool num = !Settings.Default.SpotsDbFileMalformed && !Settings.Default.CommentsDbFileMalformed;
			if (_dbFile.Equals(AppHelper.GetDbFilename("dbs")))
			{
				Settings.Default.SpotsDbFileMalformed = true;
			}
			else if (_dbFile.Equals(AppHelper.GetDbFilename("dbc")))
			{
				Settings.Default.CommentsDbFileMalformed = true;
			}
			Settings.Default.Save();
			if (num)
			{
				string text = "Database is malformed, so it will be recreated on next Spotnet start. Please restart Spotnet to make it work properly.";
				Log.Warn(text);
				AppHelper.Error(text);
			}
		}
	}

	public DbDataReader ExecuteReader(string sQuery, ISqlDbTransaction transaction)
	{
		if (sQuery.IsNullOrEmpty())
		{
			throw new Exception("No query");
		}
		DbCommand dbCommand = _connection.CreateCommand();
		dbCommand.CommandText = sQuery;
		if (transaction != null)
		{
			dbCommand.Transaction = transaction.Transaction;
		}
		return ExecuteReader(dbCommand);
	}

	public DbDataReader ExecuteReader(DbCommand command)
	{
		if (command == null)
		{
			throw new Exception("Command is null");
		}
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		try
		{
			return command.ExecuteReader();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			ProcessMalformedDbState(ex);
			return null;
		}
	}

	public long ExecuteScalar(string sQuery, ISqlDbTransaction transaction)
	{
		if (sQuery.IsNullOrEmpty())
		{
			throw new Exception("No query");
		}
		DbCommand dbCommand = _connection.CreateCommand();
		dbCommand.CommandText = sQuery;
		if (transaction != null)
		{
			dbCommand.Transaction = transaction.Transaction;
		}
		return ExecuteScalar(dbCommand);
	}

	public long ExecuteScalar(DbCommand command)
	{
		if (command == null)
		{
			throw new Exception("Command is null");
		}
		if (!CheckThread())
		{
			throw new Exception("Wrong thread");
		}
		try
		{
			object obj = command.ExecuteScalar();
			if (obj == null || obj is DBNull)
			{
				return -1L;
			}
			return Convert.ToInt64(obj);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			ProcessMalformedDbState(ex);
			return -1L;
		}
	}

	internal static void CloseAllConnections()
	{
		lock (LockConnectionsPool)
		{
			foreach (SQliteDb item in ConnectionsPool.ToList())
			{
				item.Dispose();
			}
			SQLiteFactory.Instance.Dispose();
			SQLiteConnection.ClearAllPools();
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
	}

	private bool CheckThread()
	{
		if (_threadId == -1 || _threadId == Thread.CurrentThread.ManagedThreadId)
		{
			return true;
		}
		AppHelper.Error("Wrong thread");
		return false;
	}

	private void Close()
	{
		if (_connection == null)
		{
			return;
		}
		try
		{
			_connection.Close();
			_connection.Dispose();
			lock (LockConnectionsPool)
			{
				ConnectionsPool.Remove(this);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		finally
		{
			_connection = null;
		}
	}
}
