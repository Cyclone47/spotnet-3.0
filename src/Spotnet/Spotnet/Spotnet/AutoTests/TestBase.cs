using NLog;

namespace Spotnet.AutoTests;

public class TestBase
{
	protected static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public virtual void Start()
	{
	}

	public virtual void Stop()
	{
	}

	public virtual void Run()
	{
	}
}
