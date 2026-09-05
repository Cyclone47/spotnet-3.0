using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace Spotnet.Helpers;

public class StructConverter
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static byte[] TypeAgnosticGetBytes(object o)
	{
		if (o is int)
		{
			return BitConverter.GetBytes((int)o);
		}
		if (o is uint)
		{
			return BitConverter.GetBytes((uint)o);
		}
		if (o is long)
		{
			return BitConverter.GetBytes((long)o);
		}
		if (o is ulong)
		{
			return BitConverter.GetBytes((ulong)o);
		}
		if (o is short)
		{
			return BitConverter.GetBytes((short)o);
		}
		if (o is ushort)
		{
			return BitConverter.GetBytes((ushort)o);
		}
		if (o is byte || o is sbyte)
		{
			return new byte[1] { (byte)o };
		}
		throw new ArgumentException("Unsupported object type found");
	}

	private static string GetFormatSpecifierFor(object o)
	{
		if (o is int)
		{
			return "i";
		}
		if (o is uint)
		{
			return "I";
		}
		if (o is long)
		{
			return "q";
		}
		if (o is ulong)
		{
			return "Q";
		}
		if (o is short)
		{
			return "h";
		}
		if (o is ushort)
		{
			return "H";
		}
		if (o is byte)
		{
			return "B";
		}
		if (o is sbyte)
		{
			return "b";
		}
		throw new ArgumentException("Unsupported object type found");
	}

	private static void Debug(string message)
	{
	}

	public static object[] Unpack(string fmt, byte[] bytes)
	{
		Debug($"Format string is length {fmt.Length}, {bytes.Length} bytes provided.");
		if (fmt.Length < 1)
		{
			throw new ArgumentException("Format string cannot be empty.");
		}
		bool flag = false;
		if (fmt.Substring(0, 1) == "<")
		{
			Debug("  Endian marker found: little endian");
			if (!BitConverter.IsLittleEndian)
			{
				flag = true;
			}
			fmt = fmt.Substring(1);
		}
		else if (fmt.Substring(0, 1) == ">")
		{
			Debug("  Endian marker found: big endian");
			if (BitConverter.IsLittleEndian)
			{
				flag = true;
			}
			fmt = fmt.Substring(1);
		}
		int num = 0;
		string text = fmt;
		foreach (char c in text)
		{
			Debug($"  Format character found: {c}");
			switch (c)
			{
			case 'Q':
			case 'q':
				num += 8;
				break;
			case 'I':
			case 'i':
				num += 4;
				break;
			case 'H':
			case 'h':
				num += 2;
				break;
			case 'B':
			case 'b':
			case 'x':
				num++;
				break;
			default:
				throw new ArgumentException("Invalid character found in format string.");
			}
		}
		Debug(string.Format("Endianness will {0}be flipped.", flag ? "" : "NOT "));
		Debug($"The byte array is expected to be {num} bytes long.");
		if (bytes.Length != num)
		{
			throw new ArgumentException("The number of bytes provided does not match the total length of the format string.");
		}
		int num2 = 0;
		List<object> list = new List<object>();
		Debug("Processing byte array...");
		text = fmt;
		for (int i = 0; i < text.Length; i++)
		{
			switch (text[i])
			{
			case 'q':
				list.Add(BitConverter.ToInt64(bytes, num2));
				num2 += 8;
				Debug("  Added signed 64-bit integer.");
				break;
			case 'Q':
				list.Add(BitConverter.ToUInt64(bytes, num2));
				num2 += 8;
				Debug("  Added unsigned 64-bit integer.");
				break;
			case 'l':
				list.Add(BitConverter.ToInt32(bytes, num2));
				num2 += 4;
				Debug("  Added signed 32-bit integer.");
				break;
			case 'L':
				list.Add(BitConverter.ToUInt32(bytes, num2));
				num2 += 4;
				Debug("  Added unsignedsigned 32-bit integer.");
				break;
			case 'h':
				list.Add(BitConverter.ToInt16(bytes, num2));
				num2 += 2;
				Debug("  Added signed 16-bit integer.");
				break;
			case 'H':
				list.Add(BitConverter.ToUInt16(bytes, num2));
				num2 += 2;
				Debug("  Added unsigned 16-bit integer.");
				break;
			case 'b':
			{
				byte[] array = new byte[1];
				Array.Copy(bytes, num2, array, 0, 1);
				list.Add((sbyte)array[0]);
				num2++;
				Debug("  Added signed byte");
				break;
			}
			case 'B':
			{
				byte[] array = new byte[1];
				Array.Copy(bytes, num2, array, 0, 1);
				list.Add(array[0]);
				num2++;
				Debug("  Added unsigned byte");
				break;
			}
			case 'x':
				num2++;
				Debug("  Ignoring a byte");
				break;
			default:
				throw new ArgumentException("You should not be here.");
			}
		}
		return list.ToArray();
	}

	public static byte[] Pack(object[] items, bool littleEndian, out string neededFormatStringToRecover)
	{
		List<byte> list = new List<byte>();
		bool flag = littleEndian != BitConverter.IsLittleEndian;
		string text = ((!littleEndian) ? ">" : "<");
		foreach (object o in items)
		{
			byte[] array = TypeAgnosticGetBytes(o);
			if (flag)
			{
				array = (byte[])array.Reverse();
			}
			text += GetFormatSpecifierFor(o);
			list.AddRange(array);
		}
		neededFormatStringToRecover = text;
		return list.ToArray();
	}

	public static byte[] Pack(object[] items)
	{
		string neededFormatStringToRecover;
		return Pack(items, littleEndian: true, out neededFormatStringToRecover);
	}
}
