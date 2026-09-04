using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration.Provider;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse;
using Spotnet.Properties;

namespace Spotnet.Model;

internal static class Headers
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static long _workTotal;

	private static CancellationToken _cancelToken;

	private static readonly object SyncRoot = new object();

	private static BlockingCollection<List<Spot>> _workXmls;

	private static readonly AverageSpeedCalculator SpeedCalculator = new AverageSpeedCalculator();

	private static readonly IMinimumId MinId = new MinimumId("headers");

	private static Dictionary<string, string> _modulusesFromResourceFile;

	internal static bool InProgress { get; private set; }

	internal static int ProgressValue { get; private set; }

	internal static string DownloadSpeedString
	{
		get
		{
			string lastSpeedString = SpeedCalculator.GetLastSpeedString();
			if (!lastSpeedString.IsNullOrEmpty())
			{
				return $"  ({lastSpeedString})";
			}
			return "";
		}
	}

	internal static void InitializeForAutoTests(BlockingCollection<List<Spot>> spotsToAddAndRemove)
	{
		_workXmls = spotsToAddAndRemove;
	}

	internal static Task FindHeadersAsync(BlockingCollection<List<Spot>> spotsToAddAndRemove, Engine tPhuse, NntpSettings xParam, CancellationToken cToken)
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
		_workXmls = spotsToAddAndRemove;
		return Task.Factory.StartNew(delegate
		{
			FindHeaders(tPhuse, xParam);
		}, _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task t)
		{
			_workXmls.CompleteAdding();
			InProgress = false;
			if (t.IsCanceled || t.Exception == null)
			{
				return;
			}
			throw t.Exception;
		});
	}

	private static void FindHeaders(NNTP nntp, NntpSettings nntpSettings, long beginFirstId, long beginLastId, long endFirstId, long endLastId)
	{
		List<NNTPWork> second = SpotHelper.CreateWork(beginFirstId, beginLastId, Settings.Default.SpotChunkSize);
		List<NNTPWork> list = SpotHelper.CreateWork(endFirstId, endLastId, Settings.Default.SpotChunkSize);
		list.Reverse();
		List<NNTPWork> list2 = list.Concat(second).ToList();
		if (!list2.Any())
		{
			return;
		}
		_workTotal = list2.Count;
		RSACryptoServiceProvider[] rsa = SpotHelper.GetRsa(nntpSettings.TrustedKeys);
		for (int i = 0; i < list2.Count; i++)
		{
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
			if (!ParseHeaders(nntpSettings, rsa, i, headers))
			{
				break;
			}
		}
		MinId.IsActive = true;
	}

	internal static bool ParseHeaders(NntpSettings nntpSettings, RSACryptoServiceProvider[] rsa, int workCounter, string expression)
	{
		Worker obj = new Worker
		{
			Rsa = rsa,
			InstanceCount = workCounter + 1,
			XSettings = nntpSettings,
			HeaderData = expression
		};
		int length = expression.Length;
		if (length < 5)
		{
			throw new Exception("Data received is too short. Code 720");
		}
		if (!expression.EndsWith("\r\n.\r\n"))
		{
			throw new Exception($"Data ending is wrong: {expression.Substring(length - 5, 5)}. Code 721");
		}
		return obj.ParseHeaders(OnWorkDone);
	}

	public static long GetHeadersToDownload(Engine hPhuse, NntpSettings nntpSettings, out long beginFirstId, out long endFirstId, out long beginLastId, out long endLastId, out long dbFirstId, out long dbLastId)
	{
		beginFirstId = -1L;
		beginLastId = -1L;
		endFirstId = -1L;
		endLastId = -1L;
		NNTP nNTP = new NNTP(hPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		dbFirstId = -1L;
		dbLastId = -1L;
		if (!nNTP.SelectGroup(nntpSettings.GroupName, ref first, ref last, ref count, out var _, out var errorMsg))
		{
			if (errorMsg.Equals("Removed"))
			{
				return 0L;
			}
			SystemStateChecker.AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
			throw new ProviderException(errorMsg);
		}
		dbFirstId = nntpSettings.Position.First;
		dbLastId = nntpSettings.Position.Last;
		MinId.UpdateIfRequired(dbFirstId);
		if (MinId.IsActive && first < MinId.Value)
		{
			first = MinId.Value;
		}
		if (last < first)
		{
			return 0L;
		}
		if (nntpSettings.Position.First == -1)
		{
			// Nothing in the database yet. Rather than walking the whole group from its
			// oldest surviving article, start where the user's chosen range begins.
			beginFirstId = ApplyInitialFetchRange(nNTP, nntpSettings, first, last);
			beginLastId = last;
		}
		else
		{
			if (first < dbFirstId)
			{
				beginFirstId = first;
				beginLastId = ((last >= dbFirstId) ? (dbFirstId - 1) : last);
			}
			if (last > dbLastId)
			{
				endFirstId = ((first <= dbLastId) ? (dbLastId + 1) : first);
				endLastId = last;
			}
		}
		if (beginFirstId == -1 && endFirstId == -1)
		{
			return 0L;
		}
		long num = beginLastId - beginFirstId + (endLastId - endFirstId);
		if (beginFirstId > -1)
		{
			num++;
		}
		if (endFirstId > -1)
		{
			num++;
		}
		return num;
	}

	/// <summary>
	/// The article number a first synchronisation should start from, given
	/// <see cref="Settings.InitialFetchDays"/> and the retention already in force.
	/// </summary>
	/// <remarks>
	/// Headers older than <see cref="DbUpdater.RetentionStartDate"/> are dropped on save
	/// anyway (see <see cref="OnWorkDone"/>), so that date is the floor even when the user
	/// asked for everything - downloading headers only to discard them is pure waiting.
	/// Anything that cannot be determined leaves <paramref name="first"/> untouched, which
	/// is the behaviour this replaced.
	/// </remarks>
	private static long ApplyInitialFetchRange(NNTP nntp, NntpSettings nntpSettings, long first, long last)
	{
		try
		{
			DateTime cutoff = DbUpdater.RetentionStartDate;
			int days = Settings.Default.InitialFetchDays;
			if (days > 0)
			{
				DateTime requested = DateTime.Now.AddDays(-days);
				if (requested > cutoff)
				{
					cutoff = requested;
				}
			}
			DateTime cutoffUtc = cutoff.ToUniversalTime();
			if (cutoffUtc <= SpotHelper.Epoch)
			{
				return first;
			}
			long found = ArticleWatermark.FindFirstArticleOnOrAfter(first, last, cutoffUtc, (long from, long to) => ProbeFirstArticle(nntp, nntpSettings.GroupName, from, to));
			if (found == ArticleWatermark.Undetermined)
			{
				Log.Debug("Could not date the articles in {0}; syncing the whole group.", nntpSettings.GroupName);
				return first;
			}
			long start = Math.Min(Math.Max(found, first), last);
			Log.Debug("First sync of {0} starts at article {1} instead of {2} ({3} skipped, cutoff {4:yyyy-MM-dd}).", nntpSettings.GroupName, start, first, start - first, cutoff);
			return start;
		}
		catch (Exception ex)
		{
			// A provider that will not answer a probe must not stop the sync; fall back to
			// the full range.
			Log.Debug("Failed to narrow the first sync range: " + ex.Message);
			return first;
		}
	}

	/// <summary>One XOVER probe, reduced to the first dated article it returned.</summary>
	private static ArticleWatermark.ArticleStamp? ProbeFirstArticle(NNTP nntp, string group, long from, long to)
	{
		if (_cancelToken.IsCancellationRequested)
		{
			return null;
		}
		string headers = nntp.GetHeaders(group, from, to, delegate
		{
		}, out int _, out string errorMsg);
		if (headers.IsNullOrEmpty())
		{
			if (!errorMsg.IsNullOrEmpty())
			{
				Log.Debug("Probe XOVER {0}-{1} failed: {2}", from, to, errorMsg);
			}
			return null;
		}
		return ArticleWatermark.FirstStampIn(headers);
	}

	private static void FindHeaders(Engine hPhuse, NntpSettings nntpSettings)
	{
		long beginFirstId;
		long endFirstId;
		long beginLastId;
		long endLastId;
		long dbFirstId;
		long dbLastId;
		long headersToDownload = GetHeadersToDownload(hPhuse, nntpSettings, out beginFirstId, out endFirstId, out beginLastId, out endLastId, out dbFirstId, out dbLastId);
		if (headersToDownload <= 0)
		{
			Log.Debug("No new spots");
			return;
		}
		Log.Debug("Update spots db: requesting {0} headers", headersToDownload);
		FindHeaders(new NNTP(hPhuse), nntpSettings, beginFirstId, beginLastId, endFirstId, endLastId);
	}

	private static bool OnWorkDone(bool errors, int workDone, int instanceCount, string sError, List<Spot> zxOut, long numberOfNewSpots, bool noProgress)
	{
		bool result = true;
		_cancelToken.ThrowIfCancellationRequested();
		if (errors)
		{
			throw new Exception(Words.SpotsErrorWhileProcessing + ": " + sError);
		}
		if (zxOut != null && zxOut.Any())
		{
			if (_workXmls.IsAddingCompleted)
			{
				throw new Exception("Error on save spots to db. See log for details");
			}
			try
			{
				if (zxOut.RemoveAll((Spot s) => s.Stamp > 0 && s.Stamp.FromUnixTime() < DbUpdater.RetentionStartDate) > 50)
				{
					result = false;
				}
				if (zxOut.Any())
				{
					_workXmls.Add(zxOut);
					List<Spot> source = zxOut.Where((Spot e) => e.Article > 0).ToList();
					if (source.Any())
					{
						long minId = source.Min((Spot e) => e.Article);
						MinId.UpdateIfRequired(minId);
					}
				}
			}
			catch (Exception)
			{
				_cancelToken.ThrowIfCancellationRequested();
				throw;
			}
			if (_workTotal < instanceCount)
			{
				_workTotal = instanceCount;
			}
			ProgressValue = (int)Math.Round(100.0 / (double)_workTotal * (double)instanceCount);
		}
		return result;
	}

	public static async Task UpdateNullModulusSpotsAsync(Engine tPhuse, NntpSettings xParam, Action<string, int> reportAction, CancellationToken cToken)
	{
		lock (SyncRoot)
		{
			if (InProgress)
			{
				throw new Exception("Task is already running");
			}
			InProgress = true;
		}
		try
		{
			_cancelToken = cToken;
			_workXmls = null;
			await Task.Factory.StartNew(delegate
			{
				int num = 0;
				if (_modulusesFromResourceFile == null)
				{
					_modulusesFromResourceFile = LoadModulusesFromResourceFile();
				}
				using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
				do
				{
					IL_001a:
					string sQuery = "SELECT msgid FROM spots WHERE modulus is NULL AND key!=2 AND date>1356998400 ORDER by rowid DESC LIMIT " + 1000;
					HashSet<string> hashSet = new HashSet<string>();
					using (ISqlDbTransaction transaction = sqlDb.BeginReadTransaction())
					{
						using DbDataReader dbDataReader = sqlDb.ExecuteReader(sQuery, transaction);
						if (dbDataReader == null)
						{
							throw new Exception("Error during query executing.");
						}
						while (dbDataReader.Read())
						{
							string text = RuntimeHelpers.GetObjectValue(dbDataReader[0]) as string;
							if (!text.IsNullOrWhiteSpace())
							{
								hashSet.Add(text);
							}
						}
					}
					if (!hashSet.Any())
					{
						break;
					}
					Log.Debug("Spots with null modulus: " + hashSet.Count);
					Dictionary<string, string> dictionary = new Dictionary<string, string>();
					foreach (string item in hashSet)
					{
						if (_modulusesFromResourceFile.ContainsKey(item))
						{
							dictionary.Add(item, _modulusesFromResourceFile[item]);
							_modulusesFromResourceFile.Remove(item);
						}
					}
					if (dictionary.Any())
					{
						UpdateNullModuluses(sqlDb, dictionary);
						hashSet.ExceptWith(dictionary.Keys);
						num += 5;
						UpdateNullModulusProgress(reportAction, num, 1000);
						if (!hashSet.Any())
						{
							goto IL_001a;
						}
					}
					if (cToken.IsCancellationRequested)
					{
						break;
					}
					Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
					foreach (string item2 in hashSet)
					{
						if (cToken.IsCancellationRequested)
						{
							break;
						}
						string modulus;
						string errorMsg;
						bool flag = GetModulusFromUsenet(tPhuse, xParam, item2, out modulus, out errorMsg);
						if (!flag && errorMsg.StartsWith("430"))
						{
							flag = true;
						}
						if (!flag)
						{
							throw new Exception(errorMsg);
						}
						num++;
						if (num >= 1000)
						{
							break;
						}
						dictionary2.Add(item2, modulus);
						UpdateNullModulusProgress(reportAction, num, 1000);
					}
					if (dictionary2.Any())
					{
						UpdateNullModuluses(sqlDb, dictionary2);
					}
				}
				while (!cToken.IsCancellationRequested && num < 1000);
			}, cToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		}
		finally
		{
			InProgress = false;
		}
	}

	private static void UpdateNullModulusProgress(Action<string, int> reportAction, int numberOfSpotsDownloaded, int numberOfSpotsToDownloadMax)
	{
		int num = (int)((double)numberOfSpotsDownloaded * 97.0 / (double)numberOfSpotsToDownloadMax);
		if (num == 0)
		{
			num = 1;
		}
		reportAction?.Invoke(Words.DBUpdateNullModulusInProgress, num);
	}

	public static IEnumerable<string> ReadLines(Func<Stream> streamProvider, Encoding encoding)
	{
		using Stream stream = streamProvider();
		if (stream == null)
		{
			yield break;
		}
		using StreamReader reader = new StreamReader(stream, encoding);
		string text;
		while ((text = reader.ReadLine()) != null)
		{
			yield return text;
		}
	}

	private static Dictionary<string, string> LoadModulusesFromResourceFile()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		foreach (string item in ReadLines(() => Assembly.GetExecutingAssembly().GetManifestResourceStream("Spotnet.Resources.null_modulus.txt"), Encoding.UTF8))
		{
			string[] array = item.Split(new char[1] { ' ' }, 2);
			if (array.Length == 2)
			{
				dictionary.Add(array[0], array[1]);
			}
		}
		return dictionary;
	}

	private static void UpdateNullModuluses(ISqlDb db, Dictionary<string, string> msgIds)
	{
		using ISqlDbTransaction sqlDbTransaction = db.BeginWriteTransaction();
		// Both the modulus and the message id come off the wire, so they are bound as
		// parameters rather than interpolated. Reusing one command across the loop also
		// lets SQLite keep the prepared statement.
		using DbCommand dbCommand = db.CreateCommand(sqlDbTransaction);
		dbCommand.CommandText = "UPDATE spots SET modulus=? WHERE msgid=?";
		DbParameter modulusParameter = dbCommand.CreateParameter();
		dbCommand.Parameters.Add(modulusParameter);
		DbParameter msgIdParameter = dbCommand.CreateParameter();
		dbCommand.Parameters.Add(msgIdParameter);
		foreach (KeyValuePair<string, string> msgId in msgIds)
		{
			modulusParameter.Value = msgId.Value;
			msgIdParameter.Value = msgId.Key;
			db.ExecuteNonQuery(dbCommand);
		}
		sqlDbTransaction.Commit();
	}

	private static bool GetModulusFromUsenet(Engine tPhuse, NntpSettings xParam, string msgid, out string modulus, out string errorMsg)
	{
		modulus = "none";
		if (new NNTP(tPhuse).GetHeader(xParam.GroupName, SpotHelper.MakeMsg(msgid), out var resp, out var _, out errorMsg))
		{
			using (StringReader stringReader = new StringReader(resp))
			{
				string text;
				while ((text = stringReader.ReadLine()) != null)
				{
					if (text.ToUpper().StartsWith("X-USER-KEY:"))
					{
						modulus = GetModulusFromLine(text) ?? "none";
						break;
					}
				}
			}
			return true;
		}
		return false;
	}

	private static string GetModulusFromLine(string keyLine)
	{
		string text = null;
		try
		{
			text = Strings.Mid(keyLine, keyLine.IndexOf(":", StringComparison.Ordinal) + 3);
			if (text.IsNullOrWhiteSpace())
			{
				return null;
			}
			if (text.ToLower().Contains("<modulus>"))
			{
				text = text.Substring(text.ToLower().IndexOf("<modulus>", StringComparison.Ordinal) + 9);
				if (text.Contains("<"))
				{
					text = text.Substring(0, text.IndexOf("<", StringComparison.Ordinal));
				}
			}
			else
			{
				text = SpotHelper.FixPadding(SpotHelper.UnSpecialString(text));
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return text;
	}

	public static void ResetHeaders()
	{
		MinId.Reset();
	}

	public static void UpdateMinHeader(long newMinId)
	{
		MinId.Value = newMinId;
	}
}
