using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Helpers;

internal static class SystemStateChecker
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly Dictionary<SystemStateProblemEnum, string> ListOfProblems = new Dictionary<SystemStateProblemEnum, string>();

	private static Timer _nntpServerCheckTimer;

	private static Timer _updateServerCheckTimer;

	internal static bool IsGreen => !ListOfProblems.Any();

	internal static string ProblemsDescription
	{
		get
		{
			if (IsGreen)
			{
				return Words.SystemStateIsGreen;
			}
			return ListOfProblems.Keys.Aggregate("", (string c, SystemStateProblemEnum problem) => c + "* " + GetSystemStateProblemSentance(problem) + Environment.NewLine) + Words.SystemStateIsNotGreen;
		}
	}

	internal static event Action<SystemStateEventTypeEnum, SystemStateProblemEnum> StateChanged;

	internal static void Start()
	{
		Stop();
		_nntpServerCheckTimer = new Timer(10000.0)
		{
			AutoReset = false
		};
		_nntpServerCheckTimer.Elapsed += NntpServerCheckTimerOnElapsed;
		_nntpServerCheckTimer.Start();
	}

	private static void NntpServerCheckTimerOnElapsed(object sender, ElapsedEventArgs args)
	{
		try
		{
			NntpServerCheck();
		}
		finally
		{
			_nntpServerCheckTimer.Interval = (ListOfProblems.ContainsKey(SystemStateProblemEnum.NntpServerIsNotAvailable) ? 60000 : 300000);
			_nntpServerCheckTimer.Start();
		}
	}

	public static void NntpServerCheck(bool tryToSwitchToOtherPorts = false)
	{
		NNTP nNTP = new NNTP(AppHelper.HeaderPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		if (nNTP.SelectGroup(Settings.Default.HeaderGroup, ref first, ref last, ref count, out var _, out var errorMsg))
		{
			RemoveProblem(SystemStateProblemEnum.NntpServerIsNotAvailable);
		}
		else if (tryToSwitchToOtherPorts)
		{
			Log.Error("Problem with connecting to " + AppHelper.ServersDb.OHeader.Server + ":" + AppHelper.ServersDb.OHeader.Port + ". Try to check other ports.");
			if (!AppHelper.TryToCheckOtherUsenetPorts())
			{
				AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
			}
		}
		else
		{
			AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
		}
	}

	internal static void Stop()
	{
		if (_nntpServerCheckTimer != null)
		{
			_nntpServerCheckTimer.Stop();
			_nntpServerCheckTimer = null;
		}
		if (_updateServerCheckTimer != null)
		{
			_updateServerCheckTimer.Stop();
			_updateServerCheckTimer = null;
		}
	}

	private static string GetSystemStateProblemSentance(SystemStateProblemEnum problem)
	{
		return problem switch
		{
			SystemStateProblemEnum.NzbGet => Words.SystemStateProblemsWithNzbGet + " " + ListOfProblems[problem], 
			SystemStateProblemEnum.NntpServerIsNotAvailable => Words.SystemStateProblemsWithNntpServer + " " + ListOfProblems[problem], 
			SystemStateProblemEnum.UpdateServerIsNotAvaiable => Words.SystemStateProblemsWithUpdateServer + " " + ListOfProblems[problem], 
			SystemStateProblemEnum.HitConnectionsLimit => Words.SystemStateProblemsWithNntpServer + " " + ListOfProblems[problem], 
			_ => "Unknown problem.", 
		};
	}

	internal static void AddProblem(SystemStateProblemEnum problem, string errorDescription)
	{
		if (!Sys.IsShutdownRequested)
		{
			errorDescription = ExtendErrorDescription(errorDescription);
			if (ListOfProblems.ContainsKey(problem))
			{
				ListOfProblems[problem] = errorDescription;
			}
			else
			{
				ListOfProblems.Add(problem, errorDescription);
				Log.Warn(GetSystemStateProblemSentance(problem));
			}
			SystemStateChecker.StateChanged(SystemStateEventTypeEnum.Add, problem);
		}
	}

	private static string ExtendErrorDescription(string errorDescription)
	{
		if (errorDescription.Contains("Authentication Failed"))
		{
			errorDescription = errorDescription + ". " + Words.UsernamePasswordWrong + ". " + Words.ContactYourProvider;
		}
		if (errorDescription.Contains("Access Denied"))
		{
			errorDescription = errorDescription + ". " + Words.ConnectionsLimitHowToSolve;
		}
		return errorDescription;
	}

	internal static void RemoveProblem(SystemStateProblemEnum problem)
	{
		if (!Sys.IsShutdownRequested)
		{
			ListOfProblems.Remove(problem);
			SystemStateChecker.StateChanged(SystemStateEventTypeEnum.Remove, problem);
		}
	}

	static SystemStateChecker()
	{
		SystemStateChecker.StateChanged = delegate
		{
		};
	}
}
