using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;

namespace Spotnet.Model;

public class AverageSpeedCalculator
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly TimeSpan _lastElements;

	private readonly List<KeyValuePair<DateTime, long>> _bytesList;

	private readonly object _lockBytesList = new object();

	public AverageSpeedCalculator(int seconds = 30)
	{
		_lastElements = TimeSpan.FromSeconds(seconds);
		_bytesList = new List<KeyValuePair<DateTime, long>>();
	}

	public void AddNewValue(long bytes)
	{
		if (bytes < 0)
		{
			return;
		}
		lock (_lockBytesList)
		{
			int num = _bytesList.Count - 1;
			if (num >= 0 && (DateTime.UtcNow - _bytesList[num].Key).TotalSeconds < 1.0 && bytes != 0L)
			{
				_bytesList[num] = new KeyValuePair<DateTime, long>(_bytesList[num].Key, _bytesList[num].Value + bytes);
			}
			else
			{
				_bytesList.Add(new KeyValuePair<DateTime, long>(DateTime.UtcNow, bytes));
			}
			while (_bytesList.Any() && DateTime.UtcNow - _bytesList[0].Key > _lastElements)
			{
				_bytesList.RemoveAt(0);
			}
		}
	}

	public long GetBps(int resetPeriodSeconds = 20)
	{
		KeyValuePair<DateTime, long> keyValuePair;
		KeyValuePair<DateTime, long> keyValuePair2;
		long num;
		lock (_lockBytesList)
		{
			if (!_bytesList.Any())
			{
				return 0L;
			}
			keyValuePair = _bytesList[0];
			keyValuePair2 = _bytesList[_bytesList.Count - 1];
			num = _bytesList.Sum((KeyValuePair<DateTime, long> x) => x.Value);
		}
		if (DateTime.UtcNow - keyValuePair2.Key > TimeSpan.FromSeconds(resetPeriodSeconds))
		{
			return 0L;
		}
		DateTime key = keyValuePair.Key;
		double totalSeconds = (DateTime.UtcNow - key).TotalSeconds;
		if (!(totalSeconds > 1.0))
		{
			return 0L;
		}
		return (long)((double)num / totalSeconds);
	}

	public string GetLastSpeedString(bool isInBits = false, int resetPeriodSeconds = 20)
	{
		return BpsToString(GetBps(resetPeriodSeconds), isInBits);
	}

	public static string BpsToString(long bps, bool isInBits = false)
	{
		long num = bps * ((!isInBits) ? 1 : 8) / 1024;
		if (num < 1024)
		{
			if (num >= 10)
			{
				return num + (isInBits ? " Kb/s" : " KB/s");
			}
			return "";
		}
		return Math.Round((double)num / 1024.0, 2).ToString(CultureInfo.InvariantCulture).Replace(".", ",") + (isInBits ? " Mb/s" : " MB/s");
	}

	public void Reset()
	{
		lock (_lockBytesList)
		{
			_bytesList.Clear();
		}
	}
}
