using System;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Extensions;

public static class DateTimeExtension
{
	public static long ToUnixTime(this DateTime date)
	{
		return (long)date.ToUniversalTime().Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
	}

	public static DateTime FromUnixTime(this long unixTime)
	{
		return AppHelper.Epoch.AddSeconds(unixTime).ToLocalTime();
	}

	public static DateTime FromUnixTime(this int unixTime)
	{
		return ((long)unixTime).FromUnixTime();
	}

	public static string ToAge(this DateTime dateTime)
	{
		if (dateTime < new DateTime(2000, 1, 1))
		{
			return "";
		}
		DateTime dateTime2 = Conversions.ToDate(DateAndTime.Now.ToString("yyyy-MM-dd"));
		long num = DateAndTime.DateDiff(DateInterval.Day, dateTime, dateTime2);
		long num2 = DateAndTime.DateDiff("s", dateTime2, dateTime);
		if (num < 7)
		{
			if (num2 > 0)
			{
				return Words.today + " (" + dateTime.ToString("HH:mm") + ")";
			}
			if (num2 > -86400)
			{
				return Words.yesterday + " (" + dateTime.ToString("HH:mm") + ")";
			}
			return ToDayOfWeek((long)dateTime.DayOfWeek) + " (" + dateTime.ToString("HH:mm") + ")";
		}
		return num + 1 + " " + Words.days + " (" + dateTime.ToString("HH:mm") + ")";
	}

	private static string ToDayOfWeek(long lInt)
	{
		long num = lInt - 1;
		if ((ulong)num <= 5uL)
		{
			switch (num)
			{
			case 0L:
				return Words.monday;
			case 1L:
				return Words.tuesday;
			case 2L:
				return Words.wednesday;
			case 3L:
				return Words.thursday;
			case 4L:
				return Words.friday;
			case 5L:
				return Words.saturday;
			}
		}
		return Words.sunday;
	}

	public static string ToShortTimeString(this TimeSpan time)
	{
		string arg = "";
		if (time.Hours > 9)
		{
			arg = $"{time.Hours:00}:";
		}
		else if (time.Hours > 0)
		{
			arg = $"{time.Hours:0}:";
		}
		return $"{arg}{time.Minutes:00}:{time.Seconds:00}";
	}
}
