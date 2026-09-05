using System;
using System.Windows.Controls;
using System.Windows.Threading;
using Spotnet.Controls;
using Spotnet.Helpers;

namespace Spotnet.Browser;

internal interface IPage : ICloseableView, IDisposable
{
	Uri Uri { get; }

	TabItem TabItem { get; set; }

	PageTypeEnum PageType { get; }

	PageTypeEnum PageDefaultType { get; }

	string Title { get; }

	bool IsDomReady { get; }

	event Action<object> TitleChangedEvent;

	event Action<object> TypeChangedEvent;

	event Action<object> AddressChangedEvent;

	event Action<object, PageReadyEventArgs> DocumentReadyEvent;

	event Action DocumentUnloadedEvent;

	DispatcherOperation CreateJecAsync(Action action);

	void CreateJecSync(Action action);
}
