using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Spotnet.Downloader;

public class NzbGetGroup
{
	public struct StatusType
	{
		public bool IsPostProcess;

		public DownloadStatus Status;
	}

	public static readonly Dictionary<string, StatusType> StatusData = new Dictionary<string, StatusType>
	{
		{
			"QUEUED",
			new StatusType
			{
				Status = DownloadStatus.Queued,
				IsPostProcess = false
			}
		},
		{
			"FETCHING",
			new StatusType
			{
				Status = DownloadStatus.Checking,
				IsPostProcess = false
			}
		},
		{
			"DOWNLOADING",
			new StatusType
			{
				Status = DownloadStatus.Downloading,
				IsPostProcess = false
			}
		},
		{
			"PP_QUEUED",
			new StatusType
			{
				Status = DownloadStatus.Queued,
				IsPostProcess = true
			}
		},
		{
			"PAUSED",
			new StatusType
			{
				Status = DownloadStatus.Paused,
				IsPostProcess = false
			}
		},
		{
			"LOADING_PARS",
			new StatusType
			{
				Status = DownloadStatus.Checking,
				IsPostProcess = true
			}
		},
		{
			"VERIFYING_SOURCES",
			new StatusType
			{
				Status = DownloadStatus.Checking,
				IsPostProcess = true
			}
		},
		{
			"REPAIRING",
			new StatusType
			{
				Status = DownloadStatus.Repairing,
				IsPostProcess = true
			}
		},
		{
			"VERIFYING_REPAIRED",
			new StatusType
			{
				Status = DownloadStatus.Verifying,
				IsPostProcess = true
			}
		},
		{
			"RENAMING",
			new StatusType
			{
				Status = DownloadStatus.Moving,
				IsPostProcess = true
			}
		},
		{
			"MOVING",
			new StatusType
			{
				Status = DownloadStatus.Moving,
				IsPostProcess = true
			}
		},
		{
			"UNPACKING",
			new StatusType
			{
				Status = DownloadStatus.Unpacking,
				IsPostProcess = true
			}
		},
		{
			"EXECUTING_SCRIPT",
			new StatusType
			{
				Status = DownloadStatus.Checking,
				IsPostProcess = true
			}
		},
		{
			"PP_FINISHED",
			new StatusType
			{
				Status = DownloadStatus.Checking,
				IsPostProcess = false
			}
		},
		{
			"SUCCESS",
			new StatusType
			{
				Status = DownloadStatus.Success,
				IsPostProcess = false
			}
		},
		{
			"FAILURE",
			new StatusType
			{
				Status = DownloadStatus.Failure,
				IsPostProcess = false
			}
		},
		{
			"WARNING",
			new StatusType
			{
				Status = DownloadStatus.Warning,
				IsPostProcess = false
			}
		},
		{
			"DELETED",
			new StatusType
			{
				Status = DownloadStatus.Deleted,
				IsPostProcess = false
			}
		}
	};

	private readonly JObject _item;

	private readonly JObject _status;

	public readonly bool IsHistory;

	public int NzbId => _item.GetValue("NZBID").Value<int>();

	public int Priority => _item.GetValue(IsHistory ? "HistoryTime" : "MaxPriority").Value<int>();

	public string NzbName => _item.GetValue(IsHistory ? "Name" : "NZBName").Value<string>();

	private string NzbStatus => _item.GetValue("Status").Value<string>();

	public DownloadStatus Status
	{
		get
		{
			string key = NzbStatus.Split('/')[0];
			if (!StatusData.ContainsKey(key))
			{
				return DownloadStatus.Unknown;
			}
			return StatusData[key].Status;
		}
	}

	public int TotalArticles => _item.GetValue("TotalArticles").Value<int>();

	public int SuccessArticles => _item.GetValue("SuccessArticles").Value<int>();

	public bool IsPostProcess
	{
		get
		{
			if (!IsHistory)
			{
				return StatusData[NzbStatus].IsPostProcess;
			}
			return false;
		}
	}

	public double DownloadedSizeMB => _item.GetValue("DownloadedSizeMB").Value<double>();

