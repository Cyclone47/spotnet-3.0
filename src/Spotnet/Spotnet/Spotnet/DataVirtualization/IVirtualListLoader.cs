using System.Collections.Generic;
using System.Threading;

namespace Spotnet.DataVirtualization;

public interface IVirtualListLoader<T>
{
	bool CanSort { get; }

	string RowFilter { get; }

	void ResetCount();

	IList<T> LoadRange(int startIndex, int count, long minRowId, out int overallCount, out bool isNewQuery, out bool isLastPage, CancellationToken cancellationToken);
}
