namespace Spotnet.Phuse.NNTP.Net;

internal enum SlotStatus
{
	Queued,
	Downloading,
	Decoding,
	Extracting,
	Verifying,
	Repairing,
	Paused,
	Completed,
	Failed
}