	public double TotalSizeMB => _item.GetValue("FileSizeMB").Value<double>();

	public double TotalSizeLo => _item.GetValue("FileSizeLo").Value<double>();

	public string FormattedTotalSizeMB => FormatSizeMB(TotalSizeMB, TotalSizeLo);

	public double RemainingSizeMB => _item.GetValue("RemainingSizeMB").Value<double>();

	public double PausedSizeMB => _item.GetValue("PausedSizeMB").Value<double>();

	public int PercentsCompleted
	{
		get
		{
			if (IsPostProcess)
			{
				return PostStageProgress / 10;
			}
			if (!IsHistory)
			{
				return (int)((TotalSizeMB - RemainingSizeMB) / TotalSizeMB * 100.0);
			}
			return 100 - (int)((TotalSizeMB + DownloadedSizeMB) * 100.0 / TotalSizeMB);
		}
	}

	private int PostStageProgress => _item.GetValue("PostStageProgress").Value<int>();

	private int PostStageTimeSec => _item.GetValue("PostStageTimeSec").Value<int>();

	public int EstimationTime
	{
		get
		{
			if (IsPostProcess)
			{
				if (PostStageProgress > 0)
				{
					return PostStageTimeSec / PostStageProgress * (1000 - PostStageProgress);
				}
			}
			else if (!NzbStatus.Equals("PAUSED") && Speed > 0 && Speed > 0)
			{
				return (int)((RemainingSizeMB - PausedSizeMB) * 1024.0 / ((double)Speed / 1024.0));
			}
			return 0;
		}
	}

	public int Speed => _status?.GetValue("DownloadRate").Value<int>() ?? 0;

	public string FormattedSpeed => FormatSpeed(Speed);

	public string PreUnpackDir
	{
		get
		{
			if (!IsHistory)
			{
				return _item.GetValue("PreUnpackDir").Value<string>();
			}
			return Location;
		}
	}

	public string Location => _item.GetValue("DestDir").Value<string>();

	public NzbGetGroup(JObject item, JObject status, bool isHistory)
	{
		_item = item;
		_status = status;
		IsHistory = isHistory;
	}

	private string FormatSizeMB(double sizeMB, double sizeLo)
	{
		if (sizeMB < 1024.0)
		{
			sizeMB = sizeLo / 1024.0 / 1024.0;
		}
		if (sizeMB >= 104857600.0)
		{
			return Math.Round(sizeMB / 1024.0 / 1024.0, 0) + " TB";
		}
		if (sizeMB >= 10485760.0)
		{
			return Math.Round(sizeMB / 1024.0 / 1024.0, 1) + " TB";
		}
		if (sizeMB >= 1024000.0)
		{
			return Math.Round(sizeMB / 1024.0 / 1024.0, 2) + " TB";
		}
		if (sizeMB >= 102400.0)
		{
			return Math.Round(sizeMB / 1024.0, 0) + " GB";
		}
		if (sizeMB >= 10240.0)
		{
			return Math.Round(sizeMB / 1024.0, 1) + " GB";
		}
		if (sizeMB >= 1000.0)
		{
			return Math.Round(sizeMB / 1024.0, 2) + " GB";
		}
		if (sizeMB >= 100.0)
		{
			return Math.Round(sizeMB, 0) + " MB";
		}
		if (sizeMB >= 10.0)
		{
			return Math.Round(sizeMB, 1) + " MB";
		}
		return Math.Round(sizeMB, 2) + " MB";
	}

	private string FormatSpeed(int bytesPerSec)
	{
		if (bytesPerSec >= 104857600)
		{
			return Math.Round((double)bytesPerSec / 1024.0 / 1024.0, 0) + " MB/s";
		}
		if (bytesPerSec >= 10485760)
		{
			return Math.Round((double)bytesPerSec / 1024.0 / 1024.0, 1) + " MB/s";
		}
		if (bytesPerSec >= 1024000)
		{
			return Math.Round((double)bytesPerSec / 1024.0 / 1024.0, 2) + " MB/s";
		}
		return Math.Round((double)bytesPerSec / 1024.0, 0) + " KB/s";
	}
}
