using System;
using System.Data.Common;
using NLog;
using Spotnet.DAL;

namespace Spotnet.Model;

internal class MinimumId : IMinimumId
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly string _name;

	private bool _isActive;

	private long _value;

	private string DbFieldName => $"minId_{_name}";

	public long Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (_value != value)
			{
				_value = value;
				if (IsActive)
				{
					SavetoDb();
				}
			}
		}
	}

	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			if (_isActive != value)
			{
				_isActive = value;
				SavetoDb();
			}
		}
	}

	public MinimumId(string name)
	{
		_value = 0L;
		_isActive = false;
		_name = name;
		ReadFromDb();
	}

	public void Reset()
	{
		_value = 0L;
		_isActive = false;
		SavetoDb();
	}

	public void UpdateIfRequired(long minId)
	{
		if (minId > 0 && (Value == 0L || Value > minId))
		{
			Value = minId;
		}
	}

	private void SavetoDb()
	{
		Log.Debug("Save minid to db {0} {1} {2}", _name, _value, _isActive);
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
		if (sqlDb.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS userinfo(field TEXT, value TEXT)", sqlDbTransaction) != 0)
		{
			throw new Exception("CREATE TABLE userinfo");
		}
		sqlDb.ExecuteNonQuery($"DELETE FROM userinfo WHERE field='{DbFieldName}'", sqlDbTransaction);
		if (IsActive)
		{
			DbCommand dbCommand = sqlDb.CreateCommand(sqlDbTransaction);
			dbCommand.CommandText = $"INSERT INTO userinfo(field, value) VALUES('{DbFieldName}', '{Value}')";
			if (dbCommand.ExecuteNonQuery() != 1)
			{
				throw new Exception("INSERT INTO userinfo");
			}
		}
		sqlDbTransaction.Commit();
	}

	private void ReadFromDb()
	{
		try
		{
			using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
			DbCommand dbCommand = sqlDb.CreateCommand();
			dbCommand.CommandText = $"SELECT value FROM userinfo WHERE field='{DbFieldName}' LIMIT 1";
			using DbDataReader dbDataReader = dbCommand.ExecuteReader();
			if (dbDataReader.Read())
			{
				_isActive = long.TryParse(dbDataReader.GetString(0), out var result);
				_value = result;
			}
			else
			{
				_isActive = false;
			}
		}
		catch (Exception ex)
		{
			_isActive = false;
			if (ex.Message.Contains("no such table: userinfo"))
			{
				SavetoDb();
			}
			else
			{
				Log.Debug(ex.Message);
			}
		}
		finally
		{
			if (_value > 0)
			{
				Log.Debug("Read minid from db {0} {1} {2}", _name, _value, _isActive);
			}
		}
	}
}
