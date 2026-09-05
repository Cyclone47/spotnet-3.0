using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Spotnet.Downloader.ViewModel;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader;

internal class SpotDownloader : IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly List<SpotDownloader> DownloadersList = new List<SpotDownloader>();

	private static readonly object LockDownloadersList = new object();

	private readonly DownloaderItemViewModel _dItem;

	internal SpotDownloader(SpotnetDownloaderItemViewModel dItem)
	{
		_dItem = dItem;
		lock (LockDownloadersList)
		{
			DownloadersList.Add(this);
		}
		if (dItem.FilesToDownload.Any())
		{
			UpdatePriorities();
		}
	}

	public void Dispose()
	{
		lock (LockDownloadersList)
		{
			DownloadersList.Remove(this);
		}
		UpdatePriorities().Wait();
	}

	internal static Task UpdatePriorities(List<NNTPInput> firstPriorityFiles = null)
	{
		return Task.Run(delegate
		{
			lock (LockDownloadersList)
			{
				DownloadQueue.UpdateDownloadQueue(DownloadersList.Select((SpotDownloader d) => d._dItem).ToList(), firstPriorityFiles);
			}
		});
	}

	internal static void ClearDownloadersList()
	{
		lock (LockDownloadersList)
		{
			DownloadersList.Clear();
		}
	}
}
