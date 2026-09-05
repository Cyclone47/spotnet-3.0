using System.Collections.Generic;

namespace Spotnet.Phuse.NNTP.Net;

public interface IApi
{
	int Count { get; }

	List<int> Items { get; }

	bool Remove(int id);
}
