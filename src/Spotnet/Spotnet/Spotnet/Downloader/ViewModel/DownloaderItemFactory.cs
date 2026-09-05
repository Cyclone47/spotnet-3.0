using Spotnet.Properties;

namespace Spotnet.Downloader.ViewModel;

public static class DownloaderItemFactory
{
	public static DownloaderItemViewModel New(string title)
	{
		return New(-1, title, DownloadStatus.Queued, 0, 0.0, 0, -1, "", "", "", "", -1, null, 0L, 0L);
	}

	public static DownloaderItemViewModel New(int id, string title, DownloadStatus status, int perc, double sizeMegaBytes, int secondsLeft, int index, string incompleteDir, string completeDir, string speed, string messageId, int category, string pathToNzb, long added, long finished)
	{
		if (Settings.Default.ExternalNzbGet)
		{
			return new NzbGetDownloaderItemViewModel(id, title, status, perc, sizeMegaBytes, secondsLeft, index, incompleteDir, completeDir, speed, messageId, category, pathToNzb, added, finished);
		}
		return new SpotnetDownloaderItemViewModel(id, title, status, perc, sizeMegaBytes, secondsLeft, index, incompleteDir, completeDir, speed, messageId, category, pathToNzb, added, finished);
	}
}
