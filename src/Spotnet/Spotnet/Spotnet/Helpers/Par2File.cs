using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

internal static class Par2File
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const string ParId = "PAR2\0PKT";

	public static List<Md5HashPair> Parse(string fname)
	{
		List<Md5HashPair> list = new List<Md5HashPair>();
		FileStream fileStream;
		try
		{
			fileStream = File.OpenRead(fname);
		}
		catch (Exception)
		{
			return null;
		}
		try
		{
			byte[] array = new byte[8];
			while (fileStream.Read(array, 0, 8) != 0)
			{
				string @string = Encoding.UTF8.GetString(array);
				Md5HashPair pair = ParsePar2FilePacket(fileStream, @string);
				if (!pair.Filename.IsNullOrEmpty() && !list.Exists((Md5HashPair i) => i.Filename.Equals(pair.Filename)))
				{
					list.Add(pair);
				}
			}
			return list;
		}
		catch (Exception ex2)
		{
			Log.Debug("Failed to parse par2 file: " + fname);
			Log.Exception(ex2);
			return null;
		}
		finally
		{
			fileStream.Close();
		}
	}

	private static Md5HashPair ParsePar2FilePacket(FileStream f, string header)
	{
		Md5HashPair result = default(Md5HashPair);
		if (!"PAR2\0PKT".Equals(header))
		{
			return result;
		}
		byte[] array = ReadBytes(f, 8);
		if (array == null)
		{
			throw new Exception("Failed to read par2 length.");
		}
		int num;
		try
		{
			num = Convert.ToInt32(StructConverter.Unpack("<Q", array)[0]);
		}
		catch (Exception)
		{
			Log.Debug("Failed to parse header length.");
			throw;
		}
		if (num % 4 != 0 || num < 20)
		{
			throw new Exception("Par2 header length is malformed");
		}
		array = ReadBytes(f, 16);
		if (array == null)
		{
			throw new Exception("Failed to read md5sum of packet.");
		}
		string text = AppHelper.MakeMd5(array);
		byte[] array2 = ReadBytes(f, num - 32);
		if (array2 == null)
		{
			throw new Exception("Failed to read par2 data");
		}
		// MD5.Create() resolves to the platform's managed/CNG implementation. The
		// CryptoServiceProvider types wrap a CAPI provider that a Windows 11 machine
		// with the FIPS policy enabled refuses to hand out, which threw an
		// InvalidOperationException here and failed every par2 validation.
		// The digest itself is unchanged, so par2 packet hashes still match.
		using (MD5 mD = MD5.Create())
		{
			mD.TransformFinalBlock(array2, 0, array2.Length);
			if (!text.Equals(AppHelper.MakeMd5(mD.Hash)))
			{
				throw new Exception("Par2 data is malformed");
			}
		}
		num = array2.Length;
		for (int i = 0; i < num - 72; i += 8)
		{
			string @string = Encoding.UTF8.GetString(array2, i, 16);
			if ("PAR 2.0\0FileDesc".Equals(@string))
			{
				string hash = AppHelper.MakeMd5(array2, i + 32, 16);
				string filename = Encoding.ASCII.GetString(array2, i + 72, num - (i + 72)).Replace("?", "").Trim(default(char));
				Md5HashPair result2 = default(Md5HashPair);
				result2.Filename = filename;
				result2.Hash = hash;
				return result2;
			}
		}
		return result;
	}

	private static byte[] ReadBytes(FileStream f, int count)
	{
		byte[] array = new byte[count];
		if (f.Read(array, 0, count) == count)
		{
			return array;
		}
		return null;
	}
}
