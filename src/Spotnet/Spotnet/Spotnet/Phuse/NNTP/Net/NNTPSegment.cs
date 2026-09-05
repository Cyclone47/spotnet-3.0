using System;
using System.Collections.Generic;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Phuse.NNTP.Net;

public class NNTPSegment : IComparable<NNTPSegment>, IComparable
{
	public static readonly int MaxRetries = ((Settings.Default.DownloaderRetries > 0) ? Settings.Default.DownloaderRetries : 3);

	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(Settings.Default.DownloaderRetryIntervalSec);

	private readonly object _lockIsDownloadScheduled = new object();

	public readonly int Index;

	private bool _isDataAvailable;

	private bool _isDownloaded;

	private bool _isDownloadScheduled;

	private string _lastError;

	private DateTime _startToProcessDateTime = DateTime.MinValue;

	public long ActualDataReceivedLength;

	public List<string> Commands;

	public int ExpectedSize;

	public int ExpectedSizeFromNzbFile;

	public NNTPInput File;

	public long IndentBytes;

	public string SlaveHostname = "";

	public string BridgedInfo = "";

	private bool _isSavedInternal;

	public string Command
	{
		get
		{
			if (!MessageId.IsNullOrEmpty())
			{
				return "BODY " + SpotHelper.MakeMsg(MessageId);
			}
			return "";
		}
	}

	public bool IsDownloadScheduled
	{
		get
		{
			return _isDownloadScheduled;
		}
		set
		{
			lock (_lockIsDownloadScheduled)
			{
				if (_isDownloadScheduled == value)
				{
					return;
				}
				_isDownloadScheduled = value;
			}
			this.IsDownloadScheduledChanged?.Invoke(this, value);
		}
	}

	public bool IsDownloaded
	{
		get
		{
			return _isDownloaded;
		}
		set
		{
			if (_isDownloaded != value)
			{
				_isDownloaded = value;
				this.DownloadedChanged?.Invoke(this, value);
			}
		}
	}

	public string LastError
	{
		get
		{
			return _lastError;
		}
		set
		{
			_lastError = value;
			File.DownloaderItem.LogQueue.Debug($"Segment error: {value} [try {MaxRetries - RetriesLeft}]");
		}
	}

	public int RetriesLeft { get; set; }

	public bool IsFailed { get; private set; }

	public bool IsSaved
	{
		get
		{
			return _isSavedInternal;
		}
		set
		{
			if (_isSavedInternal != value)
			{
				_isSavedInternal = value;
				this.SavedChanged?.Invoke(this, value);
			}
		}
	}

	internal bool IsSavedInternal
	{
		get
		{
			return _isSavedInternal;
		}
		set
		{
			if (_isSavedInternal != value)
			{
				_isSavedInternal = value;
				this.SavedInternalChanged?.Invoke(this, value);
			}
		}
	}

	public string MessageId { get; set; }

	public bool IsUnderTimeout => DateTime.Now < _startToProcessDateTime;

	public bool IsDataAvailable
	{
		get
		{
			return _isDataAvailable;
		}
		set
		{
			if (_isDataAvailable != value)
			{
				_isDataAvailable = value;
				this.DataAvailableChanged?.Invoke(this, value);
			}
		}
	}

	public event Action<NNTPSegment, bool> IsDownloadScheduledChanged;

	public event Action<NNTPSegment, bool> DownloadedChanged;

	public event Action<NNTPSegment, bool> DataAvailableChanged;

	public event Action<NNTPSegment, bool> SavedChanged;

	public event Action<NNTPSegment, bool> SavedInternalChanged;

	public event Action<NNTPSegment, bool> FailedChanged;

	internal NNTPSegment(int index, int bytes, string messageId, NNTPInput file)
	{
		Index = index;
		ExpectedSizeFromNzbFile = bytes;
		ExpectedSize = bytes;
		MessageId = messageId;
		File = file;
		RetriesLeft = MaxRetries;
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as NNTPSegment);
	}

	public int CompareTo(NNTPSegment obj)
	{
		return Index.CompareTo(obj.Index);
	}

	public void MarkAsFailed(string message = null)
	{
		if (!IsFailed)
		{
			IsFailed = true;
			if (message != null)
			{
				LastError = message;
			}
			File.DownloaderItem.LogQueue.Debug("Segment " + MessageId + " marked as failed. Last error: " + LastError);
			this.FailedChanged?.Invoke(this, arg2: true);
		}
	}

	internal void SetTimeout()
	{
		_startToProcessDateTime = DateTime.Now + DefaultTimeout;
	}
}
