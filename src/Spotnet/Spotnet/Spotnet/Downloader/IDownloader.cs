using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Spotnet.Downloader.ViewModel;

namespace Spotnet.Downloader;

internal interface IDownloader : INotifyPropertyChanged, IDisposable
{
	DownloaderItems Items { get; }

	DownloaderTotals TotalItems { get; }

	Visibility SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility { get; set; }

	bool IsStarted { get; }

	bool IsDownloaderNotAvailable { get; }

	event Action OnDownloaderLoadedFirstTime;

	event Action<object, DownloadStatus> DownloaderStatusChanged;

	event Action ItemsOrderChanged;

	event Action<int> DownloadsProgressChanged;

	bool IsDownloadInQueueAlready(string messageId, out DownloaderItemViewModel item);

	bool IsAnyActiveDownloads();

	void RestartProcessAsync();

	bool AddToDownloadQueue(string pathToNzb, DownloaderItemViewModel item);

	DownloaderItemViewModel AddFakeItemBeforeNzbDownloaded(string sTitle, string messageId, int category);

	Task<bool> StartProcessAsync();

	Task<bool> ShutdownProcessAsync();

	bool CanMoveDown(IEnumerable<DownloaderItemViewModel> items);

	bool CanMoveUp(IEnumerable<DownloaderItemViewModel> items);

	void SetPlayInactiveToAllItems();

	void MoveUp(IEnumerable<DownloaderItemViewModel> items);

	void MoveDown(IEnumerable<DownloaderItemViewModel> items);

	void MoveTop(DownloaderItemViewModel item);

	void RemoveItemsAsync(IEnumerable<DownloaderItemViewModel> items);

	int GetPriority(DownloaderItemViewModel item);

	void UpdateItemsOrder();

	void PauseItemsAsync(IEnumerable<DownloaderItemViewModel> items);

	void ResumeItemsAsync(IEnumerable<DownloaderItemViewModel> items);

	int GetNewHistoryIndex(int oldIndex);

	bool UpdateDownloadSpeedLimit(int kbps);
}
