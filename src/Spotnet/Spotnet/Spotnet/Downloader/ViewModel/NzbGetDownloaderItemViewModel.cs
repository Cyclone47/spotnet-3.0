using NLog;
using Spotnet.Model;

namespace Spotnet.Downloader.ViewModel;

public class NzbGetDownloaderItemViewModel : DownloaderItemViewModel
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public NzbGetDownloaderItemViewModel(int id, string title, DownloadStatus status, int perc, double sizeMegaBytes, int secondsLeft, int index, string incompleteDir, string completeDir, string speed, string messageId, int category, string pathToNzb, long added, long finished)
	{
		if (id < 0)
		{
			id = Sys.Downloader.Items.GetNewId(id);
		}
		Initialize(id, title, status, perc, sizeMegaBytes, secondsLeft, index, incompleteDir, completeDir, speed, messageId, category, pathToNzb, added, finished);
	}
}
