namespace Spotnet.Downloader.PostProcessing;

public enum Par2Results
{
	NoRepairNeeded,
	Repaired,
	CanRepair,
	CannotRepair,
	CannotRepairBecauseOfFilesAreMissed,
	OtherFailure
}
