using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Spotnet.Downloader;

/// <summary>
/// A list guarded by its own lock, for collections several download threads touch.
/// </summary>
/// <remarks>
/// Replaces <c>System.ServiceModel.SynchronizedCollection&lt;T&gt;</c>, which was the only
/// reason this application referenced WCF at all - there are no services, client or
/// server. That reference does not exist on modern .NET, so removing it takes one item
/// off the migration list.
///
/// One deliberate difference: enumeration hands out a snapshot. The queue iterates these
/// collections while worker threads add to and remove from them, which with the WCF type
/// could throw "collection was modified" from the foreach. A snapshot cannot, and the
/// callers were already treating the traversal as a point-in-time view.
/// </remarks>
internal sealed class SynchronizedList<T> : IList<T>
{
	private readonly List<T> _items = new List<T>();

	private readonly object _syncRoot = new object();

	public int Count
	{
		get
		{
			lock (_syncRoot)
			{
				return _items.Count;
			}
		}
	}

	public bool IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			lock (_syncRoot)
			{
				return _items[index];
			}
		}
		set
		{
			lock (_syncRoot)
			{
				_items[index] = value;
			}
		}
	}

	public void Add(T item)
	{
		lock (_syncRoot)
		{
			_items.Add(item);
		}
	}

	public bool Remove(T item)
	{
		lock (_syncRoot)
		{
			return _items.Remove(item);
		}
	}

	public void RemoveAt(int index)
	{
		lock (_syncRoot)
		{
			_items.RemoveAt(index);
		}
	}

	public void Insert(int index, T item)
	{
		lock (_syncRoot)
		{
			_items.Insert(index, item);
		}
	}

	public void Clear()
	{
		lock (_syncRoot)
		{
			_items.Clear();
		}
	}

	public bool Contains(T item)
	{
		lock (_syncRoot)
		{
			return _items.Contains(item);
		}
	}

	public int IndexOf(T item)
	{
		lock (_syncRoot)
		{
			return _items.IndexOf(item);
		}
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		lock (_syncRoot)
		{
			_items.CopyTo(array, arrayIndex);
		}
	}

	/// <summary>Enumerates a copy, so concurrent writers cannot invalidate the walk.</summary>
	public IEnumerator<T> GetEnumerator()
	{
		List<T> snapshot;
		lock (_syncRoot)
		{
			snapshot = _items.ToList();
		}
		return snapshot.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
