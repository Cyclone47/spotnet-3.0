using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Downloader;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Phuse.NNTP.Net;

internal class ConnectionTask
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private void Download(VirtualConnection vCon)
	{
		DateTime dateTime = DateTime.Now;
		DateTime now = DateTime.Now;
		int num = ((vCon.IsCacheSlave && !CachingSystem.IsBridgedModeOn) ? Settings.Default.ConnectionIdleTimeoutSlave : Settings.Default.ConnectionIdleTimeout);
		while (vCon.Enabled && !Sys.IsShutdownRequested)
		{
			vCon.Token.Token.ThrowIfCancellationRequested();
			NNTPCommands nNTPCommands = vCon.Scheduler.FindWork(vCon);
			if (nNTPCommands == null)
			{
				if ((DateTime.Now - dateTime).TotalSeconds > 5.0)
				{
					vCon.ClearData();
					dateTime = DateTime.MaxValue;
				}
				if (vCon.SocketStatus != NNTPStatus.Closed && (DateTime.Now - now).TotalMilliseconds > (double)num)
				{
					vCon.Disconnect("Idle connection close");
				}
				else
				{
					Wait(vCon, 500);
				}
			}
			else
			{
				ProcessAnswer(vCon, nNTPCommands);
				dateTime = DateTime.Now;
				now = DateTime.Now;
			}
		}
	}

	private void ProcessAnswer(VirtualConnection vCon, NNTPCommands nntpCommands)
	{
		vCon.Token.Token.ThrowIfCancellationRequested();
		long ticks = DateTime.UtcNow.Ticks;
		nntpCommands = vCon.ExecuteCommand(nntpCommands);
		vCon.Token.Token.ThrowIfCancellationRequested();
		if (nntpCommands.Status == WorkStatus.Completed)
		{
			nntpCommands.Statistics(nntpCommands.Data.Length, DateTime.UtcNow.Subtract(new DateTime(ticks)).Ticks, vCon);
		}
		else
		{
			nntpCommands.Statistics(0L, DateTime.UtcNow.Subtract(new DateTime(ticks)).Ticks, vCon);
			if (vCon.IsTestConnection)
			{
				nntpCommands.Error.Tries += 100;
			}
			else
			{
				bool flag = HandleError(nntpCommands, vCon);
				if (flag || nntpCommands.Error.Tries <= vCon.Scheduler.Count)
				{
					string message = nntpCommands.Error.Message;
					if (RescheduleCommandsToRunOneMoreTime(vCon, nntpCommands))
					{
						nntpCommands = null;
					}
					if (flag)
					{
						vCon.Enabled = false;
						throw new Exception(message);
					}
				}
			}
		}
		vCon.Token.Token.ThrowIfCancellationRequested();
		if (nntpCommands != null)
		{
			nntpCommands.Progress(nntpCommands.Expected, vCon);
			Process(nntpCommands, vCon);
		}
	}

	private bool RescheduleCommandsToRunOneMoreTime(VirtualConnection vCon, NNTPCommands nntpCommands)
	{
		IndexedCollection indexedCollection = vCon.Scheduler.SwitchStack(nntpCommands.Segment.SlotID, vCon);
		if (indexedCollection != null)
		{
			nntpCommands.Status = WorkStatus.Queued;
			nntpCommands.Error.Code = 0;
			nntpCommands.Error.Message = "";
			indexedCollection.Add(nntpCommands);
			return true;
		}
		return false;
	}

	internal WaitHandle Wait(VirtualConnection vConnection, int lMilliseconds = 100)
	{
		WaitHandle waitHandle = Module.WaitList(new List<WaitHandle>
		{
			vConnection.Idle.WaitHandle,
			vConnection.Token.Token.WaitHandle
		}, lMilliseconds);
		if (waitHandle != null && waitHandle.SafeWaitHandle == vConnection.Idle.WaitHandle.SafeWaitHandle)
		{
			vConnection.Idle.Reset();
		}
		return waitHandle;
	}

	private void Main(VirtualConnection vCon)
	{
		vCon.Enabled = true;
		string text = "";
		try
		{
			Download(vCon);
		}
		catch (Exception ex)
		{
			if (!vCon.Cancelled)
			{
				text = ex.Message;
				vCon.Server.WriteStatus("Error: " + text);
				Log.Error(text);
			}
		}
		if (vCon.Cancelled || Sys.IsShutdownRequested)
		{
			vCon.Server.WriteStatus("Cancelled");
		}
		else
		{
			int num = vCon.Scheduler.Connections.Count();
			double num2 = 1.0;
			if (num > 1)
			{
				num2 = 3.0;
			}
			if (num > 3)
			{
				num2 = 6.0;
			}
			if (text.Contains("please reconnect"))
			{
				num2 = 0.1;
			}
			TimeSpan delay = TimeSpan.FromSeconds((double)Settings.Default.DownloaderRetryIntervalSec * num2);
			Log.Debug("Decrease connections number to {0} for {1} seconds", num - 1, delay.TotalSeconds);
			System.Threading.Tasks.Task.Delay(delay).ContinueWith(delegate
			{
				vCon.Scheduler.Connections.Add(vCon.Server.ID);
				int num3 = vCon.Scheduler.Connections.Count();
				Log.Debug("Restore connections number to " + num3);
				SystemStateChecker.RemoveProblem(SystemStateProblemEnum.HitConnectionsLimit);
			});
		}
		vCon.Enabled = false;
		vCon.Remove();
	}

	private void Process(NNTPCommands zCommand, VirtualConnection vCon)
	{
		try
		{
			if (zCommand.Status == WorkStatus.Failed || zCommand.Status == WorkStatus.Missing)
			{
				zCommand.Data = null;
				zCommand.Segment.LogError(zCommand.ID, zCommand.Error);
			}
			if (!zCommand.Segment.Output.Store(zCommand.ID, zCommand))
			{
				throw new Exception("Store #" + zCommand.ID);
			}
			if (!zCommand.Segment.Output.Finished)
			{
				return;
			}
			zCommand.Segment.IsDecoded = true;
			VirtualSlot virtualSlot = vCon.Scheduler.Slots.Item(zCommand.Segment.SlotID);
			if (virtualSlot == null || !virtualSlot.IsDecoded)
			{
				return;
			}
			bool flag = false;
			List<string> list = new List<string>();
			foreach (VirtualFile item in virtualSlot.List())
			{
				if (item.Output.Data.Length > 0)
				{
					flag = true;
					break;
				}
				list.Add(Module.MostFrequent(item.Errors.GetEnumerator()));
			}
			if (flag)
			{
				virtualSlot.Status = SlotStatus.Completed;
				return;
			}
			virtualSlot.StatusLine = Module.MostFrequent(list.GetEnumerator());
			if (virtualSlot.StatusLine.Length == 0)
			{
				virtualSlot.StatusLine = "No data";
			}
			virtualSlot.Status = SlotStatus.Failed;
		}
		catch (Exception ex)
		{
			string text = "Decode: " + ex.Message;
			Log.Error(text);
			try
			{
				if (!vCon.Cancelled)
				{
					vCon.Server.WriteStatus(text);
				}
			}
			catch
			{
			}
		}
	}

	private static bool HandleError(NNTPCommands commands, VirtualConnection vConnection)
	{
		commands.LogError(commands.Error, vConnection);
		int code = commands.Error.Code;
		commands.Status = WorkStatus.Failed;
		if (commands.Error.Message.ToLower().Contains("connection"))
		{
			switch (code)
			{
			case 381:
			case 400:
			case 450:
			case 452:
			case 480:
			case 481:
			case 482:
			case 502:
			{
				string text = $"{AppHelper.ServersDb.ODown.Connections}/{AppHelper.ServersDb.OUp.Connections}/{AppHelper.ServersDb.OHeader.Connections}";
				string message = "Hit connection limit (" + text + "): " + commands.Error.Message.Trim();
				Log.Warn(message);
				SystemStateChecker.AddProblem(SystemStateProblemEnum.HitConnectionsLimit, Words.ConnectionsMaxNumberReached + " " + Words.ConnectionsLimitHowToSolve + " Server: " + commands.Error.Message.Trim());
				return true;
			}
			}
		}
		switch (code)
		{
		case 205:
		case 400:
		case 437:
			return true;
		case 423:
			commands.Status = WorkStatus.Missing;
			commands.Error.Tries++;
			return false;
		case 430:
			commands.Status = WorkStatus.Missing;
			commands.Error.Tries += 100;
			return false;
		case 2000:
			commands.Error.Tries += 100;
			return false;
		default:
			commands.Error.Tries++;
			return false;
		}
	}

	internal Task Task(VirtualConnection vConnection)
	{
		return new Task(RunMain, vConnection.Token.Token, TaskCreationOptions.LongRunning);
		void RunMain()
		{
			Main(vConnection);
		}
	}
}
