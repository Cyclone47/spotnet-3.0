using System;
using System.Security.Cryptography;

namespace Spotnet.Phuse;

public class YEncDecoder : ICryptoTransform, IDisposable
{
	private const byte life = 42;

	private const byte death = 64;

	private const byte escapeByte = 61;

	private bool _escapeNextByte;

	private YEncCrc32 crc32Hasher = new YEncCrc32();

	public byte[] CRCHash { get; private set; }

	public string CRCDecoded
	{
		get
		{
			string text = string.Empty;
			if (CRCHash != null && CRCHash.Length != 0)
			{
				for (int i = 0; i < CRCHash.Length; i++)
				{
					text += CRCHash[i].ToString("X2");
				}
			}
			return text;
		}
	}

	bool ICryptoTransform.CanReuseTransform => true;

	bool ICryptoTransform.CanTransformMultipleBlocks => true;

	int ICryptoTransform.InputBlockSize => 1;

	int ICryptoTransform.OutputBlockSize => 1;

	public int GetByteCount(byte[] source, int index, int count, bool flush)
	{
		if (source == null)
		{
			throw new ArgumentNullException();
		}
		int num = 0;
		bool flag = _escapeNextByte;
		for (int i = index; i < index + count; i++)
		{
			bool flag2 = false;
			bool flag3 = false;
			try
			{
				byte b = source[i];
				if (!flag)
				{
					switch (b)
					{
					case 61:
						i++;
						if (i >= index + count)
						{
							flag3 = true;
							flag = true;
						}
						break;
					case 10:
					case 13:
						flag2 = true;
						break;
					}
				}
			}
			catch
			{
				throw new ArgumentOutOfRangeException();
			}
			if (!flag2 && !flag3)
			{
				num++;
				flag = false;
			}
		}
		return num;
	}

	public int GetBytes(byte[] source, int sourceIndex, int sourceCount, byte[] dest, int destIndex, bool flush, out bool failed)
	{
		failed = source == null;
		if (failed)
		{
			return 0;
		}
		int num = 0;
		int num2 = destIndex;
		for (int i = sourceIndex; i < sourceIndex + sourceCount; i++)
		{
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			byte b;
			try
			{
				b = source[i];
				if (!_escapeNextByte)
				{
					switch (b)
					{
					case 61:
						i++;
						flag = true;
						if (i < sourceIndex + sourceCount)
						{
							b = source[i];
							break;
						}
						_escapeNextByte = true;
						flag3 = true;
						break;
					case 10:
					case 13:
						flag2 = true;
						break;
					}
				}
			}
			catch
			{
				throw new ArgumentOutOfRangeException();
			}
			if (!flag2 && !flag3)
			{
				b = DecodeByte(b, flag | _escapeNextByte);
				_escapeNextByte = false;
				try
				{
					dest[num2] = b;
					num2++;
					num++;
				}
				catch
				{
					failed = true;
				}
			}
		}
		if (flush)
		{
			crc32Hasher.TransformFinalBlock(dest, destIndex, num);
			CRCHash = crc32Hasher.Hash;
			crc32Hasher = new YEncCrc32();
		}
		else
		{
			crc32Hasher.TransformBlock(dest, destIndex, num, dest, destIndex);
		}
		return num;
	}

	public int GetBytesFast(byte[] source, out byte[] destination, bool flush, out bool failed)
	{
		destination = null;
		failed = source == null;
		if (failed)
		{
			return 0;
		}
		destination = new byte[source.Length];
		int num = 0;
		for (int i = 0; i < source.Length; i++)
		{
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			byte b = source[i];
			if (!_escapeNextByte)
			{
				switch (b)
				{
				case 61:
					i++;
					flag = true;
					if (i < source.Length)
					{
						b = source[i];
						break;
					}
					_escapeNextByte = true;
					flag3 = true;
					break;
				case 10:
				case 13:
					flag2 = true;
					break;
				}
			}
			if (!flag2 && !flag3)
			{
				try
				{
					destination[num++] = DecodeByte(b, flag | _escapeNextByte);
					_escapeNextByte = false;
				}
				catch
				{
					failed = true;
				}
			}
		}
		if (!flush)
		{
			crc32Hasher.TransformBlock(destination, 0, num, destination, 0);
		}
		return num;
	}

	private byte DecodeByte(byte b, bool escape)
	{
		if (escape)
		{
			b -= 64;
		}
		b -= 42;
		return b;
	}

	int ICryptoTransform.TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
	{
		bool failed;
		return GetBytes(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset, flush: false, out failed);
	}

	byte[] ICryptoTransform.TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
	{
		byte[] array = new byte[GetByteCount(inputBuffer, inputOffset, inputCount, flush: true)];
		GetBytes(inputBuffer, inputOffset, inputCount, array, 0, flush: true, out var _);
		return array;
	}

	void IDisposable.Dispose()
	{
	}
}
