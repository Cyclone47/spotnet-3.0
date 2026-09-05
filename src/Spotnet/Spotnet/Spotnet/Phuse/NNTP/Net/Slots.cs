using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

namespace Spotnet.Phuse.NNTP.Net;

internal class Slots : VirtualItem
{
	private readonly IndexedCollection zCol;

	internal ConcurrentQueue<string> Log;

	private long zUptime;

	internal string XML => Module.XmlToString(this);

	internal TimeSpan Uptime => DateTime.UtcNow.Subtract(new DateTime(Interlocked.Read(ref zUptime)));

	internal string Status
	{
		get
		{
			if (Paused)
			{
				return "Paused";
			}
			if (Active)
			{
				return "Active";
			}
			return "Idle";
		}
	}

	internal bool Paused
	{
		get
		{
			int count = ListStatus(SlotStatus.Paused).Count;
			if (Count == 0)
			{
				return false;
			}
			if (count == 0)
			{
				return false;
			}
			return List().All((VirtualSlot current) => current.History || current.Status == SlotStatus.Paused);
		}
	}

	internal bool Active
	{
		get
		{
			if (Count == 0)
			{
				return false;
			}
			return List().Any((VirtualSlot current) => !current.History && current.Status != SlotStatus.Paused);
		}
	}

	public long Speed => iSpeed(average: false);

	public long SpeedAverage => iSpeed(average: true);

	public long TotalTime => ListStatus(SlotStatus.Downloading).Sum((VirtualSlot current) => current.TotalTime);

	public int Count => zCol.Count;

	public NNTPInfo Info => Module.CountInfo(VirtualList);

	public List<VirtualItem> VirtualList => ((IEnumerable<VirtualItem>)List()).ToList();

	internal Slots()
	{
		zUptime = DateTime.UtcNow.Ticks;
		zCol = new IndexedCollection();
		Log = new ConcurrentQueue<string>();
	}

	public bool WriteXML(XmlWriter xR)
	{
		if (Count != 0)
		{
			return ApiXML.Slots(xR, this);
		}
		return false;
	}

	internal bool ContainsKey(int slotId)
	{
		return zCol.ContainsKey(slotId);
	}

	internal List<int> ListId(int slotId = -1)
	{
		return zCol.KeyList(slotId);
	}

	internal VirtualSlot Item(int slotId)
	{
		return (VirtualSlot)zCol.Item(slotId);
	}

	internal List<VirtualSlot> List(int slotId = -1)
	{
		return zCol.ObjectList(slotId).Cast<VirtualSlot>().ToList();
	}

	internal List<VirtualSlot> ListStatus(SlotStatus cStatus)
	{
		return (from current in List()
			where current.Status == cStatus
			select current).ToList();
	}

	internal bool Remove(int slotId = -1)
	{
		List<VirtualSlot> list = List(slotId);
		if (list.Count == 0)
		{
			return true;
		}
		foreach (VirtualSlot item in list)
		{
			item.Remove();
		}
		if (slotId == -1)
		{
			zCol.Clear();
			return true;
		}
		zCol.Remove(slotId);
		return true;
	}

	internal VirtualSlot Add(string name, List<NNTPInput> cList, CancellationToken vToken, ManualResetEventSlim vWait)
	{
		if (cList == null)
		{
			return null;
		}
		if (name == null)
		{
			return null;
		}
		if (cList.Count == 0)
		{
			return null;
		}
		List<VirtualFile> list = (from current in cList
			let segments = current.Segments
			where segments != null && segments.Count != 0
			select new VirtualFile(current.Subject, segments)).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		if (vToken.IsCancellationRequested)
		{
			return null;
		}
		VirtualSlot virtualSlot = new VirtualSlot(name, list, vWait);
		if (!zCol.Add(virtualSlot))
		{
			return null;
		}
		return virtualSlot;
	}

	private long iSpeed(bool average)
	{
		long num = 0L;
		foreach (VirtualSlot item in ListStatus(SlotStatus.Downloading))
		{
			num = (average ? (num + item.SpeedAverage) : (num + item.Speed));
		}
		return num;
	}
}
