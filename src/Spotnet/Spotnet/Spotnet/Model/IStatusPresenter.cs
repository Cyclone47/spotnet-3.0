using System.ComponentModel;

namespace Spotnet.Model;

internal interface IStatusPresenter
{
	bool IsItBlockerPresenter { get; }

	void OnStatusChanged(object sender, StatusChangedEventArgs status);

	void OnTaskCompleted(object sender, AsyncCompletedEventArgs results);
}
