using System.Diagnostics;
using NLog;

namespace Spotnet.Helpers;

public class Tracker
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly string _name;

	private readonly Stopwatch _timer;

	private int _count = 1;

	public long ElapsedMilliseconds => _timer.ElapsedMilliseconds;

	public Tracker()
	{
		StackFrame stackFrame = new StackFrame(1);
		_name = stackFrame.GetMethod().Name;
		_timer = new Stopwatch();
		_timer.Start();
	}

	public void Debug()
	{
		Log.Debug("Watch ({0}:{1}): {2} ms", _name, _count++, _timer.ElapsedMilliseconds);
	}

	public void Debug(string mark)
	{
		Log.Debug("Watch ({0}:{1}): {2} ms ({3})", _name, _count++, _timer.ElapsedMilliseconds, mark);
	}

	public void Restart()
	{
		_timer.Restart();
	}
}
