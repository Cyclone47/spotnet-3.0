using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using NLog;
using Spotnet.Helpers;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Platform;
using Spotnet.Mac.PostProcessing;
using Spotnet.Mac.Services;

namespace Spotnet.Mac.ViewModels;

/// <summary>
/// The Downloads tab, second in the strip and never closable — as on Windows.
/// Lists the NZBs fetched from Usenet and, when the integrated downloader is
/// active, shows live progress (Voortgang/Snelheid/ETA) for each row.
/// </summary>
public sealed class DownloadsTabViewModel : WorkspaceTabViewModel
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly DownloadHistoryService _history;
    private readonly MacNotificationService _notificationService;

    /// <summary>
    /// Optional external unpackers. Verification, repair and unpacking are all built
    /// into the app, so this is only consulted as a fallback for archives the
    /// built-in extractor cannot read — there is nothing for the user to install.
    /// </summary>
    private readonly PostProcessToolset _tools = new();

    public override string Header => "Downloads";
    public override bool CanClose => false;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    private DownloadItem? _selected;
    public DownloadItem? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public bool IsEmpty => Downloads.Count == 0;

    public ICommand OpenCommand   { get; }
    public ICommand RevealCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand  { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand OpenSpotInfoCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand SetPasswordCommand { get; }
    public ICommand RetryPostProcessCommand { get; }

    public event Action<string>? RequestOpenSpotInfo;
    public event Action<DownloadItem>? RequestSetPassword;
    public Func<DownloadItem, Task<(bool confirmed, bool deleteFiles)>>? RequestConfirmRemove;
    public Func<int, long, Task<(bool confirmed, bool deleteFiles)>>? RequestConfirmClear;


    public DownloadsTabViewModel(DownloadHistoryService history, UserPreferencesService? preferences = null, MacNotificationService? notificationService = null)
    {
        _history = history;
        _notificationService = notificationService ?? new MacNotificationService(preferences);

        foreach (var item in _history.Load())
        {
            if (item.IsCompleted)
            {
                item.IsDownloading = false;
                if (item.BytesTotal <= 0 && item.SizeBytes > 0) item.BytesTotal = item.SizeBytes;
                item.BytesDone = item.BytesTotal;
            }
            Downloads.Add(item);
        }
        Renumber();

        OpenCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item == null) return;

            // 1. If download directory exists, open it directly in Finder
            if (!string.IsNullOrEmpty(item.DownloadDir) && Directory.Exists(item.DownloadDir))
            {
                Run("/usr/bin/open", $"\"{item.DownloadDir}\"");
                return;
            }

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Spotnet", NzbService.SanitizeFileName(item.Title));
            if (Directory.Exists(defaultDir))
            {
                Run("/usr/bin/open", $"\"{defaultDir}\"");
                return;
            }

            // 2. Otherwise open the NZB file if it exists
            if (item.HasFile && File.Exists(item.NzbPath))
            {
                Run("/usr/bin/open", $"\"{item.NzbPath}\"");
            }
        });

        RevealCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item == null) return;

            if (!string.IsNullOrEmpty(item.DownloadDir) && Directory.Exists(item.DownloadDir))
            {
                Run("/usr/bin/open", $"\"{item.DownloadDir}\"");
                return;
            }

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Spotnet", NzbService.SanitizeFileName(item.Title));
            if (Directory.Exists(defaultDir))
            {
                Run("/usr/bin/open", $"\"{defaultDir}\"");
                return;
            }

            if (item.HasFile && File.Exists(item.NzbPath))
            {
                Run("/usr/bin/open", $"-R \"{item.NzbPath}\"");
            }
        });

        RemoveCommand = new RelayCommand(async param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null)
            {
                bool deleteFiles = false;
                if (RequestConfirmRemove != null)
                {
                    var (confirmed, del) = await RequestConfirmRemove(item);
                    if (!confirmed) return;
                    deleteFiles = del;
                }

                item.JobCts?.Cancel();
                item.PauseGate?.Set();

                if (deleteFiles)
                {
                    DeleteStoredFiles(item);
                }

                Downloads.Remove(item);
                Renumber();
                Persist();
            }
        });

        ClearCommand = new RelayCommand(async () =>
        {
            if (Downloads.Count == 0) return;

            bool deleteFiles = false;
            if (RequestConfirmClear != null)
            {
                long totalBytes = Downloads.Sum(d => DownloadItem.GetDiskSizeBytes(d));
                var (confirmed, del) = await RequestConfirmClear(Downloads.Count, totalBytes);
                if (!confirmed) return;
                deleteFiles = del;
            }

            foreach (var item in Downloads.ToList())
            {
                item.JobCts?.Cancel();
                item.PauseGate?.Set();
                if (deleteFiles)
                {
                    DeleteStoredFiles(item);
                }
            }
            Downloads.Clear();
            Renumber();
            Persist();
        });

        CancelDownloadCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item?.JobCts != null && !item.JobCts.IsCancellationRequested)
            {
                item.JobCts.Cancel();
                item.PauseGate?.Set();
                item.SetStage(DownloadStage.Cancelled);
                item.IsDownloading = false;
                Persist();
            }
        });

        OpenSpotInfoCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null && !string.IsNullOrEmpty(item.MsgId))
            {
                RequestOpenSpotInfo?.Invoke(item.MsgId);
            }
        });

        OpenLogCommand = new RelayCommand(() =>
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Logs", "Spotnet");
            Directory.CreateDirectory(logsDir);
            Run("/usr/bin/open", $"\"{logsDir}\"");
        });

        MoveUpCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null)
            {
                int idx = Downloads.IndexOf(item);
                if (idx > 0)
                {
                    Downloads.Move(idx, idx - 1);
                    Renumber();
                    Persist();
                }
            }
        });

        MoveDownCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null)
            {
                int idx = Downloads.IndexOf(item);
                if (idx >= 0 && idx < Downloads.Count - 1)
                {
                    Downloads.Move(idx, idx + 1);
                    Renumber();
                    Persist();
                }
            }
        });

        TogglePauseCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null && item.IsDownloading)
            {
                if (item.IsPaused)
                {
                    item.IsPaused = false;
                    item.PauseGate?.Set();
                    item.SetStage(DownloadStage.Downloading, $"{item.ProgressPercent}%");
                }
                else
                {
                    item.IsPaused = true;
                    item.PauseGate?.Reset();
                    item.SetStage(DownloadStage.Paused);
                    item.SpeedText = "0 B/s";
                }
                Persist();
            }
        });

        SetPasswordCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null)
            {
                RequestSetPassword?.Invoke(item);
            }
        });

        RetryPostProcessCommand = new RelayCommand(param =>
        {
            var item = param as DownloadItem ?? Selected;
            if (item != null) _ = RunPostProcessAsync(item);
        });
    }

    /// <summary>
    /// Records a finished (or failed) NZB fetch and puts it on top of the list.
    /// When <paramref name="job"/> is non-null the integrated downloader is active and
    /// progress will be streamed into the row.
    /// </summary>
    public void Add(SpotItem spot, bool success, string? nzbPath, string message,
                    NzbDownloadJob? job = null,
                    string? description = null,
                    CancellationToken cancellationToken = default)
    {
        var existing = Downloads.FirstOrDefault(d => d.MsgId == spot.MsgId);
        if (existing != null)
        {
            existing.JobCts?.Cancel();
            Downloads.Remove(existing);
        }

        var cts = job != null ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;

        var gate = new System.Threading.ManualResetEventSlim(true);
        var item = new DownloadItem
        {
            Title    = spot.Subject,
            MsgId    = spot.MsgId,
            NzbPath  = success ? nzbPath ?? string.Empty : string.Empty,
            SizeBytes = spot.Filesize,
            JobCts   = cts,
            PauseGate = gate,
            DownloadDir = job?.OutputDir ?? "",
            IsDownloading = job != null && success
        };

        string? autoPassword = UnpackPasswordDetector.Detect(nzbPath, description, spot.Subject);
        if (!string.IsNullOrEmpty(autoPassword))
        {
            item.UnpackPassword = autoPassword;
            Log.Info("[{0}] Auto-detected archive password at queue time", spot.Subject);
        }

        if (!success)
            item.SetStage(DownloadStage.Failure, message);
        else if (job != null)
            item.SetStage(DownloadStage.Downloading);
        else
            item.SetStage(DownloadStage.NzbSaved);

        Downloads.Insert(0, item);
        Renumber();
        Persist();

        // Fire off the binary download in the background and stream progress into the row
        if (job != null && success && cts != null)
        {
            _ = RunJobAsync(item, job, cts.Token);
        }
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunJobAsync(
        DownloadItem item,
        NzbDownloadJob job,
        CancellationToken ct)
    {
        long bytesTotal = job.Files.Sum(f => f.Segments.Sum(s => s.Bytes));
        item.BytesTotal = bytesTotal > 0 ? bytesTotal : item.SizeBytes;

        var progress = new Progress<NzbJobProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!item.IsDownloading) return;

                item.BytesDone = p.BytesDone;
                item.BytesTotal = p.BytesTotal > 0 ? p.BytesTotal : item.BytesTotal;

                if (p.SpeedBps > 0)
                {
                    item.SpeedText = FormatSpeed(p.SpeedBps);
                    long remaining = item.BytesTotal - p.BytesDone;
                    long eta = p.SpeedBps > 0 ? remaining / p.SpeedBps : 0;
                    item.EtaText = eta > 0 ? FormatEta(eta) : "";
                }

                if (item.BytesDone < item.BytesTotal)
                {
                    item.SetStage(DownloadStage.Downloading,
                        item.BytesTotal > 0
                            ? (string.IsNullOrEmpty(item.SpeedText)
                                ? $"{item.ProgressPercent}%"
                                : $"{item.ProgressPercent}% — {item.SpeedText}")
                            : "");
                }
            });
        });

        try
        {
            await job.RunAsync(progress, item.PauseGate, ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.IsDownloading = false;
                item.BytesDone     = item.BytesTotal;
                item.SpeedText     = "";
                item.EtaText       = "";
                item.JobCts        = null;
                if (string.IsNullOrEmpty(item.DownloadDir)) item.DownloadDir = job.OutputDir;
                Persist();
            });

            // The bytes are on disk; now do what Windows does next.
            await RunPostProcessAsync(item);
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.SetStage(DownloadStage.Cancelled);
                item.IsDownloading = false;
                item.SpeedText    = "";
                item.EtaText      = "";
                Persist();
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Binary download failed for {0}", item.MsgId);
            Dispatcher.UIThread.Post(() =>
            {
                item.SetStage(DownloadStage.Failure, ex.Message);
                item.IsDownloading = false;
                item.SpeedText    = "";
                item.EtaText      = "";
                Persist();
            });
        }
    }

    /// <summary>
    /// Verifies, repairs and unpacks a finished download, streaming each stage into
    /// the row. Safe to call again after the user supplies a password: the pipeline
    /// simply re-runs over whatever is still in the directory.
    /// </summary>
    public async System.Threading.Tasks.Task RunPostProcessAsync(DownloadItem item)
    {
        string dir = item.DownloadDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.SetStage(DownloadStage.Success);
                Persist();
                _notificationService.NotifyDownloadFinished(item.Title, success: true);
            });
            return;
        }

        // 1. Auto-detect archive password from NZB if not already provided
        if (string.IsNullOrEmpty(item.UnpackPassword))
        {
            string? nzbPath = item.HasFile && File.Exists(item.NzbPath) ? item.NzbPath : null;
            if (string.IsNullOrEmpty(nzbPath) && Directory.Exists(dir))
            {
                var candidates = Directory.GetFiles(dir, "*.nzb");
                if (candidates.Length > 0) nzbPath = candidates[0];
            }

            string? detectedPassword = UnpackPasswordDetector.FromNzbFile(nzbPath);
            if (!string.IsNullOrEmpty(detectedPassword))
            {
                item.UnpackPassword = detectedPassword;
                Log.Info("[{0}] Auto-detected archive password from NZB metadata", item.Title);
            }
        }

        var progress = new Progress<PostProcessProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.PostProcessPercent = p.Percent;
                string detail = p.Detail ?? (p.Percent >= 0 && DownloadStageText.IsPostProcessing(p.Stage)
                    ? $"{(int)p.Percent}%"
                    : "");
                item.SetStage(p.Stage, detail);
            });
        });

        var coordinator = new PostProcessCoordinator(dir, _tools, progress,
            logSink: line => Log.Info("[{0}] {1}", item.Title, line));

        PostProcessOutcome outcome;
        try
        {
            outcome = await coordinator.RunAsync(item.UnpackPassword ?? "", CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Post-process failed for {0}", item.MsgId);
            outcome = PostProcessOutcome.Failed;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            item.PostProcessPercent = -1;
            switch (outcome)
            {
                case PostProcessOutcome.Success:
                    item.SetStage(DownloadStage.Success);
                    _notificationService.NotifyDownloadFinished(item.Title, success: true);
                    break;
                case PostProcessOutcome.Warning:
                    item.SetStage(DownloadStage.Warning, "nabewerking gaf problemen, zie log");
                    _notificationService.NotifyDownloadFinished(item.Title, success: false, detail: "problemen tijdens nabewerking");
                    break;
                case PostProcessOutcome.ArchiveDamaged:
                    item.SetStage(DownloadStage.Warning, "archief beschadigd, reparatie niet gelukt");
                    _notificationService.NotifyDownloadFinished(item.Title, success: false, detail: "archief beschadigd");
                    break;
                case PostProcessOutcome.ArchiveDamagedNoPar2:
                    item.SetStage(DownloadStage.Warning, "archief beschadigd, geen par2 om te herstellen");
                    _notificationService.NotifyDownloadFinished(item.Title, success: false, detail: "geen par2 herstelbestanden");
                    break;
                case PostProcessOutcome.PasswordRequired:
                    item.SetStage(DownloadStage.WrongPassword,
                        string.IsNullOrEmpty(item.UnpackPassword)
                            ? "wachtwoord vereist"
                            : "wachtwoord onjuist");
                    _notificationService.NotifyDownloadFinished(item.Title, success: false, detail: "wachtwoord vereist");
                    break;
                case PostProcessOutcome.Cancelled:
                    item.SetStage(DownloadStage.Cancelled);
                    break;
                default:
                    item.SetStage(DownloadStage.Failure, "nabewerking mislukt, zie log");
                    _notificationService.NotifyDownloadFinished(item.Title, success: false, detail: "nabewerking mislukt");
                    break;
            }
            Persist();
        });
    }

    private void Renumber()
    {
        for (int i = 0; i < Downloads.Count; i++)
        {
            Downloads[i].Index = i + 1;
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void SaveHistory() => Persist();
    private void Persist() => _history.Save(Downloads);

    private static void Run(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kon {0} niet starten", fileName);
        }
    }

    private static string FormatSpeed(long bps)
    {
        if (bps >= 1_000_000) return $"{bps / 1_000_000.0:0.#} MB/s";
        if (bps >= 1_000)     return $"{bps / 1_000.0:0.#} KB/s";
        return $"{bps} B/s";
    }

    private static string FormatEta(long seconds)
    {
        if (seconds > 3600) return $"~{seconds / 3600}u {(seconds % 3600) / 60}m";
        if (seconds > 60)   return $"~{seconds / 60}m {seconds % 60}s";
        return $"~{seconds}s";
    }

    public static void DeleteStoredFiles(DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.DownloadDir))
        {
            try
            {
                if (Directory.Exists(item.DownloadDir))
                {
                    Directory.Delete(item.DownloadDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Kon downloadmap niet verwijderen: {0}", item.DownloadDir);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.NzbPath))
        {
            try
            {
                if (File.Exists(item.NzbPath))
                {
                    File.Delete(item.NzbPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Kon NZB-bestand niet verwijderen: {0}", item.NzbPath);
            }
        }
    }
}
