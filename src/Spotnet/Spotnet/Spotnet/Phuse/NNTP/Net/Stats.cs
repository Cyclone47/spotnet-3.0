using System;
using System.Threading;

namespace Spotnet.Phuse.NNTP.Net;

internal class Stats
{
	private long zFakeBytes;

	private long zLastBytes;

	private long zLastReset;

	private long zLastTime;

	private long zTotalBytes;

	private long zTotalTime;

	internal long TotalTime
	{
		get
		{
			return Interlocked.Read(ref zTotalTime);
		}
		set
		{
			Module.Safe32(ref zTotalTime, value);
		}
	}

	internal long TotalBytes
	{
		get
		{
			return Interlocked.Read(ref zTotalBytes);
		}
		set
		{
			Module.Safe32(ref zTotalBytes, value);
		}
	}

	internal long FakeBytes
	{
		get
		{
			return Interlocked.Read(ref zFakeBytes);
		}
		set
		{
			Module.Safe32(ref zFakeBytes, value);
		}
	}

	internal long LastTime
	{
		get
		{
			return Interlocked.Read(ref zLastTime);
		}
		set
		{
			Module.Safe32(ref zLastTime, value);
		}
	}

	private long LastReset
	{
		get
		{
			return Interlocked.Read(ref zLastReset);
		}
		set
		{
			Module.Safe32(ref zLastReset, value);
		}
	}

	internal long LastBytes
	{
		get
		{
			return Interlocked.Read(ref zLastBytes);
		}
		set
		{
			Module.Safe32(ref zLastBytes, value);
		}
	}

	internal void ValidateCache()
	{
		if (DateTime.UtcNow.Subtract(new DateTime(LastReset)).TotalSeconds >= 5.0)
		{
			LastTime = 0L;
			LastBytes = 0L;
			LastReset = DateTime.UtcNow.Ticks;
		}
	}

	internal void Progress(long AddedBytes)
	{
		Module.Add32(ref zFakeBytes, AddedBytes);
	}

	internal void Statistics(long AddedBytes, long AddedTime)
	{
		ValidateCache();
		Module.Add32(ref zLastTime, AddedTime);
		Module.Add32(ref zTotalTime, AddedTime);
		Module.Add32(ref zLastBytes, AddedBytes);
		Module.Add32(ref zTotalBytes, AddedBytes);
	}
}
