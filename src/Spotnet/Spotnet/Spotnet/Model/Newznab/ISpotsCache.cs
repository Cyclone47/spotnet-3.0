using System.Collections.Generic;
using Spotnet.DataVirtualization;

namespace Spotnet.Model.Newznab;

public interface ISpotsCache
{
	void AddOrUpdate(string query, int offset, int limit, int overallCount, List<ISpotRow> data);

	List<ISpotRow> Get(string query, int offset, int limit, out int overallCount);

	void Clear();
}
