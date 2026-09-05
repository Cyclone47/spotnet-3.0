using System;
using System.Collections.Generic;
using System.Timers;

namespace Spotnet.Extensions;

public static class EventExtension
{
	private static readonly List<Timer> TimersList = new List<Timer>();

	public static Timer RunAfter(this Action action, TimeSpan span)
	{
		Timer timer = new Timer
		{
			AutoReset = false,
			Interval = span.TotalMilliseconds
		};
		timer.Elapsed += delegate(object sender, ElapsedEventArgs args)
		{
			if (sender is Timer timer2)
			{
				timer2.Enabled = false;
			}
			TimersList.RemoveAll((Timer t) => !t.Enabled);
			action();
		};
		TimersList.Add(timer);
		timer.Start();
		return timer;
	}
}
