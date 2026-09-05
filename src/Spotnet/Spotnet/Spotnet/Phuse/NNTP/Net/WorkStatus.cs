namespace Spotnet.Phuse.NNTP.Net;

internal enum WorkStatus
{
	Queued,
	Downloading,
	Completed,
	Decoded,
	Decompressed,
	Missing,
	Failed
}
