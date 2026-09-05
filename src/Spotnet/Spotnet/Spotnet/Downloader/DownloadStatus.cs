namespace Spotnet.Downloader;

public enum DownloadStatus
{
	Unknown,
	Empty,
	Totals,
	Stopping,
	Starting,
	Queued,
	NzbDownloading,
	Downloading,
	Pausing,
	Paused,
	Par2PieceDownloading,
	Checking,
	Repairing,
	Verifying,
	Moving,
	Unpacking,
	WrongPassword,
	Success,
	Failure,
	FailureNoSuchArticle,
	Warning,
	Deleted
}
