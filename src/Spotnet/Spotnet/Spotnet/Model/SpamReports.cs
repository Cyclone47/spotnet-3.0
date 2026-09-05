using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration.Provider;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse;
using Spotnet.Properties;

namespace Spotnet.Model;

internal static class SpamReports
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static long _workTotal;

	private static CancellationToken _cancelToken;

	private static readonly object SyncRoot = new object();

	private static BlockingCollection<List<SpamReport>> _workXmls;

	private static readonly AverageSpeedCalculator SpeedCalculator = new AverageSpeedCalculator();

	private static bool _isFirstShortTry;

	private static readonly IMinimumId MinId = new MinimumId("reports");

	internal static bool InProgress { get; private set; }

	internal static int ProgressValue { get; private set; }

	internal static bool IsAnyReportsReceived { get; private set; }

	internal static string DownloadSpeedString
	{
		get
		{
			string lastSpeedString = SpeedCalculator.GetLastSpeedString();
			if (!lastSpeedString.IsNullOrEmpty())
			{
				return "  (" + lastSpeedString + ")";
			}
			return "";
		}
	}

	internal static Task FindSpamReportHeadersAsync(BlockingCollection<List<SpamReport>> reports, Engine tPhuse, NntpSettings xParam, bool isFirstShortTry, CancellationToken cToken)
	{
		lock (SyncRoot)
		{
			if (InProgress)
			{
				throw new Exception("Task is already running");
			}
			InProgress = true;
		}
		_cancelToken = cToken;
		_workXmls = reports;
		_isFirstShortTry = isFirstShortTry;
		return Task.Factory.StartNew(delegate
		{
			FindSpamReports(tPhuse, xParam);
		}, _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task t)
		{
			_workXmls.CompleteAdding();
			InProgress = false;
			IsAnyReportsReceived = false;
			if (t.IsCanceled || t.Exception == null)
			{
				return;
			}
			throw t.Exception;
		});
	}

	private static void FindSpamReports(NNTP nntp, NntpSettings nntpSettings, long beginFirstId, long beginLastId, long endFirstId, long endLastId)
	{
		List<NNTPWork> second = SpotHelper.CreateWork(beginFirstId, beginLastId, Settings.Default.SpamReportChunkSize);
		List<NNTPWork> list = SpotHelper.CreateWork(endFirstId, endLastId, Settings.Default.SpamReportChunkSize);
		list.Reverse();
		List<NNTPWork> list2 = list.Concat(second).ToList();
		if (!list2.Any())
		{
			return;
		}
		_workTotal = list2.Count;
		Stopwatch stopwatch = new Stopwatch();
		for (int i = 0; i < list2.Count; i++)
		{
			stopwatch.Restart();
			if (_cancelToken.IsCancellationRequested)
			{
				return;
			}
			NNTPWork nNTPWork = list2[i];
			int num = 1;
			string headers;
			string errorMsg;
			do
			{
				if (_cancelToken.IsCancellationRequested)
				{
					return;
				}
				if (num > 1)
				{
					Thread.Sleep(5000);
					if (_cancelToken.IsCancellationRequested)
					{
						return;
					}
					Log.Debug("Try number {0} to the request: XOVER {1}-{2}. Group {3}", num, nNTPWork.xStart, nNTPWork.xEnd, nntpSettings.GroupName);
				}
				headers = nntp.GetHeaders(nntpSettings.GroupName, nNTPWork.xStart, nNTPWork.xEnd, SpeedCalculator.AddNewValue, out var _, out errorMsg);
			}
			while (!errorMsg.IsNullOrEmpty() && num++ < 2);
			if (_cancelToken.IsCancellationRequested)
			{
				return;
			}
			if (headers.IsNullOrEmpty())
			{
				throw new Exception(Words.ErrorOnRetrievingHeaders + ": " + errorMsg);
			}
			List<SpamReport> list3 = ParseSpamReports(headers);
			if (_cancelToken.IsCancellationRequested)
			{
				return;
			}
			if (list3.Any())
			{
				if (_workXmls.IsAddingCompleted)
				{
					throw new Exception("Error on save spamreports to db");
				}
				int num2 = list3.RemoveAll((SpamReport r) => r.Date < DbUpdater.RetentionStartDate);
				try
				{
					if (list3.Any())
					{
						_workXmls.Add(list3);
						List<SpamReport> source = list3.Where((SpamReport e) => e.ReportId > 0).ToList();
						if (source.Any())
						{
							long minId = source.Min((SpamReport e) => e.ReportId);
							MinId.UpdateIfRequired(minId);
						}
					}
				}
				catch (Exception)
				{
					_cancelToken.ThrowIfCancellationRequested();
					throw;
				}
				ProgressValue = (int)Math.Round(100.0 / (double)_workTotal * (double)(i + 1));
				IsAnyReportsReceived = true;
				if (num2 > 50)
				{
					break;
				}
			}
			if (_isFirstShortTry)
			{
				break;
			}
		}
		if (!_isFirstShortTry || list2.Count <= 1)
		{
			MinId.IsActive = true;
		}
	}

	internal static List<SpamReport> GetSpamReports(string spotMessageId, CancellationToken cancelToken)
	{
		List<SpamReport> list = new List<SpamReport>();
		if (cancelToken.IsCancellationRequested)
		{
			return list;
		}
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction transaction = sqlDb.BeginWriteTransaction();
		using DbCommand dbCommand = sqlDb.CreateCommand(transaction);
		dbCommand.CommandText = "SELECT rowid,msgid,modulus,date,reportmsgid,sender FROM spamreports WHERE msgid=?";
		DbParameter dbParameter = dbCommand.CreateParameter();
		dbCommand.Parameters.Add(dbParameter);
		dbParameter.Value = spotMessageId;
		using DbDataReader dbDataReader = dbCommand.ExecuteReader();
		while (dbDataReader.Read())
		{
			if (cancelToken.IsCancellationRequested)
			{
				return list;
			}
			int @int = dbDataReader.GetInt32(0);
			string @string = dbDataReader.GetString(1);
			string string2 = dbDataReader.GetString(2);
			int int2 = dbDataReader.GetInt32(3);
			string bodyMessageId;
			string username;
			if (dbDataReader.GetValue(4) is DBNull)
			{
				bodyMessageId = "";
				username = "";
			}
			else
			{
				bodyMessageId = dbDataReader.GetString(4);
				username = dbDataReader.GetString(5);
			}
			list.Add(new SpamReport
			{
				ReportId = @int,
				MessageId = @string,
				Modulus = string2,
				Date = int2.FromUnixTime(),
				BodyMessageId = bodyMessageId,
				Username = username
			});
		}
		return list;
	}

	private static List<SpamReport> ParseSpamReports(string expression)
	{
		Regex regex = new Regex("([0-9]+)\\tREPORT \\<(.+)\\> .+\\t(\\w+) \\<(.+)\\>\\t(.+)\\t\\<(.+)\\>\\t\\<(.+)\\>\\t[0-9]+\\t[0-9]+(\\t(.+))?\\r\\n");
		Regex regex2 = new Regex("([a-zA-Z0-9]+)\\.([a-zA-Z0-9\\-]+)\\.([a-zA-Z0-9\\-]+)");
		List<SpamReport> list = new List<SpamReport>();
		foreach (Match item2 in regex.Matches(expression))
		{
			try
			{
				string value = item2.Groups[4].Value;
				Match match2 = regex2.Match(value);
				if (match2.Success && match2.Groups[1].Length == 10)
				{
					value = match2.Groups[2].Value;
				}
				DateTime date;
				try
				{
					date = Convert.ToDateTime(item2.Groups[5].Value.Replace("(", "").Replace(")", "").Replace("UTC", "")
						.Replace("CET", "")
						.Replace("CEST", ""));
				}
				catch (FormatException)
				{
					Log.Debug("Failed to convert DateTime: " + item2.Groups[5].Value);
					date = DateTime.Now;
				}
				SpamReport item = new SpamReport
				{
					ReportId = Convert.ToInt64(item2.Groups[1].Value),
					MessageId = item2.Groups[2].Value,
					Modulus = value,
					Date = date,
					Username = item2.Groups[3].Value,
					BodyMessageId = item2.Groups[6].Value
				};
				list.Add(item);
			}
			catch (Exception ex2)
			{
				Log.Debug("Failed to parse spamreport: " + ex2.Message);
				Log.Debug("Line: " + item2.Value);
			}
		}
		return list;
	}

	public static void ResetReports()
	{
		MinId.Reset();
	}

	private static void FindSpamReports(Engine hPhuse, NntpSettings nntpSettings)
	{
		NNTP nNTP = new NNTP(hPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		if (!nNTP.SelectGroup(nntpSettings.GroupName, ref first, ref last, ref count, out var _, out var errorMsg))
		{
			if (!errorMsg.Equals("Removed"))
			{
				SystemStateChecker.AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
				throw new ProviderException(errorMsg);
			}
			return;
		}
		long first2 = nntpSettings.Position.First;
		long last2 = nntpSettings.Position.Last;
		MinId.UpdateIfRequired(first2);
		if (MinId.IsActive && first < MinId.Value)
		{
			first = MinId.Value;
		}
		if (last < first)
		{
			return;
		}
		long num = -1L;
		long num2 = -1L;
		long num3 = -1L;
		long num4 = -1L;
		if (nntpSettings.Position.First == -1)
		{
			num = first;
			num2 = last;
		}
		else
		{
			if (first < first2)
			{
				num = first;
				num2 = ((last >= first2) ? (first2 - 1) : last);
			}
			if (last > last2)
			{
				num3 = ((first <= last2) ? (last2 + 1) : first);
				num4 = last;
			}
		}
		if (num == -1 && num3 == -1)
		{
			Log.Debug("db update: no new spamreports");
			return;
		}
		long num5 = num2 - num + (num4 - num3);
		if (num > -1)
		{
			num5++;
		}
		if (num3 > -1)
		{
			num5++;
		}
		Log.Debug("Update spamreports db: requesting {0} reports", num5);
		FindSpamReports(nNTP, nntpSettings, num, num2, num3, num4);
	}
}
