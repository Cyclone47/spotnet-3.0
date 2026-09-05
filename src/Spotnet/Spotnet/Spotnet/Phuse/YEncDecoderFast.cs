using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NLog;
using Spotnet.Downloader;
using Spotnet.Helpers;
using Spotnet.Model;

namespace Spotnet.Phuse;

public class YEncDecoderFast
{
	private enum DecoderTypeEnum
	{
		Default,
		DsimOptimized
	}

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public void DecodeTest()
	{
		string tempFileName = AppHelper.GetTempFileName("decoded", "result_Default");
		string tempFileName2 = AppHelper.GetTempFileName("decoded", "result_DsimOptimized");
		if (!System.IO.File.Exists(tempFileName))
		{
			return;
		}
		System.IO.File.WriteAllText(tempFileName, "");
		System.IO.File.WriteAllText(tempFileName2, "");
		Dictionary<DecoderTypeEnum, long> dictionary = new Dictionary<DecoderTypeEnum, long>
		{
			[DecoderTypeEnum.Default] = 0L,
			[DecoderTypeEnum.DsimOptimized] = 0L
		};
		long num = 0L;
		for (int i = 0; i < 100; i++)
		{
			using MemoryStream memoryStream = new MemoryStream();
			using (FileStream fileStream = System.IO.File.OpenRead(AppHelper.GetTempFileName("enc", $"test{i + 1}")))
			{
				fileStream.CopyTo(memoryStream);
			}
			Log.Debug($"Processing segment #{i + 1}");
			Tracker tracker = new Tracker();
			foreach (DecoderTypeEnum item in Enum.GetValues(typeof(DecoderTypeEnum)).Cast<DecoderTypeEnum>())
			{
				tracker.Restart();
				long msHeaders;
				using Stream stream = DecodeSegment(memoryStream, item, out msHeaders);
				if (i > 1)
				{
					dictionary[item] += tracker.ElapsedMilliseconds;
					num += msHeaders;
				}
				string tempFileName3 = AppHelper.GetTempFileName("decoded", $"result_{item}");
				stream?.Seek(0L, SeekOrigin.Begin);
				using FileStream destination = System.IO.File.Open(tempFileName3, FileMode.Append, FileAccess.Write);
				stream?.CopyTo(destination);
			}
		}
		Log.Debug($"Default decode time(ms): {dictionary[DecoderTypeEnum.Default]}");
		Log.Debug($"Headers processing time(ms): {num}");
		Log.Debug($"DsimOptimized decode time(ms): {dictionary[DecoderTypeEnum.DsimOptimized]}");
		using (MD5 mD = MD5.Create())
		{
			using FileStream inputStream = System.IO.File.OpenRead(tempFileName);
			Log.Debug("f1 md5:" + AppHelper.MakeMd5(mD.ComputeHash(inputStream)));
		}
		using MD5 mD2 = MD5.Create();
		using FileStream inputStream2 = System.IO.File.OpenRead(tempFileName2);
		Log.Debug("f2 md5:" + AppHelper.MakeMd5(mD2.ComputeHash(inputStream2)));
	}

	private Stream DownloadSegment(string group, string messageId)
	{
		if (!new Spotnet.Model.NNTP(AppHelper.HeaderPhuse).GetBody(group, messageId, out Stream resp, out int resCode, out string errorMsg))
		{
			Log.Error($"Failed to get the body. Error: {errorMsg}. Code: {resCode}");
			return null;
		}
		return resp;
	}

	private Stream DecodeSegment(MemoryStream data, DecoderTypeEnum type, out long msHeaders)
	{
		Stream result = null;
		msHeaders = 0L;
		try
		{
			switch (type)
			{
			case DecoderTypeEnum.Default:
				result = DownloaderDataDecoder.DecodeBinary(data);
				break;
			case DecoderTypeEnum.DsimOptimized:
				result = DownloaderDataDecoderCpuOptimized.NewDecodeBinary(data);
				break;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return null;
		}
		return result;
	}
}
