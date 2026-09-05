using System.Windows.Controls.Primitives;
using Spotnet.DataVirtualization;
using Spotnet.ViewModel;

namespace Spotnet.Controls;

public interface ISpotsContainer
{
	Selector Spots { get; }

	bool IcStopWait { get; set; }

	bool IsSpotKeyboardFocused { get; }

	SpotRowViewModel SelectedSpot { get; }

	void SaveCols();

	void UpdateContainer();

	void RefreshAllItemsStyle();

	void UpdateItemStyle(ISpotRow row);

	void RestoreFocus();

	void LoadContentForTheFirstTime();

	void SaveScrollPosition();

	void RestoreScrollPosition();
}
