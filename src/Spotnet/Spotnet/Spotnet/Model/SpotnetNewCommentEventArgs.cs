using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spotnet.Model;

public class SpotnetNewCommentEventArgs : ProgressChangedEventArgs
{
	private readonly Comment _progComment;

	public Comment cComment => _progComment;

	public SpotnetNewCommentEventArgs(Comment pc, object userState)
		: base(0, RuntimeHelpers.GetObjectValue(userState))
	{
		_progComment = pc;
	}
}
