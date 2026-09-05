using System.ComponentModel;
using NLog;

namespace Spotnet.DataVirtualization;

public sealed class VirtualListItem<T> : INotifyPropertyChanged
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly VirtualList<T> _list;

	private readonly int _listVersion;

	private bool _isLoaded;

	private ISpotRow _data;

	internal bool IsProcessed { get; set; }

	internal bool TriggerThumbAnimation { get; set; }

	internal bool IsNextPageTriggerItem { private get; set; }

	public ISpotRow Data
	{
		get
		{
			return _data;
		}
		private set
		{
			if (_data != value)
			{
				_data = value;
				OnPropertyChanged(new PropertyChangedEventArgs("Data"));
			}
		}
	}

	internal int Index { get; private set; }

	internal VirtualList<T> List
	{
		get
		{
			if (_list.Version == _listVersion)
			{
				return _list;
			}
			return null;
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

	internal VirtualListItem(VirtualList<T> list, int index)
	{
		_list = list;
		_listVersion = list.Version;
		Index = index;
	}

	internal VirtualListItem(VirtualList<T> list, int index, T data)
		: this(list, index)
	{
		SetData(data);
	}

	public void Load()
	{
		if (IsNextPageTriggerItem)
		{
			List.Load(Index, increasePage: true);
		}
		if (!_isLoaded)
		{
			List.Load(Index);
		}
	}

	private void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		if (this.PropertyChanged != null)
		{
			this.PropertyChanged(this, e);
		}
	}

	internal void SetData(T data)
	{
		if (!_isLoaded)
		{
			Data = (ISpotRow)(object)data;
			_isLoaded = true;
		}
	}
}
