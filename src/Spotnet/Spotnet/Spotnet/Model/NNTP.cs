using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Phuse;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;

namespace Spotnet.Model;

internal class NNTP
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly Engine _tPhuse;

	internal NNTP(Engine hPhuse)
	{
		_tPhuse = hPhuse;
	}

	public bool GetArticle(string group, string articleId, ref string resp, out int result, ref string errorMsg)
	{
		if (!articleId.IsNullOrEmpty())
		{
			try
			{
				result = GetResponse(group, out resp, "ARTICLE " + articleId);
				if (result == 220)
				{
					SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
					return true;
				}
				string text = Strings.Left(resp, 500);
				result = SpotHelper.TryToExtractCodeFromResponse(text);
				errorMsg = TranslateError(result, text);
				Log.Error(errorMsg);
			}
			catch (Exception ex)
			{
				errorMsg = ex.Message;
				result = 0;
			}
		}
		else
		{
			errorMsg = "No article";
			result = 0;
		}
		return false;
	}

	public bool GetBodyFromCacheFirst(string group, NNTPSegment segment, out Stream resp, out int resCode, out string errorMsg)
	{
		if (_tPhuse == AppHelper.DownloadPhuse && !segment.SlaveHostname.IsNullOrEmpty())
		{
			if (CachingSystem.GetBody(group, segment, out resp, out resCode, out errorMsg))
			{
				return true;
			}
			if (!Sys.IsShutdownRequested)
			{
				Log.Debug("Redirect to main server: " + segment.MessageId);
			}
		}
		return GetBody(group, segment.MessageId, out resp, out resCode, out errorMsg);
	}

	public bool GetBody(string group, string articleMsgId, out string resp, out int resCode, out string errorMsg)
	{
		if (!articleMsgId.IsNullOrEmpty())
		{
			resCode = 0;
			try
			{
				articleMsgId = SpotHelper.MakeMsg(articleMsgId);
				resCode = GetResponse(group, out resp, "BODY " + articleMsgId);
				string text = resp.ReadLine(2);
				if (text.StartsWith("Ã") || text.StartsWith("Â"))
				{
					resp = Encoding.UTF8.GetString(AppHelper.MakeLatin(resp));
				}
				if (resCode == 222)
				{
					SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
					errorMsg = null;
					return true;
				}
			}
			catch (Exception ex)
			{
				resp = ex.Message;
			}
			if (resCode <= 0)
			{
				resCode = SpotHelper.TryToExtractCodeFromResponse(resp);
			}
			string originalError = Strings.Left(resp, 500);
			errorMsg = TranslateError(resCode, originalError);
		}
		else
		{
			errorMsg = "ArticleID is empty";
			resCode = 1101;
		}
		resp = null;
		return false;
	}

	public bool GetBodies(string group, List<NNTPSegment> segments, out string resp, out int resCode, out string errorMsg)
	{
		if (segments != null && segments.Any())
		{
			resCode = 0;
			try
			{
				StringBuilder stringBuilder = new StringBuilder();
				foreach (NNTPSegment segment in segments)
				{
					stringBuilder.AppendLine("BODY " + SpotHelper.MakeMsg(segment.MessageId));
				}
				resCode = GetResponse(group, out resp, stringBuilder.ToString());
				errorMsg = "";
				return true;
			}
			catch (Exception ex)
			{
				resp = ex.Message;
			}
			if (resCode <= 0)
			{
				resCode = SpotHelper.TryToExtractCodeFromResponse(resp);
			}
			string originalError = Strings.Left(resp, 500);
			errorMsg = TranslateError(resCode, originalError);
		}
		else
		{
			errorMsg = "ArticleIDs list is empty";
			resCode = 1101;
		}
		resp = null;
		return false;
	}

	public bool GetBody(string group, string messageId, out Stream resp, out int resCode, out string errorMsg)
	{
		if (messageId.IsNullOrEmpty())
		{
			errorMsg = "ArticleID is empty";
			resCode = 1101;
			resp = null;
			return false;
		}
		resCode = 0;
		string text = "";
		try
		{
			string text2 = SpotHelper.MakeMsg(messageId);
			resCode = GetResponse(group, out resp, "BODY " + text2);
			if (resCode == 222)
			{
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
				errorMsg = null;
				return true;
			}
		}
		catch (Exception ex)
		{
			resp = null;
			text = ex.Message;
		}
		if (resCode <= 0)
		{
			resCode = ((resp == null) ? SpotHelper.TryToExtractCodeFromResponse(text) : SpotHelper.TryToExtractCodeFromResponse(resp));
		}
		string originalError = ((resp == null) ? Strings.Left(text, 200) : Module.GetString(resp, 0L, 200L));
		errorMsg = TranslateError(resCode, originalError);
		resp = null;
		return false;
	}

	public string GetField(string group, string field, long xStart, long end, Action<long> onNewData, out int result, ref string errorMsg)
	{
		string text = $"XHDR {field} {xStart}-{end}";
		try
		{
			VirtualNNTP.NewDataForSpeedReportSubscribe(text, onNewData);
			result = GetResponse(group, out string result2, text);
			if (result == 221)
			{
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
				return result2;
			}
			string text2 = Strings.Left(result2, 500);
			result = SpotHelper.TryToExtractCodeFromResponse(text2);
			errorMsg = TranslateError(result, text2);
		}
		catch (Exception ex)
		{
			errorMsg = ex.Message;
			result = 0;
		}
		finally
		{
			VirtualNNTP.NewDataForSpeedReportUnsubscribe(text);
		}
		return null;
	}

	public bool GetHeader(string group, string articleId, out string resp, out int result, out string errorMsg)
	{
		resp = null;
		errorMsg = null;
		if (!articleId.IsNullOrEmpty())
		{
			try
			{
				result = GetResponse(group, out resp, "HEAD " + articleId);
				if (result == 221)
				{
					SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
					return true;
				}
			}
			catch (Exception ex)
			{
				errorMsg = ex.Message;
				result = 0;
				return false;
			}
			string text = Strings.Left(resp, 500);
			result = SpotHelper.TryToExtractCodeFromResponse(text);
			errorMsg = TranslateError(result, text);
		}
		else
		{
			errorMsg = "No such article";
			result = 0;
		}
		return false;
	}

	public string GetHeaders(string group, long start, long end, Action<long> onNewData, out int result, out string errorMsg)
	{
		string text = "XOVER " + start + "-" + end;
		string result2;
		try
		{
			VirtualNNTP.NewDataForSpeedReportSubscribe(text, onNewData);
			result = GetResponse(group, out result2, text);
			if (result == 224)
			{
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
				errorMsg = null;
				return result2;
			}
		}
		catch (Exception ex)
		{
			errorMsg = ex.Message;
			result = 0;
			return null;
		}
		finally
		{
			VirtualNNTP.NewDataForSpeedReportUnsubscribe(text);
		}
		result2 = Strings.Left(result2, 500);
		result = SpotHelper.TryToExtractCodeFromResponse(result2);
		errorMsg = TranslateError(result, result2);
		return null;
	}

	private int GetResponse(string group, out string result, params string[] commands)
	{
		result = _tPhuse.Slots.Send(group, commands.ToList());
		return SpotHelper.TryToExtractCodeFromResponse(result);
	}

	private int GetTestResponse(string group, out string result)
	{
		List<string> command = new List<string> { "START TEST CONNECTION" };
		result = _tPhuse.Slots.Send(group, command);
		return SpotHelper.TryToExtractCodeFromResponse(result);
	}

	private int GetResponse(string group, out Stream result, params string[] commands)
	{
		result = _tPhuse.Slots.SendAndGetStream(group, commands.ToList(), null, isDownloaderBody: true);
		return SpotHelper.TryToExtractCodeFromResponse(result);
	}

	public bool PostData(string group, string data, ref string resp, out int result, ref string errorMsg)
	{
		try
		{
			result = GetResponse(group, out resp, "POST", data);
			if (result == 240)
			{
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
				return true;
			}
		}
		catch (Exception ex)
		{
			errorMsg = ex.Message;
			result = 0;
			return false;
		}
		string text = Strings.Left(resp, 500);
		result = SpotHelper.TryToExtractCodeFromResponse(text);
		errorMsg = TranslateError(result, text);
		return false;
	}

	public bool SelectGroup(string group, ref long first, ref long last, ref long count, out int result, out string errorMsg, bool testConnection = false)
	{
		bool result2 = false;
		string result3;
		try
		{
			result = (testConnection ? GetTestResponse(group, out result3) : GetResponse(group, out result3));
			if (result == 211)
			{
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
				result2 = true;
			}
		}
		catch (Exception ex)
		{
			errorMsg = ex.Message;
			result = 0;
			return false;
		}
		string sResponse = Strings.Left(result3, 500);
		result = SpotHelper.TryToExtractCodeFromResponse(sResponse);
		try
		{
			string[] array = result3.Split(' ');
			last = long.Parse(array[3]);
			first = long.Parse(array[2]);
			count = long.Parse(array[1]);
			errorMsg = null;
			return true;
		}
		catch (Exception ex2)
		{
			errorMsg = ex2.Message;
			return result2;
		}
	}

	private string TranslateError(int lRet, string originalError)
	{
		switch (lRet)
		{
		case 381:
		case 400:
		case 450:
		case 452:
		case 480:
		case 481:
		case 482:
		case 502:
			if (!originalError.Contains("connection"))
			{
				return Words.UsernamePasswordWrong + ". Msg: " + originalError;
			}
			return Words.ConnectionsMaxNumberReached + ". Msg: " + originalError;
		case 411:
			return Words.GroupNotFound + ". Msg: " + originalError;
		case 931:
		case 950:
		case 952:
		case 995:
			return Words.TimeoutOccured + ". Msg: " + originalError;
		case 941:
			return Words.HostIsUnknown + ". Msg: " + originalError;
		default:
			return originalError;
		}
	}
}
