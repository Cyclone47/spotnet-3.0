using System.Collections;
using System.Security.Cryptography;

namespace Spotnet.Phuse;

public class YEncCrc32 : HashAlgorithm
{
	protected static uint AllOnes;

	protected static Hashtable CachedCrc32Tables;

	protected uint[] Crc32Table;

	private uint _mCrc;

	public static uint DefaultPolynomial => 79764919u;

	public static bool AutoCache { get; set; }

	public uint[] CurrentTable => Crc32Table;

	static YEncCrc32()
	{
		AllOnes = uint.MaxValue;
		CachedCrc32Tables = Hashtable.Synchronized(new Hashtable());
		AutoCache = true;
	}

	public YEncCrc32()
		: this(DefaultPolynomial)
	{
	}

	public YEncCrc32(uint aPolynomial)
		: this(aPolynomial, AutoCache)
	{
	}

	public YEncCrc32(uint aPolynomial, bool cacheTable)
	{
		HashSizeValue = 32;
		Crc32Table = (uint[])CachedCrc32Tables[aPolynomial];
		if (Crc32Table == null)
		{
			Crc32Table = BuildCrc32Table(aPolynomial);
			if (cacheTable && !CachedCrc32Tables.ContainsKey(aPolynomial))
			{
				CachedCrc32Tables.Add(aPolynomial, Crc32Table);
			}
		}
		Initialize();
	}

	public static void ClearCache()
	{
		CachedCrc32Tables.Clear();
	}

	private static uint Reflect(uint val)
	{
		uint num = 0u;
		for (int i = 0; i < 32; i++)
		{
			num = (num << 1) + (val & 1);
			val >>= 1;
		}
		return num;
	}

	protected static uint[] BuildCrc32Table(uint ulPolynomial)
	{
		uint[] array = new uint[256];
		ulPolynomial = Reflect(ulPolynomial);
		for (int i = 0; i < 256; i++)
		{
			uint num = (uint)i;
			for (int num2 = 8; num2 > 0; num2--)
			{
				num = (((num & 1) != 1) ? (num >> 1) : ((num >> 1) ^ ulPolynomial));
			}
			array[i] = num;
		}
		return array;
	}

	public override void Initialize()
	{
		_mCrc = AllOnes;
		State = 0;
	}

	protected override void HashCore(byte[] buffer, int offset, int count)
	{
		for (int i = offset; i < offset + count; i++)
		{
			ulong num = (_mCrc & 0xFFu) ^ buffer[i];
			_mCrc >>= 8;
			_mCrc ^= Crc32Table[num];
		}
		State = 1;
	}

	protected override byte[] HashFinal()
	{
		byte[] array = new byte[4];
		ulong num = _mCrc ^ AllOnes;
		array[0] = (byte)((num >> 24) & 0xFF);
		array[1] = (byte)((num >> 16) & 0xFF);
		array[2] = (byte)((num >> 8) & 0xFF);
		array[3] = (byte)(num & 0xFF);
		State = 0;
		return array;
	}
}
