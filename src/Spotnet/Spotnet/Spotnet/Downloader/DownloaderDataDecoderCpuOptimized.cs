using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;
using Spotnet.Phuse.NNTP.Net;
using SpotnetEnc;

namespace Spotnet.Downloader;

public static class DownloaderDataDecoderCpuOptimized
{
	public enum DecodeStage
	{
		FirstLineSkip,
		YBeginProcessing,
		YPartProcessing,
		YBodyCollecting,
		YEndProcessing,
		YEndProcessed
	}

	private const int MaxSegmentSizeAllowedBytes = 1073741824;

	private const long MaxFileSizeAllowedBytes = 107374182400L;

	private const byte DotByte = 46;

	private const byte YEqByte = 61;

	private const byte NewLineByte1 = 13;

	private const byte NewLineByte2 = 10;

	private static readonly Logger Log;

	private static readonly byte[] YBeginBytes;

	private static readonly byte[] YPartBytes;

	private static readonly byte[] YEndBytes;

	public static AutoResetEvent ItemDecodedEvent;

	private static readonly SpotnetDecoder SpotnetDecoder;

	private static readonly BlockingCollection<KeyValuePair<NNTPSegment, Stream>> SegmentsToDecode;

	static DownloaderDataDecoderCpuOptimized()
	{
		Log = LogManager.GetCurrentClassLogger();
		ItemDecodedEvent = new AutoResetEvent(initialState: false);
		SegmentsToDecode = new BlockingCollection<KeyValuePair<NNTPSegment, Stream>>(Math.Max(Environment.ProcessorCount, 2));
		Encoding encoding = Encoding.GetEncoding("iso-8859-1");
		YBeginBytes = encoding.GetBytes("=ybegin ");
		YPartBytes = encoding.GetBytes("=ypart ");
		YEndBytes = encoding.GetBytes("=yend ");
		SpotnetDecoder = new SpotnetDecoder();
		SpotnetDecoder.Init();
		StartDecodeThreadsAsync();
	}

	private static void StartDecodeThreadsAsync()
	{
		for (int i = 0; i < SegmentsToDecode.BoundedCapacity; i++)
		{
			Task.Factory.StartNew(DecodeOneThreadBody, TaskCreationOptions.LongRunning);
		}
	}

