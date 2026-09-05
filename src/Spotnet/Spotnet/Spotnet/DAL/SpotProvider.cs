using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NLog;
using System.IO;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Model.Newznab;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.DAL;

public class SpotProvider : IVirtualListLoader<ISpotRow>
{
	public const string NoEroticaFilter = "cat<9";

	public const string NoEroticaFilterForSearch = "cats NOT LIKE '9 %'";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private int _cacheQueryCount;

	internal bool IsCacheQueryCountPrecise;

	private Dictionary<string, int> _cacheQueryCounts;

	private string _lastRowFilter;

	private string _queryName;

	private string _rowFilter;

	private readonly string _queryDefaultName;

	public long RowNew;

	private const string PosterIdentFilterString = "^PosterIdent[ ]+IN[ ]+\\(([W|B|F|T|N|,| ]+)\\)";

	private ParameterizedSql _lastCountQueryRequested;

	private readonly object _lockSlowCountCalculation = new object();

	internal bool Connected { get; private set; }

	internal string Filename { get; private set; }

	internal long QueryCount
	{
		get
		{
			if (_cacheQueryCount <= 0)
			{
				return 0L;
			}
			return _cacheQueryCount;
		}
	}

	private static StatusBarViewModel StatusBarVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).StatusBar;

	public bool Corrupted { get; private set; }

	public string QueryName
	{
		get
		{
			if (_queryName.IsNullOrEmpty() || _queryName.EqualsIgnoreCase("cat < 9"))
			{
				return _queryDefaultName;
			}
			return _queryName;
		}
		set
		{
			_queryName = value;
		}
	}

	public string SortOrder
	{
		get
		{
			return Settings.Default.SortDirection;
		}
		set
		{
			Settings.Default.SortDirection = value;
			Settings.Default.Save();
		}
	}

	public string RowFilter
	{
		get
		{
			return _rowFilter ?? "";
		}
		set
		{
			_rowFilter = value;
		}
	}

	private List<char> PosterIdentFilter
	{
		get
		{
			if (!_rowFilter.StartsWith("PosterIdent"))
			{
				return null;
			}
			List<char> result = new List<char>();
			Regex regex = new Regex("^PosterIdent[ ]+IN[ ]+\\(([W|B|F|T|N|,| ]+)\\)", RegexOptions.IgnoreCase);
			if (regex.IsMatch(_rowFilter.Trim()))
			{
				result = (from s in regex.Match(_rowFilter).Groups[1].Value.Replace(" ", "").Split(',')
					select s[0]).ToList();
			}
			return result;
		}
	}

	public bool CanSort => true;

	private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

	internal static event Action OnDbSettingsUpdate;

	public SpotProvider()
	{
		_cacheQueryCount = -1;
		_cacheQueryCounts = new Dictionary<string, int>();
		RowNew = -1L;
		Corrupted = false;
		_rowFilter = "cat < 9";
		_lastRowFilter = "";
		_queryName = _queryDefaultName;
		_queryDefaultName = Words.TabSpots;
		if (!Settings.Default.SortColumn.Trim().ToLower().Equals("rowid") || !Settings.Default.SortDirection.Trim().ToLower().Equals("desc"))
		{
			Settings.Default.SortColumn = "rowid";
			Settings.Default.SortDirection = "desc";
			Settings.Default.Save();
		}
	}

	private void UpdateQueryCount()
	{
		StatusBarVm.SetDefaultSpotsListStatusMessage();
	}

	public int GetNewCounts(string query)
	{
		if (Sys.MainWindow.SpotProvider.RowNew <= 1)
		{
			return 0;
		}
		query = Favorites.ReplaceWithFavoritesQuery(query);
		ParameterizedSql countQuery = AppHelper.IsSearchQuery(query) ? BuildSearchQueryCountNew(query) : BuildQueryCountNew(query);
		if (countQuery == null)
		{
			return 0;
		}
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
		using DbCommand command = CreateCommand(sqlDb, transaction, countQuery);
		return (int)sqlDb.ExecuteScalar(command);
	}

	public IList<ISpotRow> LoadRange(int startIndex, int count, long minRowId, out int overallCount, out bool isNewQuery, out bool isLastPage, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		int num = 0;
		isNewQuery = false;
		IList<ISpotRow> list2;
		try
		{
			if (Settings.Default.MaxResults == 0)
			{
				throw new Exception("This type of list is not supported anymore");
			}
			if (!_lastRowFilter.Equals(_rowFilter))
			{
				isNewQuery = true;
				_lastRowFilter = _rowFilter;
			}
			if (!Connected || _rowFilter.ToLower().Equals("newznab"))
			{
				overallCount = 0;
				isLastPage = true;
				return new List<ISpotRow>();
			}
			if (NewznabHelper.IsNewznabQuery(_rowFilter))
			{
				IList<ISpotRow> list = NewznabHelper.ExecuteQuery(_rowFilter, startIndex, count, out overallCount, cancellationToken);
				isLastPage = startIndex + list.Count == overallCount;
				return list;
			}
			cancellationToken.ThrowIfCancellationRequested();
			bool flag = false;
			num = 1;
			if (_cacheQueryCount < 1 && minRowId < 0)
			{
				_cacheQueryCount = -1;
				if (_rowFilter.IsNullOrEmpty() || _rowFilter.Replace(" ", "").EqualsIgnoreCase("cat<9") || _rowFilter.Replace(" ", "").EqualsIgnoreCase("cat!=0"))
				{
					_cacheQueryCount = checked((int)Settings.Default.DatabaseFilter);
					flag = true;
				}
			}
			num = 3;
			string filter = Favorites.ReplaceWithFavoritesQuery(_rowFilter);
			ParameterizedSql query;
			ParameterizedSql countQuery;
			if (AppHelper.IsSearchQuery(filter))
			{
				query = BuildSearchQuery(filter, startIndex, count, minRowId, out countQuery);
			}
			else
			{
				query = BuildQuery(filter, startIndex, count, minRowId, out countQuery);
			}
			num = 4;
			cancellationToken.ThrowIfCancellationRequested();
			num = 5;
			list2 = ReadRows(query, cancellationToken, out var countBeforeFilter);
			num = 6;
			if (flag)
			{
				_cacheQueryCounts[countQuery.CacheKey] = _cacheQueryCount;
			}
			isLastPage = UpdateQueryCount(countBeforeFilter, startIndex, count, countQuery);
			overallCount = startIndex + list2.Count;
			if (overallCount > _cacheQueryCount)
			{
				overallCount = _cacheQueryCount;
			}
		}
		catch (Exception ex)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Log.Exception(ex);
			string text2 = "LoadRange #" + num + " query: " + _rowFilter + ". Error: " + ex.Message;
			Log.Debug(text2);
			AppHelper.Error(text2);
			if (ex is SQLiteException { ResultCode: var resultCode } && (resultCode == SQLiteErrorCode.Corrupt || resultCode == SQLiteErrorCode.NotADb))
			{
				Corrupted = true;
			}
			ResetCache();
			overallCount = 0;
			isLastPage = true;
			list2 = null;
		}
		return list2;
	}

	private bool UpdateQueryCount(int countBeforeFilter, int startIndex, int count, ParameterizedSql countQuery)
	{
		IsCacheQueryCountPrecise = true;
		if (_cacheQueryCount > startIndex + count)
		{
			return false;
		}
		if (_cacheQueryCounts.ContainsKey(countQuery.CacheKey))
		{
			_cacheQueryCount = _cacheQueryCounts[countQuery.CacheKey];
			return _cacheQueryCount <= startIndex + count;
		}
		IsCacheQueryCountPrecise = false;
		_cacheQueryCount = ((countBeforeFilter > 0) ? (startIndex + count + 1) : (startIndex + countBeforeFilter));
		ScheduleSlowCountCalculationAsync(countQuery);
		return countBeforeFilter == 0;
	}

	private void ScheduleSlowCountCalculationAsync(ParameterizedSql countQuery)
	{
		Task.Run(delegate
		{
			_lastCountQueryRequested = countQuery;
			if (!Monitor.TryEnter(_lockSlowCountCalculation))
			{
				return;
			}
			try
			{
				using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
				using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
				while (true)
				{
					using (DbCommand command = CreateCommand(sqlDb, transaction, countQuery))
					{
						_cacheQueryCount = (int)sqlDb.ExecuteScalar(command);
					}
					if (_cacheQueryCount >= 0 && !Favorites.IsFavoritesQuery(countQuery.CommandText))
					{
						_cacheQueryCounts[countQuery.CacheKey] = _cacheQueryCount;
					}
					if (_lastCountQueryRequested.CacheKey.Equals(countQuery.CacheKey, StringComparison.Ordinal))
					{
						break;
					}
					countQuery = _lastCountQueryRequested;
				}
				IsCacheQueryCountPrecise = true;
				UpdateQueryCount();
			}
			finally
			{
				Monitor.Exit(_lockSlowCountCalculation);
			}
		});
	}

	internal bool OpenDb()
	{
		try
		{
			Connected = false;
			string queryName = QueryName;
			string rowFilter = RowFilter;
			if (Connect())
			{
				QueryName = queryName;
				RowFilter = rowFilter;
				Connected = true;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return Connected;
	}

	internal string CreateQuery(string filter, int startIndex, int countRequested, long minRowId, out string countQuery)
	{
		ParameterizedSql query = BuildQuery(filter, startIndex, countRequested, minRowId, out ParameterizedSql count);
		countQuery = count.CommandText;
		return query.CommandText;
	}

	private ParameterizedSql BuildQuery(string filter, int startIndex, int countRequested, long minRowId, out ParameterizedSql countQuery)
	{
		string text = GetSortColumn();
		string sortOrder = GetSortOrder();
		string text2 = "";
		string text3 = "";
		IReadOnlyList<SqlValue> values = Array.Empty<SqlValue>();
		if (filter != null && filter.StartsWith("PosterIdent"))
		{
			Regex regex = new Regex("^PosterIdent[ ]+IN[ ]+\\(([W|B|F|T|N|,| ]+)\\)", RegexOptions.IgnoreCase);
			filter = filter.Substring(regex.Match(filter.Trim()).Groups[0].Value.Length).Trim();
			filter = (filter.ToUpper().StartsWith("AND ") ? filter.Substring(4) : "cat<10");
		}
		if (!filter.IsNullOrWhiteSpace())
		{
			string text4 = filter.Replace(" ", "").ToLower();
			string text5 = ((Settings.Default.ShowEroticaInSearchResults || text4.Contains("cat=") || text4.Contains("cat<")) ? "" : "cat<9 AND ");
			string text6 = ((minRowId >= 0) ? "rowid>={0} AND ".Format(minRowId) : "");
			filter = ResolveFilterMarkers(filter);
			ParameterizedSql compiled = FilterExpressionCompiler.Compile(filter);
			values = compiled.Values;
			text2 = " WHERE " + text6 + "(" + text5 + compiled.CommandText + " AND key != 2 AND key != 5)";
			if (filter.Replace(" ", "").ToLower().Contains("date>"))
			{
				text3 = " INDEXED BY dateidx ";
			}
		}
		string text7 = " ORDER BY " + text + " " + sortOrder + " ";
		string result;
		if (text.Equals("date", StringComparison.OrdinalIgnoreCase) && sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase))
		{
			result = string.Format("SELECT rowid,{0} FROM spots {1} WHERE rowid IN (SELECT rowid FROM spots {6}{1} {2} ORDER BY rowid DESC LIMIT {4} OFFSET {5}){3}", "subcat,extcat,date,filesize,subject,sender,tag,modulus,spots.msgid,IFNULL(cnt,0),cat,cats", "LEFT JOIN spamgroup s USING (msgid)", text2, text7, countRequested, startIndex, text3);
		}
		else
		{
			if (!text.ToUpper().Equals("DATE"))
			{
				text7 += ", date DESC ";
			}
			result = string.Format("SELECT rowid,{0} FROM spots {1}{2}{3} LIMIT {4} OFFSET {5}", "subcat,extcat,date,filesize,subject,sender,tag,modulus,spots.msgid,IFNULL(cnt,0),cat,cats", "LEFT JOIN spamgroup s USING (msgid)", text2, text7, countRequested, startIndex);
		}
		countQuery = new ParameterizedSql("SELECT COUNT(1) FROM spots LEFT JOIN spamgroup s USING (msgid)" + text2, values);
		return new ParameterizedSql(result, values);
	}

	internal string CreateSearchQuery(string filter, int startIndex, int countRequested, long minRowId, out string countQuery)
	{
		ParameterizedSql query = BuildSearchQuery(filter, startIndex, countRequested, minRowId, out ParameterizedSql count);
		countQuery = count.CommandText;
		return query.CommandText;
	}

	private ParameterizedSql BuildSearchQuery(string filter, int startIndex, int countRequested, long minRowId, out ParameterizedSql countQuery)
	{
		string text = GetSortColumn();
		string sortOrder = GetSortOrder();
		string text2 = "";
		IReadOnlyList<SqlValue> values = Array.Empty<SqlValue>();
		if (!filter.IsNullOrWhiteSpace())
		{
			string text3 = ((minRowId >= 0) ? "rowid>={0} AND ".Format(minRowId) : "");
			string text4 = ((Settings.Default.ShowEroticaInSearchResults || filter.ToLower().Contains("cats match ")) ? "" : "cats NOT LIKE '9 %' AND ");
			filter = ResolveFilterMarkers(filter);
			ParameterizedSql compiled = FilterExpressionCompiler.Compile(filter);
			values = compiled.Values;
			// This guard is application-owned SQL, not user input.
			text2 = " WHERE " + text3 + "(" + text4 + compiled.CommandText + ")";
		}
		string text5 = " ORDER BY " + text + " " + sortOrder + " ";
		if (!text.ToUpper().Equals("DATE"))
		{
			text5 += ", date DESC ";
		}
		string text6 = "SELECT rowid,subcat,extcat,date,filesize,subject,sender,tag,modulus,spots.msgid,IFNULL(cnt,0),cat,cats FROM spots LEFT JOIN spamgroup s USING (msgid) WHERE rowid IN ";
		string text7 = $" LIMIT {countRequested} OFFSET {startIndex}";
		string text8 = "";
		if (text.Equals("date", StringComparison.OrdinalIgnoreCase) && sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase))
		{
			text8 = " ORDER BY rowid DESC " + text7 + " ";
			text7 = "";
		}
		string result = text6 + " (SELECT rowid FROM search" + text2 + text8 + ") AND key != 2 AND key != 5" + text5 + text7;
		countQuery = new ParameterizedSql("SELECT COUNT(1) FROM search" + text2, values);
		return new ParameterizedSql(result, values);
	}

	internal string CreateQueryCountNew(string filter)
	{
		return BuildQueryCountNew(filter)?.CommandText;
	}

	private ParameterizedSql BuildQueryCountNew(string filter)
	{
		if (filter.IsNullOrWhiteSpace())
		{
			return null;
		}
		if (filter.StartsWith("PosterIdent"))
		{
			Regex regex = new Regex("^PosterIdent[ ]+IN[ ]+\\(([W|B|F|T|N|,| ]+)\\)", RegexOptions.IgnoreCase);
			filter = filter.Substring(regex.Match(filter.Trim()).Groups[0].Value.Length).Trim();
			filter = (filter.ToUpper().StartsWith("AND ") ? filter.Substring(4) : "cat<10");
		}
		string text = filter.Replace(" ", "").ToLower();
		string text2 = ((Settings.Default.ShowEroticaInSearchResults || text.Contains("cat=") || text.Contains("cat<")) ? "" : "cat<9 AND ");
		ParameterizedSql compiled = FilterExpressionCompiler.Compile(ResolveFilterMarkers(filter));
		return new ParameterizedSql(string.Format("SELECT COUNT(1) FROM spots WHERE rowid>{0} AND ({1}{2}{3})", RowNew, text2, compiled.CommandText, " AND key != 2 AND key != 5"), compiled.Values);
	}

	internal string CreateSearchQueryCountNew(string filter)
	{
		return BuildSearchQueryCountNew(filter)?.CommandText;
	}

	private ParameterizedSql BuildSearchQueryCountNew(string filter)
	{
		if (filter.IsNullOrWhiteSpace())
		{
			return null;
		}
		string arg = ((Settings.Default.ShowEroticaInSearchResults || filter.ToLower().Contains("cats match ")) ? "" : "cats NOT LIKE '9 %' AND ");
		ParameterizedSql compiled = FilterExpressionCompiler.Compile(ResolveFilterMarkers(filter));
		return new ParameterizedSql($"SELECT COUNT(1) FROM search WHERE rowid>{RowNew} AND ({arg}{compiled.CommandText})", compiled.Values);
	}

	private string ResolveFilterMarkers(string filter)
	{
		filter = filter.Replace("[SN:DATE]", FDate().ToStringSafely());
		return RowNew <= 1
			? filter.Replace("[SN:NEW]", Convert.ToString(Settings.Default.DatabaseMax + 1))
			: filter.Replace("[SN:NEW]", Convert.ToString(RowNew));
	}

	private static string GetSortColumn()
	{
		string column = Settings.Default.SortColumn.Trim().ToLowerInvariant();
		if (column == "rowid")
		{
			return "date";
		}
		string[] allowed = { "date", "subject", "sender", "tag", "filesize", "cat", "subcat", "extcat" };
		return allowed.Contains(column) ? column : "date";
	}

	private static string GetSortOrder()
	{
		return Settings.Default.SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
	}

	private void CreateSpotsTablesOnEmptyDatabase(ISqlDb db)
	{
		if (db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'", null) != 0)
			throw new Exception("Refusing to initialize a database that already contains tables");
		// page_size must be set before the first table is written, and it can no longer
		// be changed once the database is in WAL mode, so it has to come first here.
		// 8192 suits the row sizes in `spots` better than the old 4096.
		// SQliteDb opens writable connections in WAL. On this empty database only,
		// leave WAL temporarily so page_size can actually take effect before DDL.
		if (!"delete".Equals(db.ExecuteCommand("PRAGMA journal_mode = DELETE", null)?.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			throw new Exception("Could not initialize the empty database journal mode");
		}
		// ADO.NET uses -1 for successful statements without a row count. -2 is
		// the DAL's error sentinel; verify the settings instead of requiring zero.
		if (new string[3] { "PRAGMA page_size = " + SpotsSchema.SpotsPageSize, "PRAGMA locking_mode = NORMAL", "PRAGMA user_version = 0" }.Any((string command) => db.ExecuteNonQuery(command, null) < -1))
		{
			throw new Exception("Pragma exception");
		}
		// The provider may already have written an empty 4096-byte-page header while
		// opening WAL. VACUUM applies the requested size, but ONLY on this verified-empty
		// database; existing user databases never take this path.
		if (db.ExecuteScalar("PRAGMA page_size", null) != SpotsSchema.SpotsPageSize &&
			db.ExecuteNonQuery("VACUUM", null) < -1)
			throw new Exception("Could not set the empty database page size");
		if (db.ExecuteScalar("PRAGMA page_size", null) != SpotsSchema.SpotsPageSize ||
			db.ExecuteScalar("PRAGMA user_version", null) != 0 ||
			!"normal".Equals(db.ExecuteCommand("PRAGMA locking_mode", null)?.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			throw new Exception("Empty database settings verification failed");
		}
		// Create in WAL so the database is crash-safe from its very first write, rather
		// than inheriting the rollback journal until the first import switches it over.
		string journalMode = db.ExecuteCommand("PRAGMA journal_mode = WAL", null)?.Trim();
		if (!"wal".Equals(journalMode, StringComparison.OrdinalIgnoreCase))
		{
			Log.Warn("New database {0} could not be created in WAL, journal_mode is '{1}'", db.Filename, journalMode);
		}
		using ISqlDbTransaction sqlDbTransaction = db.BeginWriteTransaction();
		// Statements live in SpotsSchema so that a database rebuilt by the recovery window
		// cannot end up with a different shape from one created here.
		foreach (string statement in SpotsSchema.Tables)
		{
			if (db.ExecuteNonQuery(statement, sqlDbTransaction) < -1)
			{
				throw new Exception(statement);
			}
		}
		if (db.ExecuteNonQuery("PRAGMA user_version = " + SpotsSchema.CurrentUserVersion, sqlDbTransaction) < -1 ||
			db.ExecuteScalar("PRAGMA user_version", sqlDbTransaction) != SpotsSchema.CurrentUserVersion)
		{
			throw new Exception("PRAGMA user_version");
		}
		sqlDbTransaction.Commit();
	}

	/// <summary>
	/// Creates the core schema when SQLite has just opened a new file, and repairs the
	/// small partial schema left behind by Spotnet 3.0.4's first-run initialization bug.
	/// </summary>
	/// <remarks>
	/// Opening a writable SQLite connection creates a non-empty database header. The old
	/// startup code checked the file length after opening the connection, so it skipped
	/// schema creation and subsequently created only the two upgrade tables. Restrict the
	/// repair path to exactly those known tables so an unrelated or damaged database is
	/// never overwritten silently.
	/// </remarks>
	private bool EnsureSpotsSchema(ISqlDb db)
	{
		if (db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'spots'", null) == 1)
		{
			return false;
		}

		long tableCount = db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'", null);
		if (tableCount == 0)
		{
			CreateSpotsTablesOnEmptyDatabase(db);
			return true;
		}

		string[] existingTables = db.ExecuteCommand(
			"SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name", null)
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		string[] recoverableTables = { "spamgroup", "spamreports" };
		if (existingTables.Length == 0 || existingTables.Any(name => !recoverableTables.Contains(name, StringComparer.OrdinalIgnoreCase)))
		{
			throw new Exception("The spots database is missing its core schema and cannot be repaired safely");
		}

		Log.Warn("Repairing incomplete first-run spots database {0}", db.Filename);
		using ISqlDbTransaction transaction = db.BeginWriteTransaction(exclusive: true);
		foreach (string statement in SpotsSchema.Tables)
		{
			if (db.ExecuteNonQuery(statement, transaction) < -1)
			{
				throw new Exception("Could not repair the incomplete spots database: " + statement);
			}
		}
		// The repair writes the current schema, so the version has to say so. Left at the
		// value the half-finished database carried, DatabaseUpgrade would run its
		// migrations over tables that were just created in their final shape.
		if (db.ExecuteNonQuery("PRAGMA user_version = " + SpotsSchema.CurrentUserVersion, transaction) < -1 ||
			db.ExecuteScalar("PRAGMA user_version", transaction) != SpotsSchema.CurrentUserVersion)
		{
			throw new Exception("Could not record the schema version on the repaired database");
		}
		transaction.Commit();

		if (db.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'spots'", null) != 1)
		{
			throw new Exception("The incomplete spots database repair did not create the spots table");
		}
		return true;
	}

	private long FDate()
	{
		return DateTime.UtcNow.ToUnixTime();
	}

	private static string GetString(object obj)
	{
		return (obj as string).ToStringSafely();
	}

	private static DbCommand CreateCommand(ISqlDb db, ISqlDbTransaction transaction, ParameterizedSql query)
	{
		DbCommand command = db.CreateCommand(transaction);
		command.CommandText = query.CommandText;
		foreach (SqlValue value in query.Values)
		{
			DbParameter parameter = command.CreateParameter();
			parameter.ParameterName = value.Name;
			parameter.Value = value.Value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}
		return command;
	}

	private List<ISpotRow> ReadRows(ParameterizedSql query, CancellationToken cancellationToken, out int countBeforeFilter)
	{
		List<ISpotRow> list = new List<ISpotRow>();
		using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots())
		{
			using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
			using DbCommand dbCommand = CreateCommand(sqlDb, transaction, query);
			using DbDataReader dbDataReader = dbCommand.ExecuteReader();
			while (dbDataReader.Read())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					string @string = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[5]));
					string string2 = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[8]));
					int @int = dbDataReader.GetInt32(1);
					int int2 = dbDataReader.GetInt32(2);
					int int3 = dbDataReader.GetInt32(11);
					SpotRowChild spot = default(SpotRowChild);
					spot.ID = dbDataReader.GetInt64(0);
					spot.SubCat = @int;
					spot.ExtCat = int2;
					spot.Stamp = dbDataReader.GetInt32(3);
					spot.Filesize = dbDataReader.GetInt64(4);
					spot.Title = @string;
					spot.Poster = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[6]));
					spot.Tag = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[7]));
					spot.Modulus = string2;
					spot.MessageId = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[9]));
					spot.NumberOfSpamReports = dbDataReader.GetInt32(10);
					spot.Cat = int3;
					spot.Cats = GetString(RuntimeHelpers.GetObjectValue(dbDataReader[12]));
					SpotRowViewModel item = SpotRowViewModel.InitializeNewSpotRow(spot);
					list.Add(item);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					throw new Exception("Reader Error: " + ex.Message);
				}
			}
		}
		countBeforeFilter = list.Count;
		List<char> posterIdentFilter = PosterIdentFilter;
		list.RemoveAll(delegate(ISpotRow row)
		{
			bool num = !ShouldRowBeVisible(row, posterIdentFilter);
			if (num)
			{
				row.Dispose();
			}
			return num;
		});
		return list;
	}

	private bool ShouldRowBeVisible(ISpotRow row, List<char> posterIdentFilter)
	{
		if (!row.Modulus.IsNullOrEmpty() && row.Modulus.Equals(UserKeyHelper.GetModulus()))
		{
			return true;
		}
		if (posterIdentFilter != null && posterIdentFilter.Any())
		{
			PosterIdentType posterIdent = row.PosterIdent;
			using (List<char>.Enumerator enumerator = posterIdentFilter.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					switch (char.ToUpper(enumerator.Current))
					{
					case 'B':
						if (posterIdent == PosterIdentType.Black)
						{
							return true;
						}
						break;
					case 'W':
						if (posterIdent == PosterIdentType.White)
						{
							return true;
						}
						break;
					case 'T':
						if (posterIdent == PosterIdentType.Verified)
						{
							return true;
						}
						break;
					case 'F':
						if (posterIdent == PosterIdentType.Fake)
						{
							return true;
						}
						break;
					case 'N':
						if (posterIdent == PosterIdentType.None)
						{
							return true;
						}
						break;
					}
				}
			}
			return false;
		}
		if (Settings.Default.NumOfSpamReportsToSpotHide > 0 && row.NumberOfSpamReports >= Settings.Default.NumOfSpamReportsToSpotHide)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(row.Titel))
		{
			return false;
		}
		if (MainWindowVm.ShowTrustedOnlyMode && row.PosterIdent != PosterIdentType.White && row.PosterIdent != PosterIdentType.SpotWhite && row.PosterIdent != PosterIdentType.Verified)
		{
			return false;
		}
		if (Settings.Default.HideBlacklistedSpots && (row.PosterIdent == PosterIdentType.Black || row.PosterIdent == PosterIdentType.SpotBlack))
		{
			return false;
		}
		return true;
	}

	internal void ResetCache()
	{
		ResetCount();
		_cacheQueryCounts = new Dictionary<string, int>();
	}

	internal string GetMessageId(long id)
	{
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
		using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
		DbCommand dbCommand = sqlDb.CreateCommand(transaction);
		// Parameterized so SQLite can reuse the prepared statement across calls; this
		// runs once per row as the grid materializes.
		dbCommand.CommandText = "SELECT msgid FROM spots WHERE rowid = ?";
		DbParameter idParameter = dbCommand.CreateParameter();
		idParameter.Value = id;
		dbCommand.Parameters.Add(idParameter);
		return sqlDb.ExecuteCommand(dbCommand).Replace("\r\n", "").Trim();
	}

	public void ResetCount()
	{
		_cacheQueryCount = -1;
	}

	public bool Connect()
	{
		bool flag = false;
		Corrupted = false;
		try
		{
			using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
			if (!Filename.IsNullOrEmpty() && !Filename.EqualsIgnoreCase(sqlDb.Filename))
			{
				flag = true;
			}
			Filename = sqlDb.Filename;
			if (EnsureSpotsSchema(sqlDb))
			{
				flag = true;
				if (new FileInfo(Filename).Length < 1)
				{
					throw new Exception("db creation failed");
				}
			}
			DatabaseUpgrade(sqlDb);
			using (ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction(exclusive: true))
			{
				foreach (string statement in SpotsSchema.Indexes)
				{
					if (sqlDb.ExecuteNonQuery(statement, sqlDbTransaction) < -1)
					{
						throw new Exception("Could not create database index: " + statement);
					}
				}
				foreach (string statement in SpotsSchema.SearchTriggers)
				{
					if (sqlDb.ExecuteNonQuery(statement, sqlDbTransaction) < -1)
					{
						throw new Exception("Could not create database trigger: " + statement);
					}
				}
				sqlDbTransaction.Commit();
			}
			if (Settings.Default.DatabaseMax < 1 || Settings.Default.DatabaseMin < 1 || Settings.Default.DatabaseCount < 1 || Settings.Default.DatabaseFilter < 1)
			{
				flag = true;
			}
			using (ISqlDbTransaction transaction = sqlDb.BeginReadTransaction())
			{
				long num = sqlDb.ExecuteScalar("SELECT MAX(rowid) FROM spots", transaction);
				if (num < 1 || num != Settings.Default.DatabaseMax)
				{
					flag = true;
				}
				long num2 = sqlDb.ExecuteScalar("SELECT MIN(rowid) FROM spots", transaction);
				if (num2 < 1 || num2 != Settings.Default.DatabaseMin)
				{
					flag = true;
				}
				if (flag)
				{
					Settings.Default.DatabaseMax = num;
					Settings.Default.DatabaseMin = num2;
					long databaseCount = sqlDb.ExecuteScalar("SELECT COUNT(1) FROM spots", transaction);
					Settings.Default.DatabaseCount = databaseCount;
					long databaseCount2 = Settings.Default.DatabaseCount;
					long num3 = Math.Abs(sqlDb.ExecuteScalar("SELECT COUNT(1) FROM spots WHERE cat=9", transaction));
					long databaseFilter = databaseCount2 - num3;
					Settings.Default.DatabaseFilter = databaseFilter;
					Settings.Default.Save();
					SpotProvider.OnDbSettingsUpdate?.Invoke();
				}
			}
			return true;
		}
		catch (AccessViolationException ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		catch (Exception ex2)
		{
			Log.Exception(ex2);
			if (ex2 is SQLiteException { ResultCode: var resultCode } && (resultCode == SQLiteErrorCode.Corrupt || resultCode == SQLiteErrorCode.NotADb))
			{
				Corrupted = true;
			}
			return false;
		}
	}

	/// <summary>
	/// True while a schema migration is rewriting the database.
	/// </summary>
	/// <remarks>
	/// Startup gives the database twenty seconds to open before it offers the recovery
	/// window. That limit is there to catch a locked or hung file, and a migration that
	/// rebuilds the whole search index is neither - on a large database it can legitimately
	/// run for minutes. The startup path waits while this is set.
	/// </remarks>
	internal static volatile bool SchemaUpgradeInProgress;

	/// <summary>Reports a long-running migration, so startup can say what it is doing.</summary>
	internal static Action<string, string> OnSchemaUpgradeMessage;

	private int DatabaseUpgrade(ISqlDb db)
	{
		long num = db.ExecuteScalar("PRAGMA user_version", null);
		int num2 = 1;
		if (num < num2)
		{
			Log.Debug("Upgrade DB to version " + num2);
			using (ISqlDbTransaction sqlDbTransaction = db.BeginWriteTransaction())
			{
				if (db.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS spamreports(rowid INTEGER PRIMARY KEY, msgid TEXT, modulus TEXT, date INT)", sqlDbTransaction) != 0)
				{
					throw new Exception("CREATE TABLE spamreports");
				}
				if (db.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS spamgroup(msgid TEXT PRIMARY KEY NOT NULL, cnt INT DEFAULT 0)", sqlDbTransaction) != 0)
				{
					throw new Exception("CREATE TABLE spamgroup");
				}
				if (db.ExecuteNonQuery("PRAGMA user_version = " + num2, sqlDbTransaction) < -1 ||
					db.ExecuteScalar("PRAGMA user_version", sqlDbTransaction) != num2)
				{
					throw new Exception("PRAGMA user_version");
				}
				sqlDbTransaction.Commit();
			}
			Log.Debug("DB upgraded to " + num2);
		}
		num2 = 2;
		if (num < num2)
		{
			Log.Debug("Upgrade DB to version " + num2);
			using (ISqlDbTransaction sqlDbTransaction2 = db.BeginWriteTransaction())
			{
				if (db.ExecuteNonQuery("ALTER TABLE spamreports ADD COLUMN reportmsgid TEXT", sqlDbTransaction2) != 0)
				{
					throw new Exception("ADD COLUMN reportmsgid");
				}
				if (db.ExecuteNonQuery("ALTER TABLE spamreports ADD COLUMN sender TEXT", sqlDbTransaction2) != 0)
				{
					throw new Exception("ADD COLUMN sender");
				}
				if (db.ExecuteNonQuery("PRAGMA user_version = " + num2, sqlDbTransaction2) < -1 ||
					db.ExecuteScalar("PRAGMA user_version", sqlDbTransaction2) != num2)
				{
					throw new Exception("PRAGMA user_version");
				}
				sqlDbTransaction2.Commit();
			}
			Log.Debug("DB upgraded to " + num2);
		}
		num2 = 3;
		if (num < num2)
		{
			Log.Info("Rebuilding the spots full-text index as FTS5");
			// Every row of `spots` is read back and reindexed, so on a large database this
			// is the one startup step a user will actually notice.
			SchemaUpgradeInProgress = true;
			OnSchemaUpgradeMessage?.Invoke(
				"Zoekindex eenmalig herbouwen. Dit kan enkele minuten duren...",
				"Rebuilding the search index, once. This can take a few minutes...");
			try
			{
				using ISqlDbTransaction transaction = db.BeginWriteTransaction(exclusive: true);
				foreach (string trigger in new[] { "search_bu", "search_bd", "search_au", "search_ai" })
				{
					if (db.ExecuteNonQuery("DROP TRIGGER IF EXISTS " + trigger, transaction) < -1)
					{
						throw new Exception("DROP TRIGGER " + trigger);
					}
				}
				if (db.ExecuteNonQuery("DROP TABLE IF EXISTS search", transaction) < -1 ||
					db.ExecuteNonQuery(SpotsSchema.CreateSearch, transaction) < -1 ||
					db.ExecuteNonQuery(SpotsSchema.RebuildSearchIndex, transaction) < -1 ||
					db.ExecuteNonQuery("PRAGMA user_version = " + num2, transaction) < -1 ||
					db.ExecuteScalar("PRAGMA user_version", transaction) != num2)
				{
					throw new Exception("Could not migrate the spots search index to FTS5");
				}
				transaction.Commit();
				Log.Info("Spots full-text index migrated to FTS5");
			}
			finally
			{
				SchemaUpgradeInProgress = false;
			}
		}
		return num2;
	}

	public IdPosition GetIdPosition(string sTable)
	{
		using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
		return AppHelper.GetIdPosition(db, sTable);
	}

	public int GetTheNumberOfSpamReports(string messageId)
	{
		if (messageId.IsNullOrEmpty())
		{
			return 0;
		}
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
		int num;
		using (ISqlDbTransaction transaction = sqlDb.BeginReadTransaction())
		{
			// messageId arrives from the network, so it must never be concatenated into
			// SQL. A message id containing a quote used to break the query outright, and
			// a crafted one could append clauses to it.
			DbCommand dbCommand = sqlDb.CreateCommand(transaction);
			dbCommand.CommandText = "SELECT cnt FROM spamgroup WHERE msgid = ?";
			DbParameter msgIdParameter = dbCommand.CreateParameter();
			msgIdParameter.Value = SpotHelper.MakeMsg(messageId, tag: false);
			dbCommand.Parameters.Add(msgIdParameter);
			num = (int)sqlDb.ExecuteScalar(dbCommand);
		}
		return (num >= 0) ? num : 0;
	}

	public void ClearDbFilesIfMalformed()
	{
		if (Settings.Default.SpotsDbFileMalformed || Settings.Default.RecreateDbScheduled)
		{
			AppHelper.ClearSpotsDb();
			Settings.Default.SpotsDbFileMalformed = false;
		}
		if (Settings.Default.CommentsDbFileMalformed || Settings.Default.RecreateDbScheduled)
		{
			AppHelper.ClearCommentsDb();
			Settings.Default.CommentsDbFileMalformed = false;
		}
		Settings.Default.RecreateDbScheduled = false;
		Settings.Default.Save();
	}
}
