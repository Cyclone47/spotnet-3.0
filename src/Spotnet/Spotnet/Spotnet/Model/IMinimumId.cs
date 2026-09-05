namespace Spotnet.Model;

internal interface IMinimumId
{
	long Value { get; set; }

	bool IsActive { get; set; }

	void Reset();

	void UpdateIfRequired(long minId);
}
