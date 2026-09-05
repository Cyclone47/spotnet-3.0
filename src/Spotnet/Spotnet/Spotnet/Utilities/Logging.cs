using NLog;

namespace Spotnet.Utilities;

public static class Logging
{
	public static Logger Logger => LogManager.GetLogger("CommonLogger");
}
