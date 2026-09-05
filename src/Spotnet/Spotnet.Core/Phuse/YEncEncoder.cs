using System;
using System.Security.Cryptography;

namespace Spotnet.Phuse;

public class YEncEncoder : ICryptoTransform, IDisposable
{
	private const byte Life = 42;

	private const byte Death = 64;

	private const byte EscapeByte = 61;

	private readonly byte[] _additionalEscapeBytes;

	private readonly bool _doLineFeedAfterFlush;

	private readonly byte[] _standardEscapeBytes = new byte[4] { 10, 13, 0, 61 };

	private YEncCrc32 _crc32Hasher = new YEncCrc32();

	private int _lineBytes;

	private int _lineLength;

	private byte[] _storedHash;

	public byte[] CRCHash => _storedHash;

	public string CrcDecoded
	{
		get
		{
			string text = string.Empty;
			if (_storedHash != null && _storedHash.Length != 0)
			{
				for (int i = 0; i < _storedHash.Length; i++)
				{
					text += _storedHash[i].ToString("X2");
				}
			}
			return text;
		}
	}

	bool ICryptoTransform.CanReuseTransform => true;

	bool ICryptoTransform.CanTransformMultipleBlocks => true;

	int ICryptoTransform.InputBlockSize => 1;

	int ICryptoTransform.OutputBlockSize => 3;

	public YEncEncoder()
		: this(128, new byte[0], crlfAfter: false)
	{
	}

	public YEncEncoder(int lineLength, byte[] escapeThese, bool crlfAfter)
	{
		_lineLength = lineLength;
		_additionalEscapeBytes = escapeThese;
		_doLineFeedAfterFlush = crlfAfter;
	}

	public int GetBytes(byte[] source, int sourceIndex, int sourceCount, byte[] dest, int destIndex, bool flush)
	{
		if (source == null || dest == null)
		{
			throw new ArgumentNullException();
		}
		int num = 0;
		int num2 = _lineBytes;
		for (int i = sourceIndex; i < sourceCount + sourceIndex; i++)
		{
			byte b;
			try
			{
				b = source[i];
			}
			catch
			{
				throw new ArgumentOutOfRangeException();
			}
			bool escape;
			byte b2 = EncodeByte(b, out escape);
			try
			{
				if (escape)
				{
					dest[destIndex] = 61;
					destIndex++;
					num++;
					num2++;
				}
				dest[destIndex] = b2;
				destIndex++;
				num2++;
				num++;
			}
			catch
			{
				throw new ArgumentException();
			}
		}
		if (flush)
		{
			if (_doLineFeedAfterFlush)
			{
				dest[destIndex] = 13;
				destIndex++;
				num++;
				dest[destIndex] = 10;
				destIndex++;
				num++;
			}
			_crc32Hasher.TransformFinalBlock(source, sourceIndex, sourceCount);
			_storedHash = _crc32Hasher.Hash;
			_crc32Hasher = new YEncCrc32();
			_lineBytes = 0;
		}
		else
		{
			_crc32Hasher.TransformBlock(source, sourceIndex, sourceCount, source, sourceIndex);
			_lineBytes = num2;
		}
		return num;
	}

	private byte EncodeByte(byte b, out bool escape)
	{
		b += 42;
		escape = false;
		for (int i = 0; i < _standardEscapeBytes.Length; i++)
		{
			if (b == _standardEscapeBytes[i])
			{
				escape = true;
				b += 64;
				break;
			}
		}
		if (!escape)
		{
			for (int j = 0; j < _additionalEscapeBytes.Length; j++)
			{
				if (b == _additionalEscapeBytes[j])
				{
					b += 64;
					escape = true;
					break;
				}
			}
		}
		return b;
	}

	public int GetByteCount(byte[] bytes, int index, int count, bool flush)
	{
		if (bytes == null)
		{
			throw new ArgumentNullException();
		}
		int num = 0;
		int num2 = _lineBytes;
		for (int i = index; i < count + index; i++)
		{
			byte b;
			try
			{
				b = bytes[i];
			}
			catch
			{
				throw new ArgumentOutOfRangeException();
			}
			EncodeByte(b, out var escape);
			try
			{
				if (escape)
				{
					num++;
					num2++;
				}
				num2++;
				num++;
			}
			catch
			{
				throw new ArgumentException();
			}
		}
		if (flush && _doLineFeedAfterFlush)
		{
			num++;
			num++;
		}
		return num;
	}

	int ICryptoTransform.TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
	{
		return GetBytes(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset, flush: false);
	}

	byte[] ICryptoTransform.TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
	{
		byte[] array = new byte[GetByteCount(inputBuffer, inputOffset, inputCount, flush: true)];
		GetBytes(inputBuffer, inputOffset, inputCount, array, 0, flush: true);
		return array;
	}

	void IDisposable.Dispose()
	{
	}
}
