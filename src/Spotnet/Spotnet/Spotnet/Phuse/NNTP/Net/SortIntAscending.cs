using System.Collections.Generic;

namespace Spotnet.Phuse.NNTP.Net;

internal class SortIntAscending : IComparer<int>
{
	int IComparer<int>.Compare(int a, int b)
	{
		if (a > b)
		{
			return 1;
		}
		if (a < b)
		{
			return -1;
		}
		return 0;
	}
}
