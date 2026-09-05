using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualFile : VirtualItem, IndexedObject
{
	private readonly ConcurrentQueue<string> _errorLog;

	internal ConcurrentQueue<string> Errors;

	private IndexedCollection _col;

	private long _expected;

	private readonly Stats _stats;

	private long _total;

	public string Name { get; private set; }

	internal int Available => _col.Count;

	internal bool IsEmpty => _col.IsEmpty;

	internal NNTPOutput Output { get; private set; }

	internal bool IsCompleted => _col.IsCompleted;

	internal string Log => Module.ReadLog(_errorLog, 50);

	internal bool IsDecoded { get; set; }

	internal int SlotID { get; set; }

	public int ID { get; set; }

	public int Index { get; set; }

	public List<VirtualItem> VirtualList => null;

	public int Count => (int)Interlocked.Read(ref _total);

	public NNTPInfo Info => new NNTPInfo(Available, Interlocked.Read(ref _expected), Interlocked.Read(ref _total), _stats.FakeBytes);

	internal VirtualFile(string sName, List<NNTPSegment> cList)
	{
		Name = sName;
		_stats = new Stats();
		Errors = new ConcurrentQueue<string>();
		_errorLog = new ConcurrentQueue<string>();
		Output = new NNTPOutput(cList.Count, Name);
		if (!Add(cList))
		{
			throw new Exception("Add failed");
		}
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
		return false;
	}

	internal void Progress(long addedBytes)
	{
		_stats.Progress(addedBytes);
	}

	internal bool Remove(int commandId = -1)
	{
		return _col.Remove(commandId);
	}

	internal void Statistics(long addedBytes, long addedTime)
	{
		_stats.Statistics(addedBytes, addedTime);
	}

	internal NNTPCommands Take()
	{
		return (NNTPCommands)_col.Take();
	}

	private List<NNTPCommands> List(int commandId = -1)
	{
		return _col.ObjectList(commandId).Cast<NNTPCommands>().ToList();
	}

	private bool Add(List<NNTPSegment> cList)
	{
		long num = 0L;
		List<IndexedObject> list = new List<IndexedObject>();
		if (cList == null)
		{
			return false;
		}
		if (cList.Count == 0)
		{
			return false;
		}
		Module.Safe32(ref _total, cList.Count);
		cList.Sort();
		foreach (NNTPSegment c in cList)
		{
			if (c.Commands != null)
			{
				list.Add(new NNTPCommands(c.Commands, this));
				continue;
			}
			num += c.ExpectedSize;
			if (c.Command.Length == 0)
			{
				return false;
			}
			list.Add(new NNTPCommands(new List<string> { c.Command }, this, c.ExpectedSize));
		}
		Module.Safe32(ref _expected, num);
		_col = new IndexedCollection(list);
		return true;
	}

	internal void LogError(int commandId, NNTPError zErr)
	{
		Errors.Enqueue(zErr.Message.Replace(Environment.NewLine, ""));
		_errorLog.Enqueue(Module.MakeMsg(Convert.ToString(zErr.Code), "Command #" + Convert.ToString(commandId) + " - Error " + Module.MakeErr(zErr)));
	}
}
