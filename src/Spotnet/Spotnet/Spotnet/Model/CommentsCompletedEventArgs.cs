using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spotnet.Model;

public class CommentsCompletedEventArgs : AsyncCompletedEventArgs
{
	public List<Comment> Comments;

	public string DbFile;

	public CommentsCompletedEventArgs(ref List<Comment> tComments, Exception e, bool cancelled, object state)
		: base(e, cancelled, RuntimeHelpers.GetObjectValue(state))
	{
		DbFile = "";
		if (tComments != null)
		{
			Comments = tComments;
		}
	}
}
