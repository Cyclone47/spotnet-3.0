using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Properties;

namespace Spotnet.DAL;

internal class SqlDbTransaction : ISqlDbTransaction, IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly Dictionary<string, ReaderWriterLock> RwlDict = new Dictionary<string, ReaderWriterLock>();

	private DbTransaction _transaction;

	private readonly bool _isWrite;

	private static readonly TimeSpan ReaderTimeout = TimeSpan.FromSeconds(10.0);

	private static readonly TimeSpan WriterTimeout = TimeSpan.FromSeconds(20.0);

	private readonly ReaderWriterLock _rwl;

	private bool _exclusive;

	public DbTransaction Transaction => _transaction;

	public static bool WaitForRelease(string dataSource)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(dataSource);
		if (fileNameWithoutExtension.IsNullOrEmpty())
		{
			return true;
		}
		bool result = true;
		if (RwlDict.TryGetValue(fileNameWithoutExtension, out var value))
		{
			try
			{
				value.AcquireReaderLock(ReaderTimeout);
			}
			catch (Exception ex)
			{
				Log.Warn("AcquireReaderLock failed: " + ex.Message);
				result = false;
			}
			finally
			{
				try
				{
					value.ReleaseReaderLock();
				}
				catch (Exception ex2)
				{
					Log.Warn("ReleaseReaderLock failed: " + ex2.Message);
					result = false;
				}
			}
		}
		return result;
	}

	private SqlDbTransaction(DbConnection connection, bool isWrite = false)
	{
		if (connection == null || connection.DataSource == null)
		{
			throw new ArgumentNullException("connection");
		}
		_isWrite = isWrite;
		if (!RwlDict.ContainsKey(connection.DataSource))
		{
			_rwl = new ReaderWriterLock();
			RwlDict.Add(connection.DataSource, _rwl);
		}
		else
		{
			_rwl = RwlDict[connection.DataSource];
		}
	}

	public void Dispose()
	{
		if (_isWrite)
		{
			_transaction?.Dispose();
			ReleaseWriterLock();
		}
		else
		{
			ReleaseReaderLock();
		}
	}

	public void Commit()
	{
		DbTransaction transaction = _transaction;
		if (transaction == null)
		{
			return;
		}
		try
		{
			if (!_exclusive)
			{
				AcquireWriterLock();
			}
			transaction.Commit();
		}
		catch (ApplicationException)
		{
			throw new Exception("Failed to write to db as it is blocked by long update operation. Please wait.");
		}
		finally
		{
			if (!_exclusive)
			{
				ReleaseWriterLock();
			}
		}
	}

	public void Rollback()
	{
		DbTransaction transaction = _transaction;
		if (transaction != null && transaction.Connection != null)
		{
			transaction.Rollback();
		}
	}

	public static ISqlDbTransaction BeginWriteTransaction(DbConnection connection, bool exclusive = false)
	{
		SqlDbTransaction sqlDbTransaction = new SqlDbTransaction(connection, isWrite: true);
		if (exclusive)
		{
			try
			{
				sqlDbTransaction.AcquireWriterLock();
			}
			catch (ApplicationException)
			{
				throw new Exception("Failed to write from db as it is blocked by long db update operation. Even geduld a.u.b.");
			}
		}
		sqlDbTransaction._transaction = connection.BeginTransaction();
		sqlDbTransaction._exclusive = exclusive;
		return sqlDbTransaction;
	}

	public static ISqlDbTransaction BeginReadTransaction(DbConnection connection)
	{
		SqlDbTransaction sqlDbTransaction = new SqlDbTransaction(connection);
		try
		{
			sqlDbTransaction.AcquireReaderLock();
			return sqlDbTransaction;
		}
		catch (ApplicationException)
		{
			throw new Exception(Words.DbLockTimeout);
		}
	}

	private void AcquireWriterLock()
	{
		_rwl.AcquireWriterLock(WriterTimeout);
	}

	private void ReleaseWriterLock()
	{
		if (_isWrite && _rwl.IsWriterLockHeld)
		{
			_rwl.ReleaseWriterLock();
		}
	}

	private void AcquireReaderLock()
	{
		_rwl.AcquireReaderLock(ReaderTimeout);
	}

	private void ReleaseReaderLock()
	{
		if (!_isWrite && _rwl.IsReaderLockHeld)
		{
			_rwl.ReleaseReaderLock();
		}
	}
}
