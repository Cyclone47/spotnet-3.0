using System.IO;
using System.Threading;

namespace Spotnet.Phuse.NNTP.Net;

internal class NNTPOutput
{
	private readonly IndexedCollection _commandsList;

	private int _index = 1;

	private int _offset;

	internal int Total { get; }

	internal Stream Data { get; private set; }

	internal bool Finished { get; private set; }

	internal NNTPOutput(int lTotal, string sFile)
	{
		Total = lTotal;
		Finished = false;
		Data = new MemoryStream();
		_commandsList = new IndexedCollection(Total);
	}

	internal bool Store(int lIndex, NNTPCommands nCom)
	{
		if (lIndex != _index)
		{
			return Queue(lIndex, nCom);
		}
		bool flag = Write(nCom);
		Interlocked.Increment(ref _index);
		if (flag)
		{
			while (_commandsList.ContainsKey(_index))
			{
				NNTPCommands nNTPCommands = (NNTPCommands)_commandsList.Take();
				if (nNTPCommands == null || nNTPCommands.ID != _index || !Write(nNTPCommands))
				{
					break;
				}
				Interlocked.Increment(ref _index);
			}
		}
		if (Total == _index - 1)
		{
			Finished = true;
		}
		return flag;
	}

	private bool Write(NNTPCommands nCom)
	{
		if (nCom == null)
		{
			return false;
		}
		if (nCom.Data == null)
		{
			return true;
		}
		if (nCom.Status != WorkStatus.Completed)
		{
			return true;
		}
		if (nCom.Part != null)
		{
			if (_offset > nCom.Part.Begin - 1)
			{
				return false;
			}
			if (_offset < nCom.Part.Begin - 1)
			{
				byte[] array = new byte[nCom.Part.Begin - 1 - _offset];
				Data.Write(array, 0, array.Length);
				Interlocked.Add(ref _offset, array.Length);
			}
			if (_offset != nCom.Part.Begin - 1)
			{
				return false;
			}
		}
		if (nCom.Segment.Name.Equals("D"))
		{
			Data = nCom.Data;
		}
		else
		{
			nCom.Data.Position = 0L;
			nCom.Data.CopyTo(Data);
		}
		Interlocked.Add(ref _offset, (int)nCom.Data.Length);
		return true;
	}

	private bool Queue(int lIndex, NNTPCommands nCom)
	{
		if (!_commandsList.ContainsKey(lIndex))
		{
			return _commandsList.Add(lIndex, nCom);
		}
		return false;
	}

	public void Dispose()
	{
		Data.Dispose();
		Data = new MemoryStream();
		_commandsList.Clear();
	}
}