	private static void DecodeOneThreadBody()
	{
		try
		{
			while (true)
			{
				KeyValuePair<NNTPSegment, Stream> keyValuePair = SegmentsToDecode.Take();
				NNTPSegment key = keyValuePair.Key;
				Stream value = keyValuePair.Value;
				try
				{
					Decode(key, value);
				}
				catch (Exception ex)
				{
					key.LastError = "Failed to decode segment: " + ex.Message;
				}
				finally
				{
					if (!key.IsDataAvailable && !key.IsSaved)
					{
						DownloadQueue.PutSegmentBackToDownload(key);
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex4)
		{
			Log.Exception(ex4);
		}
		Log.Debug("Decode thread END");
	}

	public static void DecodeAsync(NNTPSegment segment, Stream response)
	{
		SegmentsToDecode.Add(new KeyValuePair<NNTPSegment, Stream>(segment, response));
	}

	private static void Decode(NNTPSegment segment, Stream response)
	{
		try
		{
			if (segment.IsDataAvailable || segment.File.DownloaderItem.RawStatus == DownloadStatus.Deleted || segment.File.IsDisposed)
			{
				return;
			}
			MemoryStream memoryStream;
			try
			{
				memoryStream = NewDecodeBinary((MemoryStream)response, segment);
			}
			catch (Exception ex)
			{
				segment.LastError = "Failed to decode " + segment.MessageId + " segment: " + ex.Message + ". Try to download one more time.";
				segment.IsDownloaded = false;
				return;
			}
			try
			{
				segment.ExpectedSize = (int)memoryStream.Length;
				if (segment.ExpectedSize > 1073741824)
				{
					segment.MarkAsFailed($"Segment {segment.MessageId} max size reached: {segment.ExpectedSize}");
					return;
				}
				if (segment.File.Filesize > 107374182400L)
				{
					segment.MarkAsFailed($"File {segment.File.Filename} max size reached: {segment.File.Filesize}");
					return;
				}
				DownloaderDataStorer.SaveBytesAsync(segment, memoryStream);
				segment.IsDataAvailable = true;
			}
			catch (Exception ex2)
			{
				Log.Exception(ex2);
				segment.MarkAsFailed("Failed to save " + segment.MessageId + " segment: " + ex2.Message);
			}
		}
		finally
		{
			ItemDecodedEvent.Set();
		}
	}

	public static void ProcessYEncHeaderAndFooter(MemoryStream streamToDecode, out int start, out int length, out bool noEnc, NNTPSegment segment = null)
	{
		start = 0;
		length = 0;
		byte[] buffer = streamToDecode.GetBuffer();
		int num = IndexOfPattern(buffer, YPartBytes, 0, 1000);
		int num2 = Array.IndexOf(buffer, (byte)10, num + 7, 1000) + 1;
		int num3 = SearchIndexFromTheEnd(buffer, YEndBytes) - 2;
		if (num2 > 0 && num3 - num2 > 0)
		{
			noEnc = false;
			start = num2;
			length = num3 - num2;
		}
		else
		{
			noEnc = true;
		}
	}

	private static int SearchIndexFromTheEnd(byte[] inputArr, byte[] searchArr)
	{
		int num = inputArr.Length;
		int num2 = searchArr.Length;
		int num3 = 0;
		for (int num4 = num - 1; num4 >= 0; num4--)
		{
			if (inputArr[num4] == searchArr[num2 - num3 - 1])
			{
				num3++;
				if (num3 == num2)
				{
					return num4;
				}
			}
			else
			{
				num3 = 0;
			}
		}
		return -1;
	}

	public static int IndexOfPattern<T>(T[] array, T[] pattern, int startIndex, int count)
	{
		int fidx = 0;
		int num = Array.FindIndex(array, startIndex, count, delegate(T item)
		{
			fidx = (item.Equals(pattern[fidx]) ? (fidx + 1) : 0);
			return fidx == pattern.Length;
		});
		if (num >= 0)
		{
			return num - fidx + 1;
		}
		return -1;
	}

	public static int IndexOfPatternLast<T>(T[] array, T[] pattern, int startIndex, int count)
	{
		int fidx = pattern.Length - 1;
		int num = Array.FindLastIndex(array, startIndex, count, delegate(T item)
		{
			fidx = (item.Equals(pattern[fidx]) ? (fidx - 1) : (pattern.Length - 1));
			return fidx == -1;
		});
		if (num >= 0)
		{
			return num;
		}
		return -1;
	}

	public static MemoryStream NewDecodeBinary(MemoryStream streamToDecode, NNTPSegment segment = null)
	{
		int start = 0;
		int num = 0;
		bool flag = true;
		int num2 = -1;
		int num3 = -1;
		byte[] buffer = streamToDecode.GetBuffer();
		int num4 = Math.Min(buffer.Length - 1, 1000);
		int num5 = Array.IndexOf(buffer, (byte)10, 0, num4) + 1;
		if (num5 < 1)
		{
			throw new Exception("Bad formed data received, no new line");
		}
		int num6 = IndexOfPattern(buffer, YBeginBytes, num5, num4 - num5);
		if (num6 > 0)
		{
			int num7 = num6 + 7;
			num2 = IndexOfPattern(buffer, YPartBytes, num7, num4 - num7);
			num7 = ((num2 > 0) ? num2 : num6) + 7;
			num3 = Array.IndexOf(buffer, (byte)10, num7, num4 - num7) + 1;
			if (num3 < 0)
			{
				throw new Exception("Bad formed yEnc received, no new line after =ypart");
			}
			int num8 = SearchIndexFromTheEnd(buffer, YEndBytes) - 2;
			if (num8 < 0)
			{
				throw new Exception("Bad formed yEnc received, no =yend");
			}
			if (num8 - num3 > 0)
			{
				flag = false;
				start = num3;
				num = num8 - num3;
			}
		}
		MemoryStream memoryStream;
		if (flag || num == 0)
		{
			memoryStream = new MemoryStream((int)streamToDecode.Length);
			streamToDecode.Position = num5;
			RemoveDoubleDotAfterNewLine(streamToDecode, memoryStream);
		}
		else
		{
			if (segment != null)
			{
				int num9 = ((num2 > 0) ? num2 : num3) - 2;
				string @string = Module.GetString(buffer, num6, num9 - num6);
				ParseYBeginLine(segment, @string);
				if (num2 > 0)
				{
					string string2 = Module.GetString(buffer, num2, num3 - 2 - num2);
					ParseYPartLine(segment, string2);
				}
			}
			byte[] array = new byte[num];
			uint count = SpotnetDecoder.Decode(buffer, array, start, (uint)num);
			memoryStream = new MemoryStream(array, 0, (int)count);
		}
		if (memoryStream.Length == 0L)
		{
			throw new Exception("Body is empty");
		}
		return memoryStream;
	}

	public static void RemoveDoubleDotAfterNewLine(Stream streamToDecode, Stream result)
	{
		int num;
		while ((num = streamToDecode.ReadByte()) > -1)
		{
			if (num == 13)
			{
				result.WriteByte((byte)num);
				num = streamToDecode.ReadByte();
				if (num == 10)
				{
					result.WriteByte((byte)num);
					num = streamToDecode.ReadByte();
					if (num == 46)
					{
						result.WriteByte((byte)num);
						num = streamToDecode.ReadByte();
						if (num == 46)
						{
							num = streamToDecode.ReadByte();
						}
					}
				}
				if (num < 0)
				{
					break;
				}
			}
			result.WriteByte((byte)num);
		}
	}

	private static void ParseYPartLine(NNTPSegment segment, string line)
	{
		int num = line.IndexOf("begin=", StringComparison.Ordinal) + 6;
		if (num > 6)
		{
			long num2 = ParseNumberFromTheLineIndex(line, num);
			if (num2 > -1)
			{
				segment.IndentBytes = num2 - 1;
			}
		}
	}

	private static long ParseNumberFromTheLineIndex(string line, int idx)
	{
		string text = "";
		while (idx < line.Length && char.IsDigit(line[idx]))
		{
			text += line[idx++];
		}
		if (long.TryParse(text, out var result))
		{
			return result;
		}
		return -1L;
	}

	private static void ParseYBeginLine(NNTPSegment segment, string line)
	{
		line = line.Trim();
		int num = line.IndexOf("size=", StringComparison.Ordinal) + 5;
		if (num > 5)
		{
			long num2 = ParseNumberFromTheLineIndex(line, num);
			if (num2 > -1)
			{
				segment.File.Filesize = num2;
			}
		}
	}

	public static void WaitForDecodeTasksToComplete(List<NNTPInput> filesToDownload)
	{
		foreach (NNTPSegment item in filesToDownload.SelectMany((NNTPInput f) => f.Segments))
		{
			while (!item.IsDataAvailable && !item.IsFailed)
			{
				Thread.Sleep(50);
			}
		}
	}
}
