using System;
using System.Data.Common;

namespace Spotnet.DAL;

public interface ISqlDb : IDisposable
{
	bool Connected { get; }

	string Filename { get; }

	ISqlDbTransaction BeginWriteTransaction(bool exclusive = false);

	ISqlDbTransaction BeginReadTransaction();

	DbCommand CreateCommand(ISqlDbTransaction transaction = null);

	string ExecuteCommand(string sQuery, ISqlDbTransaction transaction);

	string ExecuteCommand(DbCommand command);

	int ExecuteNonQuery(string command, ISqlDbTransaction transaction);

	int ExecuteNonQuery(DbCommand command);

	DbDataReader ExecuteReader(string sQuery, ISqlDbTransaction transaction);

	DbDataReader ExecuteReader(DbCommand command);

	long ExecuteScalar(string sQuery, ISqlDbTransaction transaction);

	long ExecuteScalar(DbCommand command);

	void ProcessMalformedDbState(Exception exception);
}
