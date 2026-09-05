using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Downloader;

public class HealthChecker
{
	private readonly long _parSize;

	private readonly long _totalSize;

	public readonly double HealthThreshold;

	private long _failedSize;

	public double HealthLevel => 1.0 - 1.0 * (double)_failedSize / (double)_totalSize;

	public HealthChecker(IEnumerable<NNTPInput> files)
	{
		foreach (NNTPInput file in files)
		{
			long num = ((IEnumerable<NNTPSegment>)file.Segments).Sum((Func<NNTPSegment, long>)((NNTPSegment s) => s.ExpectedSizeFromNzbFile));
			_totalSize += num;
			if (new Regex("(\\.par2$)", RegexOptions.IgnoreCase).IsMatch(file.Filename.Trim()))
			{
				_parSize += num;
			}
		}
		long num2 = _totalSize - _parSize;
		HealthThreshold = ((num2 > 0) ? (1.0 - (double)_parSize / (double)num2 - 0.01) : 0.0);
		if (HealthThreshold < 0.0)
		{
			HealthThreshold = 0.0;
		}
		else if (_parSize == 0L)
		{
			HealthThreshold = 0.85;
		}
	}

	public void RemoveFromFailed(NNTPSegment segment)
	{
		_failedSize -= segment.ExpectedSizeFromNzbFile;
	}

	public bool IsHealthThresholdReached(NNTPSegment failedSegment = null)
	{
		if (failedSegment != null)
		{
			_failedSize += failedSegment.ExpectedSizeFromNzbFile;
		}
		return HealthLevel <= HealthThreshold;
	}
}
