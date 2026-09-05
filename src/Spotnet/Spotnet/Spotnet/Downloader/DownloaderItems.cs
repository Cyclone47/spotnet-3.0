using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Spotnet.Mvvm.Threading;
using NLog;
using System.IO;
using Spotnet.Downloader.Controls;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Downloader;

public class DownloaderItems : ObservableCollection<DownloaderItemViewModel>
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly object _lockCollection = new object();

	public ConcurrentDictionary<int, DownloaderItemViewModel> ItemsDict;

	private bool _isMessageIdsClearPerformedOneTime;

	private static readonly DownloaderMessageIDs MessageIDs = new DownloaderMessageIDs();

	public DownloaderItems()
	{
		ItemsDict = new ConcurrentDictionary<int, DownloaderItemViewModel>();
	}

	public void AddToTheList(DownloaderItemViewModel downloaderItem)
	{
		lock (_lockCollection)
		{
			AddItemToTheRightPlace(downloaderItem);
			ItemsDict.TryAdd(downloaderItem.ID, downloaderItem);
			SaveTheState();
		}
	}

	public void RefreshId(int oldId)
	{
		lock (_lockCollection)
		{
			ItemsDict.TryRemove(oldId, out var value);
			ItemsDict.TryAdd(value.ID, value);
		}
	}

	public int GetNewId(int id)
	{
		lock (_lockCollection)
		{
			return ItemsDict.Any() ? (ItemsDict.Keys.Max() + 1) : ((id < 0) ? 1 : id);
		}
	}

	public int GetNewIndex(DownloadStatus status)
	{
		int result = ((status < DownloadStatus.Success) ? 10000 : 50000);
		lock (_lockCollection)
		{
			if (ItemsDict.Any())
			{
				if (status < DownloadStatus.Success)
				{
					result = 10000;
					List<DownloaderItemViewModel> source = ItemsDict.Values.Where((DownloaderItemViewModel i) => !i.IsHistory).ToList();
					if (source.Any())
					{
						result = source.Max((DownloaderItemViewModel i) => i.Index) + 1;
					}
				}
				else
				{
					result = Sys.Downloader.GetNewHistoryIndex(0);
				}
			}
		}
		return result;
	}

	private void AddItemToTheRightPlace(DownloaderItemViewModel item)
	{
		lock (_lockCollection)
		{
			DispatcherHelper.UIDispatcher.Invoke(delegate
			{
				if (Contains(item))
				{
					List<DownloaderItemViewModel> list = this.OrderBy((DownloaderItemViewModel x) => x).ToList();
					int num = IndexOf(item);
					if (list[num] != item)
					{
						Move(num, list.IndexOf(item));
					}
				}
				else
				{
					for (int i = 0; i < base.Count; i++)
					{
						if (item.Index < base[i].Index)
						{
							Insert(i, item);
							return;
						}
					}
					Add(item);
				}
			});
		}
	}

	public bool IsDownloadQueuedAlready(string messageId, out DownloaderItemViewModel item)
	{
		lock (_lockCollection)
		{
			KeyValuePair<int, DownloaderItemViewModel> keyValuePair = ItemsDict.FirstOrDefault((KeyValuePair<int, DownloaderItemViewModel> d) => !d.Value.IsNzbDownload && d.Value.MessageId.EqualsIgnoreCase(messageId));
			bool flag = !keyValuePair.Equals(default(KeyValuePair<int, DownloaderItemViewModel>));
			item = (flag ? keyValuePair.Value : null);
			return flag;
		}
	}

	internal bool RemoveItems(IEnumerable<DownloaderItemViewModel> itemsToRemove, Func<bool> removeFromNzbGetAction = null, bool askToRemoveFiles = true)
	{
		List<DownloaderItemViewModel> list = itemsToRemove.Where((DownloaderItemViewModel i) => i != null).ToList();
		if (!list.Any())
		{
			return true;
		}
		RemoveFilesFromTheDiskDialogAnswerEnum removeFilesFromTheDiskDialogAnswerEnum = RemoveFilesFromTheDiskDialogAnswerEnum.No;
		if (askToRemoveFiles)
		{
			List<string> locationsToRemoveFilesFromDisk = (from i in list
				where i != null && i.IsHistory && !i.CompleteDir.IsNullOrEmpty()
				select i.CompleteDir).ToList();
			removeFilesFromTheDiskDialogAnswerEnum = RunRemoveFilesFromTheDiskDialog(locationsToRemoveFilesFromDisk);
			if (removeFilesFromTheDiskDialogAnswerEnum == RemoveFilesFromTheDiskDialogAnswerEnum.Cancel)
			{
				return true;
			}
		}
		if (Sys.DownloadsPlayer != null && list.Contains(Sys.DownloadsPlayer.ParentDownloaderItem))
		{
			Sys.DownloadsPlayer.PlayerFullStop();
		}
		if (removeFromNzbGetAction != null && !removeFromNzbGetAction())
		{
			return false;
		}
		lock (_lockCollection)
		{
			foreach (DownloaderItemViewModel j in list)
			{
				try
				{
					if (removeFromNzbGetAction == null)
					{
						DispatcherHelper.UIDispatcher.Invoke(() => Remove(j));
						j.Remove();
						ItemsDict.TryRemove(j.ID, out var _);
						SaveTheState();
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					return false;
				}
			}
		}
		if (removeFilesFromTheDiskDialogAnswerEnum == RemoveFilesFromTheDiskDialogAnswerEnum.Yes)
		{
			return RemoveCompleteDirs(list);
		}
		return true;
	}

	private bool RemoveCompleteDirs(List<DownloaderItemViewModel> items)
	{
		foreach (DownloaderItemViewModel item in items)
		{
			try
			{
				if (!item.IsHistory || item.CompleteDir.IsNullOrEmpty())
				{
					continue;
				}
				string completeDir = item.CompleteDir;
				if (!completeDir.IsNullOrEmpty() && Settings.Default.RemoveFilesOnDownloadRemove != 0 && Directory.Exists(completeDir))
				{
					try
					{
						Directory.Delete(completeDir, recursive: true);
					}
					catch (Exception ex)
					{
						Log.Warn(ex.Message);
					}
				}
			}
			catch (Exception ex2)
			{
				Log.Exception(ex2);
				return false;
			}
		}
		return true;
	}

	private RemoveFilesFromTheDiskDialogAnswerEnum RunRemoveFilesFromTheDiskDialog(List<string> locationsToRemoveFilesFromDisk)
	{
		RemoveFilesFromTheDiskDialogAnswerEnum dialogAnswerRemoveFilesFromDisk = ((Settings.Default.RemoveFilesOnDownloadRemove != 1) ? RemoveFilesFromTheDiskDialogAnswerEnum.No : RemoveFilesFromTheDiskDialogAnswerEnum.Yes);
		if (Settings.Default.RemoveFilesOnDownloadRemove == -1 && locationsToRemoveFilesFromDisk.Any((string l) => File.Exists(l) || Directory.Exists(l)))
		{
			DispatcherHelper.RunAsync(delegate
			{
				RemoveFilesFromTheDiskDialog removeFilesFromTheDiskDialog = new RemoveFilesFromTheDiskDialog
				{
					Owner = Sys.MainWindow
				};
				removeFilesFromTheDiskDialog.ShowDialog();
				dialogAnswerRemoveFilesFromDisk = removeFilesFromTheDiskDialog.Answer;
			}).Wait();
		}
		return dialogAnswerRemoveFilesFromDisk;
	}

	internal void SaveTheState()
	{
		try
		{
			if (!Settings.Default.ExternalNzbGet)
			{
				List<string> values = base.Items.Select((DownloaderItemViewModel i) => i.ID.ToString()).ToList();
				string value = string.Join(" ", values);
				AppHelper.SerializeDict(new Dictionary<string, string> { { "IDs", value } }, DownloaderProps.QueueFile);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public void Update(List<NzbGetGroup> nzbgetItems)
	{
		Dictionary<int, NzbGetGroup> dictionary = nzbgetItems.ToDictionary((NzbGetGroup p) => p.NzbId);
		Dictionary<int, NzbGetGroup>.KeyCollection newItemsSet = dictionary.Keys;
		ICollection<int> keys = ItemsDict.Keys;
		List<int> list = newItemsSet.Intersect(keys).ToList();
		IEnumerable<int> enumerable = newItemsSet.Except(keys);
		if (!_isMessageIdsClearPerformedOneTime)
		{
			MessageIDs.RemoveAllExcept(newItemsSet.ToArray());
			_isMessageIdsClearPerformedOneTime = true;
		}
		foreach (int item in enumerable)
		{
			NzbGetGroup nzbGetGroup = dictionary[item];
			MessageIDs.Get(nzbGetGroup.NzbId, out var messageId, out var category);
			int num = (nzbGetGroup.IsHistory ? nzbGetGroup.Priority : 0);
			DownloaderItemFactory.New(nzbGetGroup.NzbId, nzbGetGroup.NzbName, nzbGetGroup.Status, nzbGetGroup.PercentsCompleted, nzbGetGroup.TotalSizeMB, nzbGetGroup.EstimationTime, nzbgetItems.IndexOf(nzbGetGroup), nzbGetGroup.Location, nzbGetGroup.Location, nzbGetGroup.FormattedSpeed, messageId, category, null, 0L, num);
		}
		bool flag = false;
		foreach (int item2 in list)
		{
			lock (_lockCollection)
			{
				DownloaderItemViewModel downloaderItemViewModel = ItemsDict[item2];
				NzbGetGroup nzbGetGroup2 = dictionary[item2];
				int num2 = nzbgetItems.IndexOf(nzbGetGroup2);
				if (!flag)
				{
					flag = downloaderItemViewModel.Index != num2;
				}
				downloaderItemViewModel.Index = num2;
				downloaderItemViewModel.CompleteDir = nzbGetGroup2.Location;
				downloaderItemViewModel.IncompleteDir = nzbGetGroup2.Location;
				if (downloaderItemViewModel.RawStatus != DownloadStatus.Pausing || nzbGetGroup2.Status != DownloadStatus.Downloading)
				{
					downloaderItemViewModel.RawStatus = nzbGetGroup2.Status;
				}
				downloaderItemViewModel.Speed = nzbGetGroup2.FormattedSpeed;
				downloaderItemViewModel.Perc = nzbGetGroup2.PercentsCompleted;
				downloaderItemViewModel.SizeMegaBytes = nzbGetGroup2.TotalSizeMB;
				if (nzbGetGroup2.IsHistory)
				{
					downloaderItemViewModel.Finished = nzbGetGroup2.Priority.ToString();
				}
			}
		}
		List<DownloaderItemViewModel> list2;
		lock (_lockCollection)
		{
			list2 = (from i in ItemsDict
				where !newItemsSet.Contains(i.Key) && !i.Value.IsNzbDownload && i.Value.RawStatus != DownloadStatus.Deleted
				select i.Value).ToList();
		}
		if (list2.Any())
		{
			RemoveItems(list2, null, askToRemoveFiles: false);
		}
		if (flag)
		{
			Sys.Downloader.UpdateItemsOrder();
		}
	}

	public void SetNewMsgIdCatRelation(int id, string messageId, int category)
	{
		MessageIDs.AddOrUpdate(id, messageId, category);
	}
}
