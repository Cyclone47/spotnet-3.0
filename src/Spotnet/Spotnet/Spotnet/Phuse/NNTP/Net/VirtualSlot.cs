using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using NLog;

namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualSlot : VirtualItem, IndexedObject
{
	private readonly IndexedCollection zCol;

	private readonly string zName;

	private readonly Stats zStats;

	private readonly ManualResetEventSlim zWait;

	private int zSlotStatus;

	private string zStatusLine = "";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public string Name => zName;

	public long TotalTime => zStats.TotalTime;

	public ManualResetEventSlim WaitHandle => zWait;

	public string StatusLine
	{
		get
		{
			return zStatusLine;
		}
		set
		{
			zStatusLine = value;
		}
	}

	internal SlotStatus Status
	{
		get
		{
			return (SlotStatus)zSlotStatus;
		}
		set
		{
			if (!History)
			{
				zSlotStatus = (int)value;
				if ((value == SlotStatus.Completed || value == SlotStatus.Failed || value == SlotStatus.Paused) && zWait != null)
				{
					zWait.Set();
				}
			}
		}
	}

	internal bool History
	{
		get
		{
			if (Status != SlotStatus.Failed)
			{
				return Status == SlotStatus.Completed;
			}
			return true;
		}
	}

	internal bool IsCompleted => List().All((VirtualFile current) => current.IsCompleted);

	internal bool IsDecoded => List().All((VirtualFile current) => current.IsDecoded);

	public long Speed => iSpeed(zStats.LastBytes, zStats.LastTime);

	public long SpeedAverage => iSpeed(zStats.TotalBytes, zStats.TotalTime);

	public int ID { get; set; }

	public int Index { get; set; }

	public int Count => zCol.Count;

	public NNTPInfo Info => Module.CountInfo(VirtualList);

	public List<VirtualItem> VirtualList => ((IEnumerable<VirtualItem>)List()).ToList();

	internal VirtualSlot(string sName, IEnumerable<VirtualFile> cList, ManualResetEventSlim waitHandle = null)
	{
		zName = sName;
		zWait = waitHandle;
		zStats = new Stats();
		if (zWait == null)
		{
			zWait = new ManualResetEventSlim();
		}
		zCol = new IndexedCollection(((IEnumerable<IndexedObject>)cList).ToList());
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	public bool WriteXML(XmlWriter xR)
	{
		return ApiXML.Slot(xR, this);
	}

	internal void Progress(long addedBytes)
	{
		zStats.Progress(addedBytes);
	}

	internal List<int> ListId(int fileId = -1)
	{
		return zCol.KeyList(fileId);
	}

	internal VirtualFile Item(int fileId)
	{
		return (VirtualFile)zCol.Item(fileId);
	}

	internal List<VirtualFile> List(int fileId = -1)
	{
		return zCol.ObjectList(fileId).Cast<VirtualFile>().ToList();
	}

	internal void Statistics(long addedBytes, long addedTime)
	{
		zStats.Statistics(addedBytes, addedTime);
	}

	internal string LogGenerate(int fileId = -1)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("<warnings>");
		bool flag = false;
		foreach (VirtualFile item in List(fileId))
		{
			string log = item.Log;
			if (log != null)
			{
				flag = true;
				stringBuilder.AppendLine(log);
			}
		}
		if (!flag)
		{
			return "";
		}
		stringBuilder.AppendLine("</warnings>");
		return stringBuilder.ToString();
	}

	internal bool Remove(int fileId = -1)
	{
		List<VirtualFile> list = List(fileId);
		if (!list.Any())
		{
			return true;
		}
		foreach (VirtualFile item in list)
		{
			item.Remove();
		}
		if (fileId == -1)
		{
			zCol.Clear();
			StatusLine = "Removed";
			Status = SlotStatus.Failed;
			return true;
		}
		return zCol.Remove(fileId);
	}

	internal NNTPCommands Take(VirtualConnection vConnection)
	{
		if (Status != SlotStatus.Downloading)
		{
			return null;
		}
		List<VirtualFile> list = List();
		if (list.Count == 0)
		{
			return null;
		}
		foreach (VirtualFile item in list)
		{
			while (Item(item.ID) != null && !item.IsEmpty)
			{
				item.SlotID = ID;
				object obj = item.Take();
				if (obj != null)
				{
					return (NNTPCommands)obj;
				}
				if (vConnection.Cancelled)
				{
					return null;
				}
			}
		}
		return null;
	}

	private long iSpeed(long lastBytes, long lastTime)
	{
		if (Status != SlotStatus.Downloading)
		{
			return 0L;
		}
		if (lastBytes == 0L || lastTime == 0L)
		{
			return 0L;
		}
		decimal num = lastTime / lastBytes;
		return Convert.ToInt64(Math.Round(10000000m / num, 0));
	}
}
