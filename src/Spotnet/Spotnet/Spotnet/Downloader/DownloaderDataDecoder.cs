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

namespace Spotnet.Downloader;

public static class DownloaderDataDecoder
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

	private static readonly BlockingCollection<KeyValuePair<NNTPSegment, Stream>> SegmentsToDecode;

	static DownloaderDataDecoder()
	{
		Log = LogManager.GetCurrentClassLogger();
		ItemDecodedEvent = new AutoResetEvent(initialState: false);
		SegmentsToDecode = new BlockingCollection<KeyValuePair<NNTPSegment, Stream>>(Math.Max(Environment.ProcessorCount, 2));
		Encoding encoding = Encoding.GetEncoding("iso-8859-1");
		YBeginBytes = encoding.GetBytes("=ybegin ");
		YPartBytes = encoding.GetBytes("=ypart ");
		YEndBytes = encoding.GetBytes("=yend ");
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
				memoryStream = DecodeBinary((MemoryStream)response, segment);
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

	public static MemoryStream DecodeBinary(Stream streamToDecode, NNTPSegment segment = null)
	{
		byte[] array = new byte[streamToDecode.Length];
		int count = 0;
		DecodeStage decodeStage = DecodeStage.FirstLineSkip;
		bool flag = false;
		MemoryStream memoryStream = new MemoryStream((int)streamToDecode.Length);
		bool flag2 = false;
		bool flag3 = false;
		streamToDecode.Position = 3L;
		bool flag4 = true;
		long num = -1L;
		int num2;
		while ((num2 = streamToDecode.ReadByte()) >= 0)
		{
			if (flag3)
			{
				flag3 = false;
				switch (decodeStage)
				{
				case DecodeStage.FirstLineSkip:
					flag4 = false;
					while (num2 == 13 || num2 == 10)
					{
						num2 = streamToDecode.ReadByte();
					}
					num = streamToDecode.Position;
					if (num2 == 61)
					{
						if (StreamStartsWith(streamToDecode, YBeginBytes))
						{
							decodeStage = DecodeStage.YBeginProcessing;
						}
						else
						{
							flag = true;
						}
					}
					else
					{
						flag = true;
					}
					break;
				case DecodeStage.YBeginProcessing:
					if (num2 == 61 && StreamStartsWith(streamToDecode, YPartBytes))
					{
						string @string = Module.GetString(array, 0, count);
						if (segment != null)
						{
							ParseYBeginLine(segment, @string);
						}
						count = 0;
						decodeStage = DecodeStage.YPartProcessing;
						break;
					}
					segment?.File.DownloaderItem.LogQueue.Warn("Bad formed yEnc received, no =ypart");
					return null;
				case DecodeStage.YPartProcessing:
				{
					string string2 = Module.GetString(array, 0, count);
					if (segment != null)
					{
						ParseYPartLine(segment, string2);
					}
					count = 0;
					decodeStage = DecodeStage.YBodyCollecting;
					if (num2.Equals(46))
					{
						num2 = streamToDecode.ReadByte();
						if (num2 < 0)
						{
							flag = true;
						}
						else if (num2 != 46)
						{
							// Not dot-stuffed: put the byte back and keep the single '.' as data.
							streamToDecode.Position--;
							num2 = 46;
						}
					}
					break;
				}
				case DecodeStage.YBodyCollecting:
					switch (num2)
					{
					case 46:
						num2 = streamToDecode.ReadByte();
						if (num2 < 0)
						{
							flag = true;
						}
						else if (num2 != 46)
						{
							// Not dot-stuffed: put the byte back and keep the single '.' as data.
							streamToDecode.Position--;
							num2 = 46;
						}
						break;
					case 61:
						if (StreamStartsWith(streamToDecode, YEndBytes))
						{
							count = 0;
							decodeStage = DecodeStage.YEndProcessing;
						}
						break;
					}
					break;
				case DecodeStage.YEndProcessing:
					while (num2 == 13 || num2 == 10)
					{
						num2 = streamToDecode.ReadByte();
					}
					decodeStage = DecodeStage.YEndProcessed;
					flag = true;
					break;
				}
			}
			if (flag)
			{
				break;
			}
			switch (num2)
			{
			case 13:
				flag2 = true;
				continue;
			case 10:
				if (flag2)
				{
					flag3 = true;
				}
				continue;
			}
			flag2 = false;
			if (decodeStage == DecodeStage.YBodyCollecting)
			{
				if (num2 == 61)
				{
					num2 = streamToDecode.ReadByte();
					if (num2 < 0)
					{
						continue;
					}
					num2 -= 64;
				}
				num2 -= 42;
				memoryStream.WriteByte((byte)num2);
			}
			else if (decodeStage > DecodeStage.FirstLineSkip)
			{
				array[count++] = (byte)num2;
			}
		}
		if (flag4)
		{
			throw new Exception("Bad formed data received, no new line");
		}
		if (decodeStage > DecodeStage.FirstLineSkip && decodeStage != DecodeStage.YEndProcessed)
		{
			throw new Exception("Bad formed yEnc received, no =yend");
		}
		if (decodeStage == DecodeStage.FirstLineSkip)
		{
			memoryStream.Position = 0L;
			streamToDecode.Position = num - 1;
			RemoveDoubleDotAfterNewLine(streamToDecode, memoryStream);
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

	private static bool StreamStartsWith(Stream stream, byte[] bytesToFind)
	{
		byte[] array = new byte[bytesToFind.Length - 1];
		try
		{
			long position = stream.Position;
			stream.Read(array, 0, bytesToFind.Length - 1);
			stream.Position = position;
			bool result = true;
			for (int i = 1; i < bytesToFind.Length; i++)
			{
				if (array[i - 1] != bytesToFind[i])
				{
					result = false;
					break;
				}
			}
			return result;
		}
		catch (Exception)
		{
			return false;
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
