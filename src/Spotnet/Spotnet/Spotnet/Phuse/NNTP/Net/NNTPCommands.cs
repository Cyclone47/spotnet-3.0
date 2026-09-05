using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Spotnet.Phuse.NNTP.Net;

internal class NNTPCommands : IndexedObject
{
	private readonly List<string> _zCommands;

	private readonly VirtualFile _zSeg;

	private int _commandIndex = -1;

	private int _zStatus;

	internal bool Finished
	{
		get
		{
			if (_zCommands != null && _zCommands.Count != 0)
			{
				return _commandIndex >= _zCommands.Count - 1;
			}
			return true;
		}
	}

	internal string Next
	{
		get
		{
			if (Finished)
			{
				return "";
			}
			_commandIndex++;
			return _zCommands[_commandIndex];
		}
	}

	internal string Current
	{
		get
		{
			if (_commandIndex < 0)
			{
				return "";
			}
			if (_commandIndex >= _zCommands.Count)
			{
				return "";
			}
			return _zCommands[_commandIndex];
		}
	}

	internal string Last
	{
		get
		{
			int count = _zCommands.Count;
			if (count != 0)
			{
				return _zCommands[count - 1];
			}
			return "";
		}
	}

	public int Expected { get; }

	internal NNTPError Error { get; } = new NNTPError();


	internal VirtualFile Segment => _zSeg;

	internal Stream Data { get; set; }

	internal PhuseFileInfo File { get; set; }

	internal PartInfo Part { get; set; }

	internal WorkStatus Status
	{
		get
		{
			return (WorkStatus)_zStatus;
		}
		set
		{
			_zStatus = (int)value;
		}
	}

	public int ID { get; set; }

	public int Index { get; set; }

	internal NNTPCommands(List<string> commands, VirtualFile segment, int expectedSize = 0)
	{
		_zSeg = segment;
		_zCommands = commands;
		Expected = expectedSize;
		_zStatus = 0;
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	internal void Reset()
	{
		_commandIndex = -1;
	}

	internal void LogError(NNTPError zErr, VirtualConnection vConnection)
	{
		_zSeg?.LogError(ID, zErr);
		vConnection?.LogError(ID, zErr);
	}

	internal void Statistics(long addedBytes, long realTime, VirtualConnection vConnection)
	{
		if (_zSeg != null)
		{
			long addedTime = Interlocked.Read(ref realTime);
			long addedBytes2 = Interlocked.Read(ref addedBytes);
			_zSeg.Statistics(addedBytes2, addedTime);
			vConnection.Scheduler.Slots.Item(_zSeg.SlotID)?.Statistics(addedBytes2, addedTime);
		}
	}

	internal void Progress(long addedBytes, VirtualConnection vConnection)
	{
		if (_zSeg != null)
		{
			long addedBytes2 = Interlocked.Read(ref addedBytes);
			_zSeg.Progress(addedBytes2);
			vConnection.Scheduler.Slots.Item(_zSeg.SlotID)?.Progress(addedBytes2);
		}
	}

	public bool HaveTestConnectionMark()
	{
		return _zCommands.Contains("START TEST CONNECTION");
	}

	public void RemoveTestConnectionMark()
	{
		_zCommands.Remove("START TEST CONNECTION");
	}
}
