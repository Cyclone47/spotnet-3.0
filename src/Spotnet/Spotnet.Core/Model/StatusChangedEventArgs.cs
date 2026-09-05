using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spotnet.Model;

public class StatusChangedEventArgs : ProgressChangedEventArgs
{
	public string Message;

	public StatusChangedEventArgs(string message, int value, object state)
		: base(value, RuntimeHelpers.GetObjectValue(state))
	{
		Message = message;
	}
}
