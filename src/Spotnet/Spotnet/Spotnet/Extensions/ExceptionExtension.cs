using System;

namespace Spotnet.Extensions;

public static class ExceptionExtension
{
	public static Exception TheMostInnerException(this Exception e)
	{
		if (e == null)
		{
			return new Exception("Unknown error");
		}
		Exception ex = e;
		while (ex.InnerException != null)
		{
			ex = ex.InnerException;
		}
		return ex;
	}
}
