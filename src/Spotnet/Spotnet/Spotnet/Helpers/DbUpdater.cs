using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Helpers;

internal static class DbUpdater
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object SyncRoot = new object();

	private static Timer _dbUpdateTimer;

	private static bool _isDbUpdateInProgress;

	private static CancellationTokenSource _dbUpdateCancelTokenSource;

	internal static bool IsDbUpdateInProgress
	{
		get
		{
			if (!_isDbUpdateInProgress && !SpamReports.InProgress && !Headers.InProgress)
			{
				return Comments.InProgress;
			}
			return true;
		}
	}

	internal static SaveSpotsRow LastHeaderResults { get; set; }

	internal static DateTime RetentionStartDate
	{
		get
		{
			if (AppHelper.IsSnelNlProvider && Settings.Default.Retention < 1)
			{
				return DateTime.Now.AddDays(-1800.0);
			}
			if (AppHelper.Is5EuroProvider && Settings.Default.Retention < 1)
			{
				return DateTime.Now.AddDays(-1800.0);
			}
			if (Settings.Default.Retention >= 1)
			{
				return DateTime.Now.AddDays(-Settings.Default.Retention);
			}
			return new DateTime(2010, 2, 2, 0, 0, 0);
		}
	}

	public static bool IsCancellationRequested
	{
		get
		{
			if (_dbUpdateCancelTokenSource != null)
			{
				return _dbUpdateCancelTokenSource.IsCancellationRequested;
			}
			return false;
		}
	}

	internal static event Action OnDbUpdateStart;

	internal static event Action OnDbUpdateEnd;

	internal static async Task StartTaskAsync(NntpSettings headerSettings, NntpSettings commentSettings, NntpSettings spamReportSettings, Action<string, int> reportAction, Action<SaveSpotsRow> onSpotsUpdate, Action<bool, bool> setDbUpToDateStatus)
	{
		_ = 5;
		try
		{
			lock (SyncRoot)
			{
				if (_isDbUpdateInProgress)
				{
					return;
				}
				_isDbUpdateInProgress = true;
			}
			_dbUpdateCancelTokenSource = new CancellationTokenSource();
			DbUpdater.OnDbUpdateStart?.Invoke();
			if (Headers.GetHeadersToDownload(AppHelper.HeaderPhuse, headerSettings, out var _, out var _, out var _, out var _, out var _, out var _) > 1000)
			{
				setDbUpToDateStatus(arg1: true, arg2: true);
			}
			if (_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				return;
			}
			BlockingCollection<List<SpamReport>> reports = new BlockingCollection<List<SpamReport>>(3);
			Task gettingTask3 = SpamReports.FindSpamReportHeadersAsync(reports, AppHelper.HeaderPhuse, spamReportSettings, isFirstShortTry: true, _dbUpdateCancelTokenSource.Token);
			await SpotSaver.SaveSpamReportsAsync(reports, reportAction, _dbUpdateCancelTokenSource.Token);
			if (_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				return;
			}
			if (gettingTask3.Exception != null)
			{
				throw gettingTask3.Exception;
			}
			reportAction?.Invoke(Words.SpotsUpdating, -1);
			LastHeaderResults = new SaveSpotsRow();
			BlockingCollection<List<Spot>> spotsToAddAndRemove = new BlockingCollection<List<Spot>>(3);
			gettingTask3 = Headers.FindHeadersAsync(spotsToAddAndRemove, AppHelper.HeaderPhuse, headerSettings, _dbUpdateCancelTokenSource.Token);
			long last = headerSettings.Position.Last;
			await SpotSaver.SaveHeadersAsync(spotsToAddAndRemove, LastHeaderResults, last, reportAction, onSpotsUpdate, _dbUpdateCancelTokenSource.Token);
			if (_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				return;
			}
			if (gettingTask3.Exception != null)
			{
				throw gettingTask3.Exception;
			}
			reportAction?.Invoke(Words.SpotsRemoving, -1);
			await SpotSaver.RemoveOutOfRetentionSpotsAsync(_dbUpdateCancelTokenSource.Token);
			if (_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				return;
			}
			reportAction?.Invoke(Words.SpamReportsUpdating, -1);
			reports = new BlockingCollection<List<SpamReport>>(3);
			gettingTask3 = SpamReports.FindSpamReportHeadersAsync(reports, AppHelper.HeaderPhuse, spamReportSettings, isFirstShortTry: false, _dbUpdateCancelTokenSource.Token);
			await SpotSaver.SaveSpamReportsAsync(reports, reportAction, _dbUpdateCancelTokenSource.Token);
			if (!_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				if (gettingTask3.Exception != null)
				{
					throw gettingTask3.Exception;
				}
				await UpdateComments(commentSettings, reportAction, setDbUpToDateStatus);
				if (!_dbUpdateCancelTokenSource.IsCancellationRequested)
				{
					await Headers.UpdateNullModulusSpotsAsync(AppHelper.HeaderPhuse, headerSettings, reportAction, _dbUpdateCancelTokenSource.Token);
				}
			}
		}
		catch (Exception e)
		{
			Log.Trace(e.TheMostInnerException().Message);
			if (!_dbUpdateCancelTokenSource.IsCancellationRequested)
			{
				throw;
			}
		}
		finally
		{
			_isDbUpdateInProgress = false;
			DbUpdater.OnDbUpdateEnd?.Invoke();
		}
	}

	internal static bool DbUpdateTimerStart()
	{
		if (Settings.Default.DbAutoUpdateIntervalMin <= 0 || !Settings.Default.DbAutoUpdateEnabled)
		{
			return false;
		}
		if (_dbUpdateTimer == null)
		{
			_dbUpdateTimer = new Timer(delegate
			{
				DbUpdateTimerElapsed(Sys.MainWindow.ScheduleDbUpdate);
			}, null, TimeSpan.FromMinutes(Settings.Default.DbAutoUpdateIntervalMin), TimeSpan.FromMinutes(Settings.Default.DbAutoUpdateIntervalMin));
		}
		return true;
	}

	internal static void DbUpdateTimerStop()
	{
		if (_dbUpdateTimer != null)
		{
			_dbUpdateTimer.Dispose();
			_dbUpdateTimer = null;
		}
	}

	private static void DbUpdateTimerElapsed(Action elapsedAction)
	{
		if (Settings.Default.DbAutoUpdateIntervalMin > 0 && Settings.Default.DbAutoUpdateEnabled)
		{
			elapsedAction?.Invoke();
		}
		else
		{
			DbUpdateTimerStop();
		}
	}

	internal static void RecreateDbIfAccountTypeChanged(ref NntpSettings headerSettings)
	{
		if (!Settings.Default.HeaderGroup.Equals("free.pt") || !AppHelper.IsSnelNlProvider)
		{
			return;
		}
		long first = headerSettings.Position.First;
		long last = headerSettings.Position.Last;
		if (first < 1)
		{
			return;
		}
		bool flag = IsItNewFullDeptHeaderDb(first, last);
		bool flag2 = !flag;
		Log.Debug("New dept headers db: " + flag.ToString() + ". dbFirstId: " + first + ". dbLastId: " + last);
		if (flag2)
		{
			if (!AppHelper.RecreateAllDatabases())
			{
				throw new Exception(Words.DbRecreationFailed);
			}
			headerSettings = AppHelper.HeaderSettings(bIncludePosition: true);
		}
	}

	private static bool IsItNewFullDeptHeaderDb(long dbFirstId, long dbLastId)
	{
		if (dbFirstId >= 500000)
		{
			return dbLastId < 3000000;
		}
		return true;
	}

	public static async Task UpdateComments(NntpSettings commentSettings, Action<string, int> reportAction, Action<bool, bool> setDbUpToDateStatus)
	{
		_dbUpdateCancelTokenSource = new CancellationTokenSource();
		if (!Settings.Default.LoadComments)
		{
			Log.Debug("Cancel comments loading as it's disabled in settings");
			return;
		}
		Task gettingTask = null;
		try
		{
			reportAction?.Invoke(Words.CommentsUpdating, -1);
			BlockingCollection<List<Comment>> blockingCollection = new BlockingCollection<List<Comment>>();
			gettingTask = Comments.FindCommentSpotRelationAsync(blockingCollection, AppHelper.HeaderPhuse, commentSettings, delegate
			{
				setDbUpToDateStatus(arg1: false, arg2: true);
			}, _dbUpdateCancelTokenSource.Token);
			await SpotSaver.SaveCommentSpotRelationAsync(blockingCollection, reportAction, _dbUpdateCancelTokenSource.Token);
		}
		finally
		{
			if (!_dbUpdateCancelTokenSource.IsCancellationRequested && gettingTask != null && gettingTask.Exception != null)
			{
				throw gettingTask.Exception;
			}
		}
	}

	public static void Stop()
	{
		_dbUpdateCancelTokenSource?.Cancel();
	}
}
