using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.DataVirtualization;

public class VirtualList<T> : IDisposable, IList, ICollection, IEnumerable, IList<VirtualListItem<T>>, ICollection<VirtualListItem<T>>, IEnumerable<VirtualListItem<T>>, INotifyPropertyChanged, INotifyCollectionChanged
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly NotifyCollectionChangedEventArgs CollectionReset = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);

	private readonly IVirtualListLoader<T> _loader;

	private readonly int _pageSize;

	private readonly int _version;

	private ObservableCollection<VirtualListItem<T>> _list;

	private Task _pendingTask;

	private CancellationTokenSource _cts;

	private Task _pendingNewTask;

	private CancellationTokenSource _ctsNew;

	internal int Version => _version;

	object IList.this[int index]
	{
		get
		{
			return GetItem(index);
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public int Count => _list.Count;

	bool IList.IsReadOnly => true;

	bool IList.IsFixedSize => true;

	public object SyncRoot => this;

	public bool IsSynchronized => false;

	VirtualListItem<T> IList<VirtualListItem<T>>.this[int index]
	{
		get
		{
			return GetItem(index);
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	bool ICollection<VirtualListItem<T>>.IsReadOnly => true;

	private static SpotsListViewModel SpotsListVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).SpotsList;

	private static StatusBarViewModel StatusBarVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).StatusBar;

	event NotifyCollectionChangedEventHandler INotifyCollectionChanged.CollectionChanged
	{
		add
		{
			CollectionChanged += value;
		}
		remove
		{
			CollectionChanged -= value;
		}
	}

	event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
	{
		add
		{
			PropertyChanged += value;
		}
		remove
		{
			PropertyChanged -= value;
		}
	}

	private event PropertyChangedEventHandler PropertyChanged;

	internal event NotifyCollectionChangedEventHandler CollectionChanged;

	public VirtualList(IVirtualListLoader<T> loader, int pageSize)
	{
		if (loader == null)
		{
			throw new ArgumentNullException("loader");
		}
		if (pageSize <= 0)
		{
			throw new ArgumentOutOfRangeException("pageSize");
		}
		_list = new ObservableCollection<VirtualListItem<T>>();
		_version++;
		_loader = loader;
		_pageSize = pageSize;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
	}

	public int IndexOf(object value)
	{
		return IndexOf((VirtualListItem<T>)value);
	}

	public void CopyTo(Array array, int index)
	{
		throw new NotImplementedException();
	}

	void IList.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	public int Add(object value)
	{
		throw new NotImplementedException();
	}

	void IList.Clear()
	{
		throw new NotSupportedException();
	}

	public void Remove(object value)
	{
		throw new NotImplementedException();
	}

	public void Insert(int index, object value)
	{
		throw new NotImplementedException();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return GetItem(i);
		}
	}

	public bool Contains(object value)
	{
		return false;
	}

	public bool Contains(VirtualListItem<T> item)
	{
		return IndexOf(item) != -1;
	}

	public int IndexOf(VirtualListItem<T> item)
	{
		if (item != null && item.List == this)
		{
			return item.Index;
		}
		return -1;
	}

	public void CopyTo(VirtualListItem<T>[] array, int arrayIndex)
	{
		if (array == null)
		{
			throw new ArgumentNullException("array");
		}
		if (arrayIndex < 0)
		{
			throw new ArgumentOutOfRangeException("arrayIndex");
		}
		if (arrayIndex >= array.Length)
		{
			throw new ArgumentException("arrayIndex is greater or equal than the array length");
		}
		if (arrayIndex + Count > array.Length)
		{
			throw new ArgumentException("Number of elements in list is greater than available space");
		}
		foreach (VirtualListItem<T> item in (IEnumerable<VirtualListItem<T>>)this)
		{
			array[arrayIndex++] = item;
		}
	}

	void IList<VirtualListItem<T>>.Insert(int index, VirtualListItem<T> item)
	{
		throw new NotSupportedException();
	}

	void IList<VirtualListItem<T>>.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	void ICollection<VirtualListItem<T>>.Add(VirtualListItem<T> item)
	{
		throw new NotSupportedException();
	}

	void ICollection<VirtualListItem<T>>.Clear()
	{
		throw new NotSupportedException();
	}

	bool ICollection<VirtualListItem<T>>.Remove(VirtualListItem<T> item)
	{
		throw new NotSupportedException();
	}

	IEnumerator<VirtualListItem<T>> IEnumerable<VirtualListItem<T>>.GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return GetItem(i);
		}
	}

	public void Clear()
	{
		_list = new ObservableCollection<VirtualListItem<T>>();
		LoadPage(0);
	}

	private void PopulatePageData(int startIndex, IList<T> pageData, long minRowId, int overallCount, bool isNewQuery, bool isLastPage, CancellationToken cancellationToken)
	{
		if (isNewQuery)
		{
			_list = new ObservableCollection<VirtualListItem<T>>();
		}
		if (overallCount > 0)
		{
			VirtualListItem<T> last = _list.LastOrDefault();
			if (last != null)
			{
				last.IsNextPageTriggerItem = false;
			}
			DispatcherHelper.UIDispatcher.Invoke(delegate
			{
				if (isNewQuery)
				{
					ScrollViewer scrollViewer = SpotsListVm.SpotsContainer.Spots.FindChildByType<ScrollViewer>();
					if (scrollViewer != null && scrollViewer.VerticalOffset > 0.0)
					{
						scrollViewer.ScrollToTop();
						scrollViewer.UpdateLayout();
					}
				}
				for (int i = 0; i < pageData.Count; i++)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						break;
					}
					int num = startIndex + i;
					if (num >= _list.Count || minRowId > -1)
					{
						VirtualListItem<T> virtualListItem = new VirtualListItem<T>(this, num, pageData[i]);
						if (num < _list.Count)
						{
							_list.Insert(num, virtualListItem);
						}
						else
						{
							_list.Add(virtualListItem);
						}
						if (this.CollectionChanged != null)
						{
							this.CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, virtualListItem));
						}
					}
					else
					{
						_list[num].SetData(pageData[i]);
					}
				}
				if (!isLastPage && !cancellationToken.IsCancellationRequested)
				{
					last = _list.LastOrDefault();
					if (last != null)
					{
						last.IsNextPageTriggerItem = true;
						ScrollViewer scrollViewer2 = SpotsListVm.SpotsContainer.Spots.FindChildByType<ScrollViewer>();
						if (scrollViewer2 != null && scrollViewer2.VerticalOffset > 0.0)
						{
							scrollViewer2.UpdateLayout();
						}
					}
				}
			});
		}
		if (isNewQuery && !cancellationToken.IsCancellationRequested)
		{
			DispatcherHelper.UIDispatcher.Invoke(delegate
			{
				FireCollectionReset(null);
			});
		}
	}

	private void FireCollectionReset(object arg)
	{
		OnCollectionReset();
	}

	internal void Load(int index, bool increasePage = false)
	{
		int num = index - index % _pageSize;
		if (increasePage)
		{
			num += _pageSize;
		}
		LoadRangeAsync(num, -1L);
	}

	private void LoadPage(int pageIndex)
	{
		int startIndex = pageIndex * _pageSize;
		LoadRangeAsync(startIndex, -1L);
	}

	internal async Task LoadRangeAsync(int startIndex, long minRowId = -1L)
	{
		CancellationTokenSource newCts = new CancellationTokenSource();
		CancellationTokenSource cancellationTokenSource;
		if (minRowId == -1)
		{
			cancellationTokenSource = _cts;
			_cts = newCts;
		}
		else
		{
			cancellationTokenSource = _ctsNew;
			_ctsNew = newCts;
		}
		Timer timer = null;
		try
		{
			timer = new Timer(delegate
			{
				SpotsListVm.IsSpotsListLoading = true;
			}, null, TimeSpan.FromMilliseconds(500.0), TimeSpan.FromDays(1.0));
			if (cancellationTokenSource != null)
			{
				cancellationTokenSource.Cancel();
				try
				{
					if (minRowId != -1)
					{
						await _pendingNewTask;
					}
					else
					{
						await _pendingTask;
					}
				}
				catch
				{
				}
			}
			if (newCts.Token.IsCancellationRequested)
			{
				return;
			}
			Task task = Task.Run(delegate
			{
				try
				{
					LoadRange(startIndex, newCts.Token, minRowId);
				}
				catch (Exception ex)
				{
					if (!newCts.Token.IsCancellationRequested)
					{
						Log.Exception(ex);
					}
				}
			}, newCts.Token);
			if (minRowId == -1)
			{
				_pendingTask = task;
				await _pendingTask;
			}
			else
			{
				_pendingNewTask = task;
				await _pendingNewTask;
			}
		}
		finally
		{
			if (timer != null)
			{
				timer.Dispose();
				if (!Sys.IsShutdownRequested && !newCts.Token.IsCancellationRequested)
				{
					SpotsListVm.IsSpotsListLoading = false;
				}
			}
		}
	}

	private void LoadRange(int startIndex, CancellationToken cancellationToken, long minRowId = -1L)
	{
		bool isNewQuery = false;
		startIndex -= _pageSize;
		Sys.MainWindow.FirstTabHeaderUpdate();
		if (minRowId > -1)
		{
			_loader.ResetCount();
		}
		IList<T> pageData;
		int overallCount;
		bool isLastPage;
		do
		{
			startIndex += _pageSize;
			pageData = _loader.LoadRange(startIndex, _pageSize, minRowId, out overallCount, out var isNewQuery2, out isLastPage, cancellationToken);
			if (isNewQuery2)
			{
				isNewQuery = true;
			}
		}
		while (!cancellationToken.IsCancellationRequested && !isLastPage && overallCount == startIndex);
		if (cancellationToken.IsCancellationRequested || pageData == null)
		{
			return;
		}
		if (pageData.Count != 0 && SpotsListVm.SpotsContainer.IcStopWait)
		{
			Sys.MainWindow.DoWait(null, blockUi: true);
		}
		if (minRowId > -1 && !Settings.Default.AutoShowNewSpotsInTheList)
		{
			if (pageData.Count > 0)
			{
				SpotsListVm.SetNewSpotsFound(pageData.Count);
			}
			return;
		}
		try
		{
			PopulatePageData(startIndex, pageData, minRowId, overallCount, isNewQuery, isLastPage, cancellationToken);
		}
		finally
		{
			StatusBarVm.SetDefaultSpotsListStatusMessage();
			DispatcherHelper.RunAsync(delegate
			{
				if (!Sys.MainWindow.IsSpotsTabSelectedAndVisible || pageData.Count == 0)
				{
					SpotsListVm.SpotsContainer.IcStopWait = false;
					Sys.MainWindow.StopWait();
				}
			});
		}
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		throw new NotImplementedException();
	}

	private VirtualListItem<T> GetItem(int index)
	{
		if (index > Count - 1)
		{
			return null;
		}
		return _list[index];
	}

	public void Insert(int index, T item)
	{
		throw new NotImplementedException();
	}

	protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		if (this.PropertyChanged != null)
		{
			this.PropertyChanged(this, e);
		}
	}

	protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		if (this.CollectionChanged != null)
		{
			this.CollectionChanged(this, e);
		}
	}

	private void OnCollectionReset()
	{
		OnCollectionChanged(CollectionReset);
	}

	public bool Contains(T item)
	{
		throw new NotImplementedException();
	}
}
