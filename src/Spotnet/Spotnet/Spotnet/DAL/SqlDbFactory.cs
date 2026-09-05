using NLog;
using Spotnet.Helpers;

namespace Spotnet.DAL;

public static class SqlDbFactory
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static ISqlDb CreateSqlDb(string connectionString, bool isReadOnly = false)
	{
		if (!isReadOnly)
		{
			return new SQliteDb(connectionString, bReadOnly: false);
		}
		try
		{
			return new SQliteDb(connectionString, bReadOnly: true);
		}
		catch (System.Exception ex)
		{
			// A read-only connection cannot replay a -wal file that needs recovery, so
			// fall back to a writable one rather than failing the caller's query. The
			// callers that ask for read-only only ever run SELECTs.
			Log.Warn("Read-only open of {0} failed ({1}), falling back to a writable connection", connectionString, ex.Message);
			return new SQliteDb(connectionString, bReadOnly: false);
		}
	}

	public static ISqlDb CreateSqlDbSpots(bool isReadOnly = false)
	{
		return CreateSqlDb(AppHelper.GetDbFilename("dbs"), isReadOnly);
	}

	public static ISqlDb CreateSqlDbNewznabSpots(bool isReadOnly = false)
	{
		return CreateSqlDb(AppHelper.GetDbFilename("newznab.dbs"), isReadOnly);
	}

	public static ISqlDb CreateSqlDbComments(bool isReadOnly = false)
	{
		return CreateSqlDb(AppHelper.GetDbFilename("dbc"), isReadOnly);
	}
}
