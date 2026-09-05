using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Spotnet.Phuse.NNTP.Net;

internal class IndexedCollection : IndexedObject
{
	private int _idCounter;

	private ConcurrentDictionary<int, IndexedObject> _zCol;

	private bool _zCompletedAdding;

	public int Count => _zCol.Count;

	public bool IsEmpty => _zCol.IsEmpty;

	private bool IsAddingCompleted => _zCompletedAdding;

	public bool IsCompleted
	{
		get
		{
			if (IsEmpty)
			{
				return IsAddingCompleted;
			}
			return false;
		}
	}

	private int Next
	{
		get
		{
			if (_zCol.IsEmpty)
			{
				return -1;
			}
			List<IndexedObject> list = Module.EnumObj(_zCol.Values.GetEnumerator());
			if (list.Count == 0)
			{
				return -1;
			}
			list.Sort();
			return list[0].ID;
		}
	}

	public int ID { get; set; }

	public int Index { get; set; }

	internal IndexedCollection()
	{
		_zCol = new ConcurrentDictionary<int, IndexedObject>();
	}

	internal IndexedCollection(List<IndexedObject> cList)
	{
		CreateCollection(cList.Count);
		if (!Add(cList))
		{
			throw new Exception("Add failed");
		}
	}

	internal IndexedCollection(int Capacity)
	{
		CreateCollection(Capacity);
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	internal void CreateCollection(int capacity)
	{
		_zCol = new ConcurrentDictionary<int, IndexedObject>(1, capacity);
	}

	public void Clear()
	{
		_zCol.Clear();
		Interlocked.Exchange(ref _idCounter, 0);
	}

	internal bool ContainsKey(int ID)
	{
		return _zCol.ContainsKey(ID);
	}

	public bool Remove(int ID = -1)
	{
		if (ID == -1)
		{
			Clear();
			return true;
		}
		IndexedObject value = null;
		while (!_zCol.TryRemove(ID, out value))
		{
			if (!_zCol.ContainsKey(ID))
			{
				return false;
			}
		}
		if (!_zCol.Any())
		{
			Interlocked.Exchange(ref _idCounter, 0);
		}
		return true;
	}

	public IndexedObject Take()
	{
		try
		{
			while (!IsEmpty)
			{
				IndexedObject value = null;
				if (_zCol.TryRemove(Next, out value))
				{
					return value;
				}
			}
			return null;
		}
		finally
		{
			if (!_zCol.Any())
			{
				Interlocked.Exchange(ref _idCounter, 0);
			}
		}
	}

	internal IndexedObject Item(int ID)
	{
		IndexedObject value = null;
		while (_zCol.ContainsKey(ID))
		{
			if (_zCol.TryGetValue(ID, out value))
			{
				return value;
			}
		}
		return null;
	}

	internal List<int> KeyList(int KeyID = -1)
	{
		if (KeyID == -1)
		{
			return Module.EnumInt(_zCol.Keys.GetEnumerator());
		}
		List<int> list = new List<int>();
		if (_zCol.ContainsKey(KeyID))
		{
			list.Add(KeyID);
		}
		return list;
	}

	internal int GetIndex(int ID)
	{
		IndexedObject value = null;
		while (_zCol.ContainsKey(ID))
		{
			if (_zCol.TryGetValue(ID, out value))
			{
				return value.Index;
			}
		}
		return -1;
	}

	internal List<IndexedObject> ObjectList(int ObjectID = -1)
	{
		List<IndexedObject> list = new List<IndexedObject>();
		if (ObjectID == -1)
		{
			foreach (IndexedObject item in Module.EnumObj(_zCol.Values.GetEnumerator()))
			{
				list.Add(item);
			}
			return list;
		}
		IndexedObject indexedObject = Item(ObjectID);
		if (indexedObject != null)
		{
			list.Add(indexedObject);
		}
		return list;
	}

	internal bool Add(IndexedObject cObj)
	{
		return Add(Interlocked.Increment(ref _idCounter), cObj);
	}

	internal bool Add(int id, IndexedObject cObj)
	{
		if (cObj == null)
		{
			return false;
		}
		if (IsAddingCompleted)
		{
			return false;
		}
		cObj.ID = id;
		cObj.Index = id;
		return _zCol.TryAdd(id, cObj);
	}

	internal bool Add(List<IndexedObject> cList)
	{
		if (cList == null)
		{
			return false;
		}
		if (!_zCol.IsEmpty)
		{
			return false;
		}
		foreach (IndexedObject c in cList)
		{
			if (!Add(c))
			{
				return false;
			}
		}
		_zCompletedAdding = true;
		return true;
	}
}
