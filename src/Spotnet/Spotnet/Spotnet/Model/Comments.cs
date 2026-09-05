using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration.Provider;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse;
using Spotnet.Properties;

namespace Spotnet.Model;

public static class Comments
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object SyncRoot = new object();

	private static CancellationToken _cancelToken;

	private static Action _commentsDbShouldBeUpdatedNotification;

	private static readonly AverageSpeedCalculator SpeedCalculator = new AverageSpeedCalculator();

	private static readonly IMinimumId MinId = new MinimumId("comments");

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

	internal static Task FindCommentSpotRelationAsync(BlockingCollection<List<Comment>> commentLists, Engine tPhuse, NntpSettings xParam, Action onCommentsDbShouldBeUpdated, CancellationToken cToken)
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
		_commentsDbShouldBeUpdatedNotification = onCommentsDbShouldBeUpdated;
		return Task.Factory.StartNew(delegate
		{
			LoadInfoForCommentsDb(tPhuse, xParam, commentLists);
		}, _cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task t)
		{
			commentLists.CompleteAdding();
			InProgress = false;
			if (t.IsCanceled || t.Exception == null)
			{
				return;
			}
			throw t.Exception;
		});
	}

	internal static Task StartLoadCommentsBody(Engine tPhuse, List<long> articleIDs, NntpSettings xParam, Action<string, int> reportAction, Action<Comment> onNewComment, CancellationToken cToken)
	{
		return Task.Factory.StartNew(delegate
		{
			try
			{
				LoadCommentsBody(tPhuse, articleIDs, xParam, reportAction, onNewComment, cToken);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}, cToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	private static void LoadInfoForCommentsDb(Engine tPhuse, NntpSettings nntpSettings, BlockingCollection<List<Comment>> commentLists)
	{
		long num = 0L;
		NNTP nNTP = new NNTP(tPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		if (!nNTP.SelectGroup(nntpSettings.GroupName, ref first, ref last, ref count, out var result, out var errorMsg))
		{
			if (!_cancelToken.IsCancellationRequested)
			{
				SystemStateChecker.AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
				throw new ProviderException("Error on getting comments: " + errorMsg);
			}
			return;
		}
		if (nntpSettings.GroupName.Equals("free.usenet") && first < 3000000)
		{
			first = 3000000L;
		}
		long num2 = -1L;
		long sLast = -1L;
		long num3 = -1L;
		long sLast2 = -1L;
		long first2 = nntpSettings.Position.First;
		long last2 = nntpSettings.Position.Last;
		MinId.UpdateIfRequired(first2);
		if (MinId.IsActive && first < MinId.Value)
		{
			first = MinId.Value;
		}
		if (last < first)
		{
			Log.Debug("No new comments. lastId: " + last + " firstId: " + first);
			return;
		}
		if (nntpSettings.Position.First == -1)
		{
			num2 = first;
			sLast = last;
		}
		else
		{
			if (first < first2)
			{
				num2 = first;
				sLast = ((last >= first2) ? (first2 - 1) : last);
			}
			if (last > last2)
			{
				num3 = ((first <= last2) ? (last2 + 1) : first);
				sLast2 = last;
			}
		}
		if (num2 == -1 && num3 == -1)
		{
			Log.Debug("No new comments. db: {0}/{1}. server: {2}/{3}", first2, last2, first, last);
			return;
		}
		List<NNTPWork> second = SpotHelper.CreateWork(num2, sLast, Settings.Default.CommentChunkSize);
		List<NNTPWork> list = SpotHelper.CreateWork(num3, sLast2, Settings.Default.CommentChunkSize);
		list.Reverse();
		List<NNTPWork> list2 = list.Concat(second).ToList();
		if (!list2.Any())
		{
			Log.Debug("No new comments");
			return;
		}
		long num4 = list2.LongCount();
		long num5 = list2.Sum((NNTPWork w) => w.xEnd - w.xStart);
		Log.Debug("Update comments db: requesting {0} comments", num5);
		if (_commentsDbShouldBeUpdatedNotification != null && num5 > 5000)
		{
			_commentsDbShouldBeUpdatedNotification();
		}
		bool flag = false;
		foreach (NNTPWork item in list2)
		{
			while (commentLists.Count > 5 && !_cancelToken.IsCancellationRequested && !commentLists.IsAddingCompleted)
			{
				Thread.Sleep(500);
			}
			if (num4 < num)
			{
				num4 = num;
			}
			ProgressValue = (int)Math.Round(100.0 / (double)num4 * (double)num);
			num++;
			int num6 = 1;
			string field;
			do
			{
				if (_cancelToken.IsCancellationRequested)
				{
					return;
				}
				if (num6 > 1)
				{
					Thread.Sleep(5000);
					if (_cancelToken.IsCancellationRequested)
					{
						return;
					}
					Log.Debug("Try number {0} to the request: XHDR References {1}-{2}. Group {3}", num6, item.xStart, item.xEnd, nntpSettings.GroupName);
				}
				errorMsg = "";
				field = nNTP.GetField(nntpSettings.GroupName, "References", item.xStart, item.xEnd, SpeedCalculator.AddNewValue, out result, ref errorMsg);
			}
			while (!errorMsg.IsNullOrEmpty() && num6++ < 2);
			if (_cancelToken.IsCancellationRequested)
			{
				return;
			}
			if (field.IsNullOrEmpty())
			{
				throw new Exception(Words.ErrorOnRetrievingHeaders + ": " + errorMsg);
			}
			string[] array = Strings.Split(field, "\r\n");
			if (array.Length < 3)
			{
				throw new Exception(Words.ErrorOnRetrievingHeaders + ":\r\n\r\nCode 620");
			}
			if (array[array.Length - 1].Length > 0)
			{
				throw new Exception(Words.ErrorOnRetrievingHeaders + ":\r\n\r\nCode 621");
			}
			if (array[array.Length - 2] != ".")
			{
				throw new Exception(Words.ErrorOnRetrievingHeaders + ":\r\n\r\nCode 631");
			}
			if (array.Length <= 3)
			{
				continue;
			}
			List<Comment> list3 = new List<Comment>();
			for (int num7 = array.Length - 3; num7 >= 1; num7--)
			{
				string[] array2 = Strings.Split(array[num7]);
				if (array2.Length > 1)
				{
					Comment comment = new Comment
					{
						Article = Conversions.ToLong(array2[0])
					};
					if (comment.Article >= 1 && array2[1] != null && array2[1].Length >= 4)
					{
						int num8 = array2[1].IndexOf("@", StringComparison.InvariantCulture);
						if (num8 >= 2)
						{
							comment.MessageId = array2[1].Substring(1, num8);
							list3.Add(comment);
							MinId.UpdateIfRequired(comment.Article);
						}
					}
				}
			}
			if (commentLists.IsAddingCompleted)
			{
				return;
			}
			if (list3.Any())
			{
				commentLists.Add(list3);
				if (RetentionReached(list3, tPhuse, nntpSettings, out var areCommentsFailed))
				{
					flag = areCommentsFailed;
					break;
				}
			}
		}
		if (!flag)
		{
			MinId.IsActive = true;
		}
	}

	private static bool RetentionReached(List<Comment> comments, Engine tPhuse, NntpSettings nntpSettings, out bool areCommentsFailed)
	{
		areCommentsFailed = false;
		if (!comments.Any())
		{
			return false;
		}
		bool result = true;
		List<Comment> list = comments.OrderBy((Comment c) => c.Article).ToList();
		int num = 0;
		int num2 = 5;
		for (int i = 0; i < num2 && i < list.Count; i++)
		{
			Comment comment = new Comment
			{
				Article = list[i].Article
			};
			if (!comment.GetCommentDateFromTheNet(tPhuse, nntpSettings, out var errorMsg))
			{
				Log.Debug("Failed to get the comment: " + errorMsg);
				num++;
			}
			else if (comment.Created >= DbUpdater.RetentionStartDate)
			{
				result = false;
				break;
			}
		}
		areCommentsFailed = num == num2;
		return result;
	}

	private static void LoadCommentsBody(Engine tPhuse, ICollection<long> articleIDs, NntpSettings xParam, Action<string, int> reportAction, Action<Comment> onNewComment, CancellationToken cToken)
	{
		reportAction?.Invoke(Words.CommentsUpdating, 0);
		int num = -1;
		long num2 = 0L;
		foreach (long articleID in articleIDs)
		{
			int num3 = (int)Math.Round(100.0 / (double)articleIDs.Count * (double)num2);
			if (num3 != num)
			{
				reportAction?.Invoke(Words.CommentsUpdating.Replace("...", "") + " (" + num3 + "%)", num3);
				num = num3;
			}
			num2++;
			if (cToken.IsCancellationRequested)
			{
				break;
			}
			try
			{
				Comment comment = new Comment
				{
					Article = articleID
				};
				string sError = "";
				if (comment.GetCommentFieldsFromTheNet(tPhuse, xParam, includeBody: true, ref sError))
				{
					onNewComment?.Invoke(comment);
				}
				else
				{
					Log.Warn("Failed to get a comment body. Message: " + sError);
				}
			}
			catch (Exception ex)
			{
				if (cToken.IsCancellationRequested || ex.Message.Equals("Removed") || ex.Message.Equals("Cancelled"))
				{
					break;
				}
				if (ex.Message.StartsWith("430"))
				{
					Log.Warn(ex.Message + ". Article: " + articleID);
				}
				Log.Exception(ex);
			}
		}
	}

	public static void ResetComments()
	{
		MinId.Reset();
	}
}
