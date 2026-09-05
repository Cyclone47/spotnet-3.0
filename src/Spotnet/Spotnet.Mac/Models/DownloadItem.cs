using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using Spotnet.Mac.PostProcessing;

namespace Spotnet.Mac.Models;

/// <summary>
/// One row in the Downloads tab: an NZB we fetched from Usenet. When the
/// integrated downloader is active this row also carries live progress state —
/// Voortgang (progress), Snelheid (speed) and ETA — mirroring the Windows
/// DownloadsViewModel columns.
/// </summary>
public sealed class DownloadItem : INotifyPropertyChanged
{
    /// <summary>Row number in the grid, as Windows' "#" column.</summary>
    private int _index;
    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public string Title { get; init; } = string.Empty;
    public string MsgId { get; init; } = string.Empty;

    /// <summary>Absolute path of the saved .nzb, empty when the fetch failed.</summary>
    private string _nzbPath = string.Empty;
    public string NzbPath
    {
        get => _nzbPath;
        set { _nzbPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFile)); }
    }

    public bool HasFile => !string.IsNullOrEmpty(NzbPath);

    private string _downloadDir = string.Empty;
    public string DownloadDir
    {
        get => _downloadDir;
        set { _downloadDir = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Where the row is in the download-then-post-process pipeline. This is the
    /// single source of truth for what the Status column shows; the Windows client
    /// works the same way, with DownloaderItemViewModel.RawStatus driving .Status.
    /// </summary>
    private DownloadStage _stage = DownloadStage.Unknown;
    public DownloadStage Stage
    {
        get => _stage;
        set
        {
            if (_stage == value) return;
            _stage = value;
            OnPropertyChanged();
            NotifyStatusChanged();
        }
    }

    /// <summary>
    /// The part of the status line that is not the stage label: "45% - 3,2 MB/s"
    /// while downloading, a par2/unrar percentage during post-processing, or the
    /// error text on a failure.
    /// </summary>
    private string _statusDetail = "";
    public string StatusDetail
    {
        get => _statusDetail;
        set
        {
            if (_statusDetail == value) return;
            _statusDetail = value;
            OnPropertyChanged();
            NotifyStatusChanged();
        }
    }

    /// <summary>What the Status column reads: stage label plus detail.</summary>
    public string Status => _statusDetail.Length == 0
        ? DownloadStageText.Label(_stage)
        : DownloadStageText.Label(_stage) + " — " + _statusDetail;

    /// <summary>Sets stage and detail together, so the row updates once.</summary>
    public void SetStage(DownloadStage stage, string detail = "")
    {
        _stage = stage;
        _statusDetail = detail;
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(StatusDetail));
        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsPostProcessing));
        OnPropertyChanged(nameof(NeedsPassword));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public bool IsFailed => _stage is DownloadStage.Failure or DownloadStage.Cancelled;

    /// <summary>True when the download and its post-processing have finished.</summary>
    public bool IsCompleted => _stage is DownloadStage.Success or DownloadStage.Warning;

    /// <summary>True while par2/unrar own the row - repairing, unpacking and so on.</summary>
    public bool IsPostProcessing => DownloadStageText.IsPostProcessing(_stage);

    /// <summary>
    /// True when the archive is encrypted and Spotnet is waiting for a password.
    /// The Downloads grid turns the status cell into a button in this state, the way
    /// Windows turns it into a hyperlink.
    /// </summary>
    public bool NeedsPassword => _stage == DownloadStage.WrongPassword;

    /// <summary>
    /// Post-process steps that cannot report a percentage - joining split files,
    /// the quick check, moving - run the progress bar as a marquee instead.
    /// </summary>
    public bool IsProgressIndeterminate => IsPostProcessing && _postProcessPercent < 0;

    /// <summary>0-100 while a post-process step runs, or -1 when it cannot say.</summary>
    private double _postProcessPercent = -1;
    public double PostProcessPercent
    {
        get => _postProcessPercent;
        set
        {
            if (Math.Abs(_postProcessPercent - value) < 0.01) return;
            _postProcessPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    /// <summary>Size of the spot's payload, not of the .nzb file.</summary>
    public long SizeBytes { get; init; }

    public string FormattedSize => SizeBytes <= 0 ? "—" : FormatBytes(SizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double size = bytes;
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {suffixes[order]}";
    }

    public static long GetDiskSizeBytes(DownloadItem item)
    {
        long bytes = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.DownloadDir) && Directory.Exists(item.DownloadDir))
            {
                var dir = new DirectoryInfo(item.DownloadDir);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { bytes += file.Length; } catch { }
                }
            }
        }
        catch { }

        try
        {
            if (bytes == 0 && !string.IsNullOrWhiteSpace(item.NzbPath) && File.Exists(item.NzbPath))
            {
                bytes += new FileInfo(item.NzbPath).Length;
            }
        }
        catch { }

        if (bytes == 0 && item.SizeBytes > 0)
        {
            bytes = item.SizeBytes;
        }

        return bytes;
    }

    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;

    public string Added => AddedUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm");

    // ── Live progress (integrated downloader only) ─────────────────────────────

    private long _bytesDone;
    public long BytesDone
    {
        get => _bytesDone;
        set
        {
            _bytesDone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    private long _bytesTotal;
    public long BytesTotal
    {
        get => _bytesTotal;
        set { _bytesTotal = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); }
    }

    /// <summary>0–1 fraction for a ProgressBar.</summary>
    public double Progress
    {
        get
        {
            if (IsCompleted) return 1.0;
            // Once the bytes are in, the bar tracks the post-process step instead.
            if (IsPostProcessing) return _postProcessPercent >= 0 ? Math.Clamp(_postProcessPercent / 100.0, 0, 1) : 1.0;
            return _bytesTotal > 0 ? Math.Clamp((double)_bytesDone / _bytesTotal, 0, 1) : 0;
        }
    }

    /// <summary>0–100 for display label.</summary>
    public int ProgressPercent => (int)(Progress * 100);

    private string _speedText = "";
    public string SpeedText { get => _speedText; set { _speedText = value; OnPropertyChanged(); } }

    private string _etaText = "";
    public string EtaText { get => _etaText; set { _etaText = value; OnPropertyChanged(); } }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; OnPropertyChanged(); }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PauseActionText));
                OnPropertyChanged(nameof(PauseActionIcon));
                OnPropertyChanged(nameof(PauseMenuHeader));
            }
        }
    }

    public string PauseActionText => IsPaused ? "Hervatten" : "Pauzeren";
    public string PauseActionIcon => IsPaused ? "▶" : "⏸";
    public string PauseMenuHeader => $"{PauseActionIcon}  {PauseActionText}";

    private string _unpackPassword = "";
    public string UnpackPassword
    {
        get => _unpackPassword;
        set { _unpackPassword = value; OnPropertyChanged(); }
    }

    /// <summary>Event used to pause/resume download workers without terminating.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public ManualResetEventSlim? PauseGate { get; set; }

    /// <summary>
    /// Cancels the in-flight binary download. Not serialized to JSON.
    /// Null when the download finished or was never an integrated download.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public CancellationTokenSource? JobCts { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

