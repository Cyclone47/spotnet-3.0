using System;
using System.IO;

namespace Spotnet.Phuse.NNTP.Net;

internal class YEnc
{
	private readonly Stream _outStream;

	public YEnc()
	{
	}

	public YEnc(Stream outStream)
	{
		_outStream = outStream;
	}

	public bool DecodeBytes(byte[] data)
	{
		if (data == null || _outStream == null)
		{
			return false;
		}
		byte[] destination;
		bool failed;
		int bytesFast = new YEncDecoder().GetBytesFast(data, out destination, flush: true, out failed);
		if (failed)
		{
			return false;
		}
		_outStream.Write(destination, 0, bytesFast);
		return true;
	}

	public bool DecodeBytes(byte[] data, out byte[] outData)
	{
		outData = null;
		if (data == null)
		{
			return false;
		}
		byte[] destination;
		bool failed;
		int bytesFast = new YEncDecoder().GetBytesFast(data, out destination, flush: true, out failed);
		if (failed)
		{
			return false;
		}
		outData = new byte[bytesFast];
		Array.Copy(destination, outData, bytesFast);
		return true;
	}
}
