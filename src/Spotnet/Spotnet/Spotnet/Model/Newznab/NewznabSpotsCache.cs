using System.Collections.Generic;
using Spotnet.DataVirtualization;

namespace Spotnet.Model.Newznab;

public class NewznabSpotsCache : ISpotsCache
{
	private struct CacheValue
	{
		public readonly List<ISpotRow> Data;

		public readonly int OverallCount;

		public CacheValue(List<ISpotRow> data, int overallCount)
		{
			Data = data;
			OverallCount = overallCount;
		}
	}

	private readonly Dictionary<string, CacheValue> _cache = new Dictionary<string, CacheValue>();

	private string Key(string query, int offset, int limit)
	{
		return $"{offset};{limit};{query}";
	}

	public void AddOrUpdate(string query, int offset, int limit, int overallCount, List<ISpotRow> data)
	{
		_cache[Key(query, offset, limit)] = new CacheValue(data, overallCount);
	}

	public List<ISpotRow> Get(string query, int offset, int limit, out int overallCount)
	{
		overallCount = 0;
		if (_cache.TryGetValue(Key(query, offset, limit), out var value))
		{
			overallCount = value.OverallCount;
			return value.Data;
		}
		return null;
	}

	public void Clear()
	{
		_cache.Clear();
	}
}
