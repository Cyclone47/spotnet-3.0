using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Spotnet.Extensions;

namespace Spotnet.Phuse.NNTP.Net;

public class SlotList : IApi
{
	private readonly Scheduler _zServers;

	public string Xml
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.Slots.XML;
			}
		}
	}

	public int Count
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.Slots.Count;
			}
		}
	}

	public List<int> Items
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.Slots.ListId();
			}
		}
	}

	internal SlotList(Scheduler lServers)
	{
		_zServers = lServers;
	}

	public bool Remove(int id)
	{
		lock (_zServers)
		{
			return _zServers.Slots.Remove(id);
		}
	}

	public string Send(string newsgroup, List<string> command, CancellationTokenSource vToken = null)
	{
		return Module.GetString(SendAndGetStream(newsgroup, command, vToken), 0L, -1L);
	}

	public Stream SendAndGetStream(string newsgroup, List<string> command, CancellationTokenSource vToken = null, bool isDownloaderBody = false)
	{
		if (newsgroup == null)
		{
			throw new Exception("No group");
		}
		if (command == null)
		{
			throw new Exception("No command");
		}
		if (_zServers == null)
		{
			throw new Exception("Cancelled");
		}
		if (_zServers.Count == 0)
		{
			throw new Exception("No server");
		}
		List<string> list = new List<string> { "GROUP " + newsgroup.ToLower() };
		list.AddRange(command);
		NNTPInput nNTPInput = new NNTPInput(null, isDownloaderBody ? "D" : "");
		NNTPSegment nNTPSegment = new NNTPSegment(1, 0, null, nNTPInput);
		List<NNTPInput> list2 = new List<NNTPInput>();
		nNTPSegment.Commands = list;
		nNTPInput.Segments.Add(nNTPSegment);
		list2.Add(nNTPInput);
		ManualResetEventSlim wHandle = new ManualResetEventSlim(initialState: false);
		if (vToken == null)
		{
			vToken = new CancellationTokenSource();
		}
		VirtualSlot virtualSlot = InternalAdd(nNTPInput.Subject, _zServers.Slots, list2, vToken.Token, wHandle);
		if (virtualSlot == null)
		{
			throw new Exception("No slot");
		}
		if (vToken.Token.IsCancellationRequested)
		{
			throw new Exception("Cancelled");
		}
		List<WaitHandle> obj = new List<WaitHandle> { vToken.Token.WaitHandle };
		if (virtualSlot.WaitHandle == null)
		{
			throw new Exception("No waithandle");
		}
		obj.Add(virtualSlot.WaitHandle.WaitHandle);
		virtualSlot.Status = SlotStatus.Downloading;
		Notify();
		WaitHandle waitHandle = Module.WaitList(obj);
		if (waitHandle == null || waitHandle.SafeWaitHandle == vToken.Token.WaitHandle.SafeWaitHandle)
		{
			throw new Exception("Cancelled");
		}
		if (vToken.Token.IsCancellationRequested)
		{
			throw new Exception("Cancelled");
		}
		Stream result = null;
		if (virtualSlot.Status != SlotStatus.Completed)
		{
			string text = ((virtualSlot.Status == SlotStatus.Failed) ? virtualSlot.StatusLine : Module.TranslateStatus((int)virtualSlot.Status));
			int iD = virtualSlot.ID;
			Remove(iD);
			if (text.IsNullOrEmpty())
			{
				text = "Unknown";
			}
			throw new Exception(text);
		}
		List<VirtualFile> list3 = virtualSlot.List();
		list3.Reverse();
		foreach (VirtualFile item in list3)
		{
			if (item.Output?.Data != null && item.Output.Data.Length != 0L)
			{
				item.Output.Data.Position = 0L;
				result = item.Output.Data;
				break;
			}
		}
		int iD2 = virtualSlot.ID;
		Remove(iD2);
		return result;
	}

	private VirtualSlot InternalAdd(string name, Slots zSlots, List<NNTPInput> cList, CancellationToken vToken, ManualResetEventSlim wHandle = null)
	{
		try
		{
			lock (_zServers)
			{
				return zSlots.Add(name, cList, vToken, wHandle);
			}
		}
		catch
		{
		}
		return null;
	}

	private void Notify()
	{
		try
		{
			lock (_zServers)
			{
				foreach (VirtualConnection item in _zServers.Connections.List())
				{
					item.Idle.Set();
				}
			}
		}
		catch
		{
		}
	}
}
