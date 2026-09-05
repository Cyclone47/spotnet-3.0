using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.DAL;

internal static class SpotSaver
{
	private const int SpamReportTablesAnalyseThreshold = 15000;

	private static readonly Logger Log;

	private static Timer _updateStatusMessageTimer;

	private static SaveSpotsRow _spotsRowsResult;

	private static SaveCommentsRow _commentsRowsResult;

	private static CancellationToken _cancelToken;

	private static readonly object SyncRoot;

	private static bool _alreadyRunning;

	private static Action<string, int> _statusChanged;

	private static long _maxDbId;

	private static int _spamReportTablesAnalyseCounter;

	private static SpotsListViewModel SpotsListVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).SpotsList;

	internal static event Action OnDbSettingsUpdate;

	static SpotSaver()
	{
		Log = LogManager.GetCurrentClassLogger();
		SyncRoot = new object();
		_spamReportTablesAnalyseCounter = 5000;
	}

	public static async Task<SaveSpotsRow> SaveHeadersAsync(BlockingCollection<List<Spot>> spotsToAddAndRemove, SaveSpotsRow headerResults, long maxDbId, Action<string, int> reportAction, Action<SaveSpotsRow> onSpotsUpdate, CancellationToken cToken)
	{
		lock (SyncRoot)
		{
			if (_alreadyRunning)
			{
				throw new Exception("Task is already running");
			}
			_alreadyRunning = true;
		}
		_statusChanged = reportAction ?? ((Action<string, int>)delegate
		{
		});
		_spotsRowsResult = headerResults;
		_cancelToken = cToken;
		_maxDbId = maxDbId;
		_updateStatusMessageTimer = new Timer(UpdateStatusMessage, null, TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(1.0));
		return await Task.Factory.StartNew(() => RunSpotsDbUpdater(spotsToAddAndRemove, onSpotsUpdate), _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task<SaveSpotsRow> t)
		{
			spotsToAddAndRemove.CompleteAdding();
			lock (SyncRoot)
			{
				_alreadyRunning = false;
			}
			_updateStatusMessageTimer?.Dispose();
			if (t.IsCanceled || _cancelToken.IsCancellationRequested)
			{
				return (SaveSpotsRow)null;
			}
			if (t.Exception != null)
			{
				Log.Error(t.Exception.Message);
				throw t.Exception;
			}
			return t.Result;
		});
	}

	internal static async Task RemoveOutOfRetentionSpotsAsync(CancellationToken cancelToken)
	{
		lock (SyncRoot)
		{
			if (_alreadyRunning)
			{
				throw new Exception("Task is already running");
			}
			_alreadyRunning = true;
		}
		_cancelToken = cancelToken;
		await Task.Factory.StartNew(RemoveOutOfRetentionSpots, cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task t)
		{
			lock (SyncRoot)
			{
				_alreadyRunning = false;
			}
			if (t.Exception != null)
			{
				Log.Error(t.Exception.Message);
				throw t.Exception;
			}
		});
	}

	private static void RemoveOutOfRetentionSpots()
	{
		try
		{
			int num = 0;
			using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots())
			{
				SetDbSettingsForInsertionImprove(sqlDb);
				DbCommand dbCommand = sqlDb.CreateCommand();
				dbCommand.CommandText = "DELETE FROM spots WHERE rowid IN (SELECT rowid FROM spots WHERE date<? LIMIT 2000);";
				DbParameter dbParameter = dbCommand.CreateParameter();
				dbParameter.Value = DbUpdater.RetentionStartDate.ToUnixTime();
				dbCommand.Parameters.Add(dbParameter);
				int num2;
				do
				{
					using (ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction(exclusive: true))
					{
						dbCommand.Transaction = sqlDbTransaction.Transaction;
						num2 = sqlDb.ExecuteNonQuery(dbCommand);
						if (num2 < 0)
						{
							throw new Exception("Spots.ExecuteNonQuery.RemoveOutOfRetentionSpots");
						}
						sqlDbTransaction.Commit();
						Log.Debug("Spots removed: " + num2);
						num += num2;
					}
					Thread.Sleep(50);
				}
				while (num2 != 0 && !_cancelToken.IsCancellationRequested);
			}
			if (num > 0)
			{
				Log.Debug("Out of retention spots removed: " + num);
			}
		}
		finally
		{
			long databaseMin = Settings.Default.DatabaseMin;
			using (ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true))
			{
				// Retention has just deleted rows, so the counts must be exact here
				// rather than whatever the import throttle last left behind.
				UpdateDatabaseSettings(db, forceExactCounts: true);
			}
			if (Settings.Default.DatabaseMin != databaseMin)
			{
				Headers.UpdateMinHeader(Settings.Default.DatabaseMin);
			}
		}
	}

	public static async Task SaveSpamReportsAsync(BlockingCollection<List<SpamReport>> reports, Action<string, int> reportAction, CancellationToken cToken)
	{
		lock (SyncRoot)
		{
			if (_alreadyRunning)
			{
				throw new Exception("Task is already running");
			}
			_alreadyRunning = true;
		}
		_statusChanged = reportAction ?? ((Action<string, int>)delegate
		{
		});
		_cancelToken = cToken;
		_updateStatusMessageTimer = new Timer(UpdateStatusMessage, null, TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(1.0));
		await Task.Factory.StartNew(delegate
		{
			RunSpamReportsDbUpdater(reports);
		}, _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task t)
		{
			reports.CompleteAdding();
			lock (SyncRoot)
			{
				_alreadyRunning = false;
			}
			_updateStatusMessageTimer?.Dispose();
			if (t.IsCanceled || _cancelToken.IsCancellationRequested || t.Exception == null)
			{
				return;
			}
			Log.Error(t.Exception.Message);
			throw t.Exception;
		});
	}

	private static void UpdateStatusMessage(object state)
	{
		if (SpamReports.InProgress)
		{
			if (SpamReports.IsAnyReportsReceived)
			{
				int progressValue = SpamReports.ProgressValue;
				_statusChanged(Words.SpamReportsUpdating + SpamReports.DownloadSpeedString, progressValue);
			}
		}
		else if (Headers.InProgress)
		{
			if (_spotsRowsResult != null && _spotsRowsResult.SpotsAdded != 0)
			{
				int num = Headers.ProgressValue;
				if (num == 0)
				{
					num = 1;
				}
				_statusChanged(SpotHelper.FormatLong(_spotsRowsResult.SpotsAdded) + " " + Words.newWord + " " + Words.Spots + " " + Words.found + Headers.DownloadSpeedString, num);
			}
		}
		else if (Comments.InProgress)
		{
			if (_commentsRowsResult != null && _commentsRowsResult.CommentsAdded != 0)
			{
				int num2 = Comments.ProgressValue;
				if (num2 == 0)
				{
					num2 = 1;
				}
				_statusChanged(SpotHelper.FormatLong(_commentsRowsResult.CommentsAdded) + " " + Words.newWord + " " + Words.comments + " " + Words.found + Comments.DownloadSpeedString, num2);
			}
		}
		else
		{
			_statusChanged(Words.LookingFor + " " + Words.newWord + " " + Words.Spots + "...", -1);
		}
	}

	private static SaveSpotsRow RunSpotsDbUpdater(BlockingCollection<List<Spot>> spotsToAddAndRemove, Action<SaveSpotsRow> onSpotsUpdate)
	{
		try
		{
			foreach (List<Spot> item in spotsToAddAndRemove.GetConsumingEnumerable(_cancelToken))
			{
				long minRowId = -1L;
				try
				{
					if (!_cancelToken.IsCancellationRequested)
					{
						long databaseMax = Settings.Default.DatabaseMax;
						_spotsRowsResult.Add(UpdateSpotsDb(item));
						if (databaseMax < Settings.Default.DatabaseMax)
						{
							minRowId = databaseMax + 1;
						}
						if (!_cancelToken.IsCancellationRequested)
						{
							continue;
						}
					}
				}
				finally
				{
					SpotsListVm.RefreshSpotsListWithNewItemsAsync(minRowId).Forget();
					DispatcherHelper.CheckBeginInvokeOnUI(delegate
					{
						if (onSpotsUpdate != null)
						{
							onSpotsUpdate(_spotsRowsResult);
						}
					});
				}
				break;
			}
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		catch (OperationCanceledException)
		{
		}
		return _spotsRowsResult;
	}

	private static void RunSpamReportsDbUpdater(BlockingCollection<List<SpamReport>> reports)
	{
		int num = 0;
		try
		{
			foreach (List<SpamReport> item in reports.GetConsumingEnumerable(_cancelToken))
			{
				if (!_cancelToken.IsCancellationRequested)
				{
					UpdateSpamReportsDb(item);
					if (item != null)
					{
						num += item.Count;
					}
					if (_cancelToken.IsCancellationRequested)
					{
						break;
					}
					continue;
				}
				break;
			}
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		catch (OperationCanceledException)
		{
		}
		if (num > 0)
		{
			Log.Debug("Spam reports added to database: " + num);
		}
	}

	public static async Task SaveCommentSpotRelationAsync(BlockingCollection<List<Comment>> commentsToAddAndRemove, Action<string, int> reportAction, CancellationToken cToken)
	{
		lock (SyncRoot)
		{
			if (_alreadyRunning)
			{
				throw new Exception("Task is already running");
			}
			_alreadyRunning = true;
		}
		_statusChanged = reportAction ?? ((Action<string, int>)delegate
		{
		});
		_commentsRowsResult = new SaveCommentsRow();
		_cancelToken = cToken;
		_updateStatusMessageTimer = new Timer(UpdateStatusMessage, null, TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(1.0));
		await Task.Factory.StartNew(() => RunCommentsDbUpdater(commentsToAddAndRemove), _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task<SaveCommentsRow> t)
		{
			lock (SyncRoot)
			{
				_alreadyRunning = false;
			}
			_updateStatusMessageTimer?.Dispose();
			if (t.IsCanceled || _cancelToken.IsCancellationRequested || t.Exception == null)
			{
				return;
			}
			Log.Error(t.Exception.Message);
			commentsToAddAndRemove.CompleteAdding();
			throw t.Exception;
		});
	}

	public static void InitializeCommentsDb()
	{
		AddComments(new List<Comment>());
	}

	public static void InitializeSpamReportsDb()
	{
		UpdateSpamReportsDb(new List<SpamReport>());
	}

	private static SaveCommentsRow RunCommentsDbUpdater(BlockingCollection<List<Comment>> commentsToAddAndRemove)
	{
		try
		{
			foreach (List<Comment> item in commentsToAddAndRemove.GetConsumingEnumerable(_cancelToken))
			{
				if (!_cancelToken.IsCancellationRequested)
				{
					_commentsRowsResult.CommentsAdded += item.Count;
					AddComments(item);
					if (_cancelToken.IsCancellationRequested)
					{
						break;
					}
					continue;
				}
				break;
			}
		}
		catch (OperationCanceledException)
		{
			return _commentsRowsResult;
		}
		finally
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		return _commentsRowsResult;
	}

	private static SaveSpotsRow UpdateSpotsDb(ICollection<Spot> spotList)
	{
		if (!spotList.Any())
		{
			return null;
		}
		bool flag = spotList.First().Article < _maxDbId;
		SaveSpotsRow saveSpotsRow = new SaveSpotsRow();
		DBNull value = DBNull.Value;
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		try
		{
			SetDbSettingsForInsertionImprove(sqlDb);
			using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
			List<DbParameter> paramsList = new List<DbParameter>();
			List<DbParameter> list = new List<DbParameter>();
			List<DbParameter> list2 = new List<DbParameter>();
			using (DbCommand command = InitializeCommandSelectSpot(sqlDb, paramsList, sqlDbTransaction))
			{
				using DbCommand dbCommand2 = InitializeCommandAddToSpots(sqlDb, list, sqlDbTransaction);
				using DbCommand dbCommand = InitializeCommandRemoveFromSpots(sqlDb, list2, sqlDbTransaction);
				List<Spot> list3 = new List<Spot>();
				foreach (Spot spot in spotList)
				{
					if (_cancelToken.IsCancellationRequested)
					{
						return new SaveSpotsRow();
					}
					string messageIdToRemove;
					bool num = spot.IsMarkedAsDisposeReport(out messageIdToRemove);
					string messageId = (num ? messageIdToRemove : spot.MessageId);
					long origSpotRowId;
					int keyId;
					string modulus;
					bool origSpotIsSpotnetDisposeReportFromAuthorOfSpot;
					long stamp;
					bool flag2 = IsSpotInDbAlready(command, messageId, out origSpotRowId, out keyId, out modulus, out origSpotIsSpotnetDisposeReportFromAuthorOfSpot, out stamp);
					if (num)
					{
						if (flag2)
						{
							if (keyId != 2 && keyId != 5 && origSpotRowId < spot.Article && (!spot.IsSpotnetDisposeReportFromAuthorOfSpot || (!(spot.Modulus != modulus) && spot.Stamp - stamp <= 432000)))
							{
								list2[0].Value = origSpotRowId;
								list3.RemoveAll((Spot s) => s.Article == origSpotRowId);
								int num2 = dbCommand.ExecuteNonQuery();
								if (num2 < 0)
								{
									throw new Exception("Spots.ExecuteNonQuery.remove.from.spots");
								}
								if (num2 > 0)
								{
									saveSpotsRow.SpotsDeleted++;
								}
							}
							continue;
						}
						long num3 = (spot.IsSpotnetDisposeReportFromAuthorOfSpot ? 999 : 1);
						object[] array = new object[13]
						{
							spot.Article,
							spot.KeyID,
							spot.Category,
							spot.Category * 100 + spot.SubCat,
							spot.Category * 100 + AppHelper.TranslateInfo(spot.Category, spot.SubCats),
							spot.Stamp,
							num3,
							spot.SubCats,
							spot.Poster,
							messageIdToRemove,
							spot.Title,
							messageIdToRemove,
							(spot.Modulus == null) ? ((IConvertible)value) : ((IConvertible)spot.Modulus)
						};
						for (int i = 0; i < array.Length; i++)
						{
							list[i].Value = array[i];
						}
						if (dbCommand2.ExecuteNonQuery() < 0)
						{
							throw new Exception("Spots.ExecuteNonQuery.add.spam.report");
						}
					}
					else
					{
						if (spot.KeyID == 2 || spot.KeyID == 5)
						{
							continue;
						}
						bool flag3 = false;
						if (flag2)
						{
							if (keyId == 2 || keyId == 5)
							{
								bool flag4 = origSpotIsSpotnetDisposeReportFromAuthorOfSpot && (spot.Modulus != modulus || stamp - spot.Stamp > 432000);
								if (origSpotRowId < spot.Article || flag4)
								{
									list2[0].Value = origSpotRowId;
									list3.RemoveAll((Spot s) => s.Article == origSpotRowId);
									int num4 = dbCommand.ExecuteNonQuery();
									if (num4 < 0)
									{
										throw new Exception("Spots.ExecuteNonQuery.remove.from.spots");
									}
									if (num4 > 0)
									{
										flag3 = true;
									}
								}
							}
						}
						else
						{
							flag3 = true;
						}
						if (flag3)
						{
							if (!spot.SubCats.IsNullOrWhiteSpace())
							{
								string text = spot.Category + spot.SubCats.Replace("|", " " + spot.Category);
								spot.SubCats = spot.Category + " " + text.Substring(0, text.Length - 2);
								spot.SubCats = spot.SubCats.Trim();
							}
							else
							{
								spot.SubCats = spot.Category.ToString();
							}
							object[] array2 = new object[13]
							{
								spot.Article,
								spot.KeyID,
								spot.Category,
								spot.Category * 100 + spot.SubCat,
								spot.Category * 100 + AppHelper.TranslateInfo(spot.Category, spot.SubCats),
								spot.Stamp,
								spot.Filesize,
								spot.SubCats,
								spot.Poster,
								(spot.Tag == null) ? ((IConvertible)value) : ((IConvertible)spot.Tag),
								spot.Title,
								spot.MessageId,
								(spot.Modulus == null) ? ((IConvertible)value) : ((IConvertible)spot.Modulus)
							};
							for (int j = 0; j < array2.Length; j++)
							{
								list[j].Value = array2[j];
							}
							int num5 = dbCommand2.ExecuteNonQuery();
							if (num5 < 0)
							{
								throw new Exception("Spots.ExecuteNonQuery.add.to.spots");
							}
							if (num5 > 0)
							{
								list3.Add(spot);
							}
						}
					}
				}
				int count = list3.Count;
				if (count > 0)
				{
					saveSpotsRow.SpotsAdded += count;
					foreach (Spot item in list3)
					{
						if (!flag)
						{
							saveSpotsRow.NewCats[item.Category]++;
						}
					}
				}
			}
			sqlDbTransaction.Commit();
			return saveSpotsRow;
		}
		catch (Exception ex)
		{
			sqlDb.ProcessMalformedDbState(ex);
			throw;
		}
		finally
		{
			UpdateDatabaseSettings(sqlDb);
		}
	}

	private static void UpdateSpamReportsDb(ICollection<SpamReport> reports)
	{
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		try
		{
			if (!reports.Any())
			{
				return;
			}
			SetDbSettingsForInsertionImprove(sqlDb);
			Tracker tracker = new Tracker();
			using (ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction())
			{
				using (DbCommand dbCommand = sqlDb.CreateCommand(sqlDbTransaction))
				{
					using DbCommand dbCommand2 = sqlDb.CreateCommand(sqlDbTransaction);
					using DbCommand dbCommand3 = sqlDb.CreateCommand(sqlDbTransaction);
					using DbCommand dbCommand4 = sqlDb.CreateCommand(sqlDbTransaction);
					using DbCommand dbCommand5 = sqlDb.CreateCommand(sqlDbTransaction);
					dbCommand.CommandText = "SELECT 1 FROM spamreports WHERE msgid=? AND modulus=? LIMIT 1";
					DbParameter dbParameter = dbCommand.CreateParameter();
					DbParameter dbParameter2 = dbCommand.CreateParameter();
					dbCommand.Parameters.Add(dbParameter);
					dbCommand.Parameters.Add(dbParameter2);
					dbCommand2.CommandText = "INSERT OR IGNORE INTO spamreports(rowid,msgid,modulus,date,reportmsgid,sender) VALUES (?,?,?,?,?,?);";
					DbParameter dbParameter3 = dbCommand2.CreateParameter();
					DbParameter dbParameter4 = dbCommand2.CreateParameter();
					DbParameter dbParameter5 = dbCommand2.CreateParameter();
					DbParameter dbParameter6 = dbCommand2.CreateParameter();
					DbParameter dbParameter7 = dbCommand2.CreateParameter();
					DbParameter dbParameter8 = dbCommand2.CreateParameter();
					dbCommand2.Parameters.Add(dbParameter3);
					dbCommand2.Parameters.Add(dbParameter4);
					dbCommand2.Parameters.Add(dbParameter5);
					dbCommand2.Parameters.Add(dbParameter6);
					dbCommand2.Parameters.Add(dbParameter7);
					dbCommand2.Parameters.Add(dbParameter8);
					dbCommand3.CommandText = "SELECT cnt FROM spamgroup WHERE msgid=?";
					DbParameter dbParameter9 = dbCommand3.CreateParameter();
					dbCommand3.Parameters.Add(dbParameter9);
					dbCommand4.CommandText = "INSERT INTO spamgroup (msgid,cnt) VALUES (?,?)";
					DbParameter dbParameter10 = dbCommand4.CreateParameter();
					DbParameter dbParameter11 = dbCommand4.CreateParameter();
					dbCommand4.Parameters.Add(dbParameter10);
					dbCommand4.Parameters.Add(dbParameter11);
					dbCommand5.CommandText = "UPDATE spamgroup SET cnt=? WHERE msgid=?";
					DbParameter dbParameter12 = dbCommand5.CreateParameter();
					DbParameter dbParameter13 = dbCommand5.CreateParameter();
					dbCommand5.Parameters.Add(dbParameter12);
					dbCommand5.Parameters.Add(dbParameter13);
					foreach (SpamReport report in reports)
					{
						if (_cancelToken.IsCancellationRequested)
						{
							return;
						}
						dbParameter.Value = report.MessageId;
						dbParameter2.Value = report.Modulus;
						object obj = dbCommand.ExecuteScalar();
						if (obj != null && !(obj is DBNull) && (long)obj >= 1)
						{
							continue;
						}
						dbParameter3.Value = report.ReportId;
						dbParameter4.Value = report.MessageId;
						dbParameter5.Value = report.Modulus;
						dbParameter6.Value = report.Date.ToUnixTime();
						dbParameter7.Value = report.BodyMessageId;
						dbParameter8.Value = report.Username;
						if (sqlDb.ExecuteNonQuery(dbCommand2) < 0)
						{
							throw new Exception("SpamInsert.insert.ExecuteNonQuery");
						}
						dbParameter9.Value = report.MessageId;
						obj = dbCommand3.ExecuteScalar();
						int num = ((obj == null || obj is DBNull) ? (-1) : ((int)obj));
						if (num == -1)
						{
							dbParameter10.Value = report.MessageId;
							dbParameter11.Value = 1;
							if (dbCommand4.ExecuteNonQuery() < 0)
							{
								throw new Exception("SpamGroupInsert.ExecuteNonQuery");
							}
							SpamReportTablesAnalyse(sqlDb);
						}
						else
						{
							dbParameter12.Value = num + 1;
							dbParameter13.Value = report.MessageId;
							if (dbCommand5.ExecuteNonQuery() < 0)
							{
								throw new Exception("SpamGroupUpdate.ExecuteNonQuery");
							}
						}
					}
				}
				sqlDbTransaction.Commit();
			}
			tracker.Debug("Spam reports saved: " + reports.Count);
		}
		catch (Exception ex)
		{
			sqlDb.ProcessMalformedDbState(ex);
			throw;
		}
	}

	private static void SpamReportTablesAnalyse(ISqlDb db)
	{
		if (++_spamReportTablesAnalyseCounter > 15000)
		{
			Tracker tracker = new Tracker();
			_spamReportTablesAnalyseCounter = 0;
			db.ExecuteNonQuery("ANALYZE spamreports; ANALYZE spamgroup;", null);
			tracker.Debug("Analyse spam report tables");
		}
	}

	private static bool IsSpotInDbAlready(DbCommand command, string messageId, out long rowId, out int keyId, out string modulus, out bool origSpotIsSpotnetDisposeReportFromAuthorOfSpot, out long stamp)
	{
		keyId = -1;
		rowId = -1L;
		modulus = "";
		stamp = -1L;
		origSpotIsSpotnetDisposeReportFromAuthorOfSpot = false;
		command.Parameters[0].Value = messageId;
		using DbDataReader dbDataReader = command.ExecuteReader();
		if (dbDataReader.Read())
		{
			rowId = dbDataReader.GetInt64(0);
			keyId = dbDataReader.GetInt32(1);
			modulus = Convert.ToString(dbDataReader.GetValue(2));
			long @int = dbDataReader.GetInt64(3);
			origSpotIsSpotnetDisposeReportFromAuthorOfSpot = @int == 999;
			stamp = dbDataReader.GetInt64(4);
			return true;
		}
		return false;
	}

	private static DbCommand InitializeCommandSelectSpot(ISqlDb db, List<DbParameter> paramsList, ISqlDbTransaction transaction)
	{
		DbCommand dbCommand = db.CreateCommand(transaction);
		dbCommand.CommandText = "SELECT rowid,key,modulus,filesize,date FROM spots WHERE msgid=? ORDER BY rowid";
		DbParameter dbParameter = dbCommand.CreateParameter();
		paramsList.Add(dbParameter);
		dbCommand.Parameters.Add(dbParameter);
		return dbCommand;
	}

	private static DbCommand InitializeCommandAddToSpots(ISqlDb db, List<DbParameter> paramsList, ISqlDbTransaction transaction)
	{
		DbCommand dbCommand = db.CreateCommand(transaction);
		dbCommand.CommandText = "INSERT OR IGNORE INTO spots(rowid,key,cat,subcat,extcat,date,filesize,cats,sender,tag,subject,msgid,modulus) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?);";
		for (int i = 0; i < 13; i++)
		{
			DbParameter dbParameter = dbCommand.CreateParameter();
			paramsList.Add(dbParameter);
			dbCommand.Parameters.Add(dbParameter);
		}
		return dbCommand;
	}

	private static DbCommand InitializeCommandRemoveFromSpots(ISqlDb db, List<DbParameter> paramsList, ISqlDbTransaction transaction)
	{
		DbCommand dbCommand = db.CreateCommand(transaction);
		dbCommand.CommandText = "DELETE FROM spots WHERE rowid=?;";
		DbParameter dbParameter = dbCommand.CreateParameter();
		paramsList.Add(dbParameter);
		dbCommand.Parameters.Add(dbParameter);
		return dbCommand;
	}

	internal static void EnsureCommentsFts5(ISqlDb db)
	{
		long commentsExists = db.ExecuteScalar(
			"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='comments'", null);
		if (commentsExists == 0)
		{
			using ISqlDbTransaction createTransaction = db.BeginWriteTransaction();
			if (db.ExecuteNonQuery(SpotsSchema.CreateComments, createTransaction) < -1)
			{
				throw new Exception("Could not create the comments FTS5 database");
			}
			createTransaction.Commit();
			return;
		}

		long isFts5 = db.ExecuteScalar(
			"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='comments' AND lower(sql) LIKE '%using fts5%'", null);
		if (isFts5 == 1)
		{
			return;
		}

		Log.Info("Migrating comments full-text index to FTS5");
		using ISqlDbTransaction transaction = db.BeginWriteTransaction(exclusive: true);
		if (db.ExecuteNonQuery("DROP TABLE IF EXISTS comments_fts5", transaction) < -1 ||
			db.ExecuteNonQuery("CREATE VIRTUAL TABLE comments_fts5 USING fts5(spot)", transaction) < -1 ||
			db.ExecuteNonQuery("INSERT INTO comments_fts5(rowid, spot) SELECT rowid, spot FROM comments", transaction) < -1 ||
			db.ExecuteNonQuery("DROP TABLE comments", transaction) < -1 ||
			db.ExecuteNonQuery("ALTER TABLE comments_fts5 RENAME TO comments", transaction) < -1)
		{
			throw new Exception("Could not migrate the comments database to FTS5");
		}
		transaction.Commit();
		Log.Info("Comments full-text index migrated to FTS5");
	}

	private static void AddComments(IEnumerable<Comment> comments)
	{
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbComments();
		try
		{
			// Must run before WAL is enabled and before the first write: page_size cannot
			// be changed afterwards. The comments store holds large FTS text blobs and
			// has always used 16 KB pages - the import path used to set this on every
			// batch, where it was a no-op except on the very first one that created the
			// database. Setting it here keeps that, without the per-batch pragma.
			sqlDb.ExecuteNonQuery("PRAGMA page_size = " + SpotsSchema.CommentsPageSize, null);
			SetDbSettingsForInsertionImprove(sqlDb);
			EnsureCommentsFts5(sqlDb);
			using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
			List<Comment> list = comments.ToList();
			new Tracker();
			using (DbCommand dbCommand = sqlDb.CreateCommand(sqlDbTransaction))
			{
				dbCommand.CommandText = "INSERT OR IGNORE INTO comments(rowid, spot) VALUES (?,?);";
				DbParameter dbParameter = dbCommand.CreateParameter();
				dbCommand.Parameters.Add(dbParameter);
				DbParameter dbParameter2 = dbCommand.CreateParameter();
				dbCommand.Parameters.Add(dbParameter2);
				foreach (Comment item in list)
				{
					if (_cancelToken.IsCancellationRequested)
					{
						return;
					}
					dbParameter.Value = item.Article;
					dbParameter2.Value = item.MessageId;
					if (dbCommand.ExecuteNonQuery() < 0)
					{
						throw new Exception("Comments.ExecuteNonQuery");
					}
				}
			}
			sqlDbTransaction.Commit();
		}
		catch (Exception ex)
		{
			sqlDb.ProcessMalformedDbState(ex);
			throw;
		}
	}

	internal static void SetDbSettingsForInsertionImprove(ISqlDb db)
	{
		using (db.BeginReadTransaction())
		{
			// Write-ahead logging keeps the main database file intact by construction:
			// writers append to a side file instead of overwriting live pages, so an
			// interrupted write cannot leave a torn database behind. The old rollback
			// journal combined with synchronous=OFF could, and that is where the
			// "database disk image is malformed" reports came from.
			string journalMode = db.ExecuteCommand("PRAGMA journal_mode = WAL", null)?.Trim();
			if (!"wal".Equals(journalMode, StringComparison.OrdinalIgnoreCase))
			{
				Log.Warn("Could not switch {0} to WAL, journal_mode is now '{1}'", db.Filename, journalMode);
			}
			// NORMAL is durable under WAL: a crash costs at most the uncommitted tail
			// of the log, never the database itself.
			SetIntegerPragma(db, "synchronous", "NORMAL", 1);
			// Checkpoint less often than the 1000-page default so a bulk import is not
			// interrupted by a full checkpoint every few batches.
			SetIntegerPragma(db, "wal_autocheckpoint", "4000", 4000);
			// Negative cache_size is measured in KiB rather than pages.
			SetIntegerPragma(db, "cache_size", "-65536", -65536);
			SetIntegerPragma(db, "temp_store", "MEMORY", 2);
		}
	}

	/// <summary>
	/// Applies and verifies a connection-scoped integer PRAGMA.
	/// </summary>
	/// <remarks>
	/// SQLiteCommand.ExecuteNonQuery returns -1 for successful statements that do not
	/// affect rows, including PRAGMA assignments in current System.Data.SQLite versions.
	/// The old code expected zero and therefore rejected a successful startup setting.
	/// Reading the value back is both provider-independent and verifies the real outcome.
	/// </remarks>
	private static void SetIntegerPragma(ISqlDb db, string name, string value, long expected)
	{
		int result = db.ExecuteNonQuery("PRAGMA " + name + " = " + value, null);
		if (result < -1)
		{
			throw new Exception("Failed to set PRAGMA " + name);
		}

		long actual = db.ExecuteScalar("PRAGMA " + name, null);
		if (actual != expected)
		{
			throw new Exception(string.Format(
				"PRAGMA {0} verification failed: expected {1}, got {2}",
				name,
				expected,
				actual));
		}
	}

	/// <summary>How often the O(n) row counts are allowed to be recomputed during an import.</summary>
	private static readonly TimeSpan ExactCountInterval = TimeSpan.FromSeconds(30.0);

	private static readonly Stopwatch ExactCountClock = Stopwatch.StartNew();

	/// <param name="forceExactCounts">
	/// Recompute the row counts even if the throttle has not elapsed. Pass this whenever
	/// the figures are about to be read as authoritative rather than displayed.
	/// </param>
	private static void UpdateDatabaseSettings(ISqlDb db, bool forceExactCounts = false)
	{
		using (ISqlDbTransaction transaction = db.BeginReadTransaction())
		{
			// MIN and MAX on the integer primary key are index lookups, so these stay
			// cheap enough to run after every batch - and DatabaseMax is the import
			// watermark, so it has to.
			Settings.Default.DatabaseMax = db.ExecuteScalar("SELECT MAX(rowid) FROM spots", transaction);
			Settings.Default.DatabaseMin = db.ExecuteScalar("SELECT MIN(rowid) FROM spots", transaction);

			// COUNT(1), by contrast, walks an entire index. This method runs in the
			// finally of every save batch, so recomputing both counts each time made the
			// cost of importing grow with the size of the database. They only feed a
			// display figure, so refresh them on a throttle instead; the count can lag by
			// up to ExactCountInterval mid-import and is exact whenever it matters.
			bool refreshCounts = forceExactCounts
				|| Settings.Default.DatabaseCount <= 0
				|| ExactCountClock.Elapsed >= ExactCountInterval;
			if (refreshCounts)
			{
				Settings.Default.DatabaseCount = db.ExecuteScalar("SELECT COUNT(1) FROM spots", transaction);
				long databaseCount = Settings.Default.DatabaseCount;
				Settings.Default.DatabaseFilter = databaseCount - Math.Abs(db.ExecuteScalar("SELECT COUNT(1) FROM spots WHERE cat=9", transaction));
				ExactCountClock.Restart();
			}
			Settings.Default.Save();
		}
		SpotSaver.OnDbSettingsUpdate?.Invoke();
	}
}
