using System.Threading;

namespace Spotnet.Phuse.NNTP.Net;

internal class NNTPInfo
{
	private long zAvailable;

	private long zBytesDone;

	private long zExpected;

	private long zTotal;

	internal int Total => (int)Interlocked.Read(ref zTotal);

	internal long BytesDone => Interlocked.Read(ref zBytesDone);

	internal long Expected => Interlocked.Read(ref zExpected);

	internal int Available => (int)Interlocked.Read(ref zAvailable);

	internal decimal Percentage
	{
		get
		{
			decimal num = default(decimal);
			if (Total > 0)
			{
				num = 100m - (decimal)(Available / Total) * 100m;
			}
			if (Expected > 0)
			{
				num = (decimal)(BytesDone / Expected) * 100m;
			}
			if (num < 0m)
			{
				num = default(decimal);
			}
			if (num > 100m)
			{
				num = 100m;
			}
			return num;
		}
	}

	internal long BytesLeft
	{
		get
		{
			if (Expected < 1)
			{
				return 0L;
			}
			if (BytesDone >= Expected)
			{
				return 1L;
			}
			return Expected - BytesDone;
		}
	}

	internal NNTPInfo(long Avail, long Expec, long lTotal, long lBytesDone)
	{
		zTotal = lTotal;
		zExpected = Expec;
		zAvailable = Avail;
		zBytesDone = lBytesDone;
	}

	internal int SecondsLeft(long lSpeed, long lTotalTime)
	{
		int num = 0;
		decimal num2 = Percentage;
		if (Expected > 1 && lSpeed > 0)
		{
			num = (int)(BytesLeft / lSpeed);
		}
		if (num < 1)
		{
			if (num2 == 0m)
			{
				num2 = -1m;
			}
			if (lTotalTime == 0L)
			{
				return -1;
			}
			num = (int)((100m / num2 * (decimal)lTotalTime - (decimal)lTotalTime) / 10000000m);
		}
		if (num < 1)
		{
			return 1;
		}
		return num;
	}
}
