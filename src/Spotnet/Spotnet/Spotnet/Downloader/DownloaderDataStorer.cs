using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader;

public static class DownloaderDataStorer
{
	private static readonly Logger Log;

	private static readonly ConcurrentDictionary<NNTPSegment, MemoryStream> MemoryCache;

	private static readonly AutoResetEvent SegmentSavedEvent;

	private static readonly AutoResetEvent NewSegmentInCacheEvent;

	private static readonly object SaveAllSegmentsFromCacheLock;

	private static readonly ConcurrentDictionary<NNTPInput, KeyValuePair<FileStream, long>> FileStreams;

	private static readonly object LockForWrite;

	private static bool _isCannotCreateShownOnce;

	static DownloaderDataStorer()
	{
		Log = LogManager.GetCurrentClassLogger();
		MemoryCache = new ConcurrentDictionary<NNTPSegment, MemoryStream>();
		SegmentSavedEvent = new AutoResetEvent(initialState: false);
		NewSegmentInCacheEvent = new AutoResetEvent(initialState: false);
		SaveAllSegmentsFromCacheLock = new object();
		FileStreams = new ConcurrentDictionary<NNTPInput, KeyValuePair<FileStream, long>>();
		LockForWrite = new object();
		StartMainSaveCycle();
	}

	public static MemoryStream GetData(NNTPSegment segment)
	{
		if (!segment.IsSaved)
		{
			return null;
		}
		return GetBytesFromTheDisk(segment);
	}

	private static MemoryStream GetBytesFromTheDisk(NNTPSegment segment)
	{
		string text = "none";
		for (int i = 0; i < 3; i++)
		{
			try
			{
				using FileStream fileStream = System.IO.File.Open(segment.File.FullFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				fileStream.Seek(segment.IndentBytes, SeekOrigin.Begin);
				MemoryStream memoryStream = new MemoryStream(segment.ExpectedSize);
				for (int j = 0; j < segment.ExpectedSize; j += fileStream.Read(memoryStream.GetBuffer(), j, segment.ExpectedSize - j))
				{
				}
				return memoryStream;
			}
			catch (IOException ex)
			{
				text = ex.Message;
				Log.Debug($"Failed to read from the disk (try {i + 1})");
				Thread.Sleep(100);
			}
		}
		segment.File.DownloaderItem.LogQueue.Warn("Failed to read segment bytes because of IOException for the file: " + segment.File.FullFilePath + ". Error: " + text);
		return null;
	}

	public static void SaveBytesAsync(NNTPSegment segment, MemoryStream data)
	{
		if (MemoryCache.Count > 50)
		{
			SegmentSavedEvent.WaitOne(1000);
		}
		MemoryCache.TryAdd(segment, data);
		NewSegmentInCacheEvent.Set();
	}

	private static void StartMainSaveCycle()
	{
		Task.Factory.StartNew(delegate
		{
			while (!Sys.IsShutdownRequested)
			{
				try
				{
					NewSegmentInCacheEvent.WaitOne();
					SaveAllSegmentsFromCache();
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					Thread.Sleep(100);
					Log.Debug("Restart data storer");
				}
			}
		}, TaskCreationOptions.LongRunning);
	}

	private static void SaveAllSegmentsFromCache()
	{
		lock (SaveAllSegmentsFromCacheLock)
		{
			if (Sys.IsShutdownRequested)
			{
				return;
			}
			foreach (IGrouping<NNTPInput, KeyValuePair<NNTPSegment, MemoryStream>> item in from s in MemoryCache
				group s by s.Key.File)
			{
				if (Sys.IsShutdownRequested)
				{
					break;
				}
				try
				{
					NNTPInput file = item.Key;
					lock (LockForWrite)
					{
						KeyValuePair<FileStream, long> fileStreamAndPos = GetFileStreamAndPos(file);
						long num = fileStreamAndPos.Value;
						foreach (KeyValuePair<NNTPSegment, MemoryStream> item2 in item.OrderBy((KeyValuePair<NNTPSegment, MemoryStream> p) => p.Key.IndentBytes))
						{
							NNTPSegment key = item2.Key;
							MemoryStream value = item2.Value;
							if (key.IsSaved)
							{
								MemoryCache.TryRemove(key, out value);
								continue;
							}
							try
							{
								if (num != key.IndentBytes)
								{
									fileStreamAndPos.Key.Seek(key.IndentBytes, SeekOrigin.Begin);
								}
								value.Seek(0L, SeekOrigin.Begin);
								value.CopyTo(fileStreamAndPos.Key);
								if (value.Length != key.ExpectedSize)
								{
									Log.Warn($"Something wrong. data.Lenght: {value.Length}. Expected: {key.ExpectedSize}");
								}
								num = fileStreamAndPos.Key.Position;
								key.IsSavedInternal = true;
								MemoryCache.TryRemove(key, out value);
							}
							catch (IOException ex)
							{
								key.LastError = "Disk op exception: " + ex.Message;
								key.RetriesLeft--;
								if (key.RetriesLeft < -1)
								{
									key.MarkAsFailed();
									MemoryCache.TryRemove(key, out value);
								}
								Thread.Sleep(100);
							}
							SegmentSavedEvent.Set();
						}
						FileStreams[file] = new KeyValuePair<FileStream, long>(fileStreamAndPos.Key, num);
					}
					if (file.IsAllSegmentsDataReceived)
					{
						Task.Run(delegate
						{
							if (file.Segments.All((NNTPSegment s) => s.IsSaved || s.IsFailed))
							{
								CloseFileStream(file);
								file.DownloaderItem.LogQueue.Debug("File received: " + file.Filename + ". md5sum: " + file.Md5Sum);
							}
						});
					}
					file.ScheduleSaveTheState();
				}
				catch (Exception ex2)
				{
					Log.Exception(ex2);
					item.Key.DownloaderItem.LogQueue.Fatal("Failed to save segment to the disk: " + ex2.Message + "(" + item.Key.Filename + ")");
				}
			}
		}
	}

	public static void WaitForAllCurrentItemsSave()
	{
		if (MemoryCache.Any())
		{
			SaveAllSegmentsFromCache();
		}
	}

	private static KeyValuePair<FileStream, long> GetFileStreamAndPos(NNTPInput file)
	{
		if (!FileStreams.TryGetValue(file, out var value))
		{
			string text = System.IO.Path.GetDirectoryName(file.FullFilePath).Trim();
			if (!AppHelper.EnsureDirectoryExist(text) && !_isCannotCreateShownOnce)
			{
				_isCannotCreateShownOnce = true;
				AppHelper.Error("Cannot create download directory. Make sure it can be created: " + text);
			}
			string path = System.IO.Path.Combine(text, System.IO.Path.GetFileName(file.FullFilePath));
			value = new KeyValuePair<FileStream, long>(System.IO.File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read | FileShare.Delete), -1L);
			if (FileStreams.TryAdd(file, value))
			{
				value.Key.SetLength(file.Filesize);
			}
		}
		return value;
	}

	public static void CloseFileStream(NNTPInput file)
	{
		lock (LockForWrite)
		{
			if (FileStreams.TryRemove(file, out var value))
			{
				value.Key.Dispose();
			}
		}
	}
}
