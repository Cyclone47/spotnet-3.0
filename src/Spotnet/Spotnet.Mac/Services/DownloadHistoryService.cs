using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Mac.PostProcessing;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// Persists the Downloads tab across restarts. Windows keeps its download queue in
/// the downloader's own store; we only need the list of NZBs we handed off, so a
/// small JSON file next to the database is enough.
/// </summary>
public sealed class DownloadHistoryService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public DownloadHistoryService(IAppPaths appPaths)
    {
        _path = Path.Combine(appPaths.DataFolder, "downloads.json");
    }

    /// <summary>
    /// One persisted row. <c>Status</c> is the legacy free-text field written by
    /// builds before the pipeline had stages; <c>Stage</c> and <c>StatusDetail</c>
    /// are what we write now. Old files are read back through
    /// <see cref="StageFromLegacyStatus"/> so nobody's history goes blank.
    /// </summary>
    private sealed record Entry(
        string Title,
        string MsgId,
        string NzbPath,
        string Status,
        long SizeBytes,
        DateTime AddedUtc,
        string? UnpackPassword = null,
        string? DownloadDir = null,
        DownloadStage? Stage = null,
        string? StatusDetail = null);

    public IReadOnlyList<DownloadItem> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<DownloadItem>();

            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path));
            if (entries == null) return Array.Empty<DownloadItem>();

            return entries.Select(e =>
            {
                var item = new DownloadItem
                {
                    Title = e.Title,
                    MsgId = e.MsgId,
                    NzbPath = e.NzbPath,
                    SizeBytes = e.SizeBytes,
                    AddedUtc = e.AddedUtc,
                    UnpackPassword = e.UnpackPassword ?? "",
                    DownloadDir = e.DownloadDir ?? ""
                };

                DownloadStage stage = e.Stage ?? StageFromLegacyStatus(e.Status);
                string detail = e.StatusDetail ?? DetailFromLegacyStatus(stage, e.Status);
                item.SetStage(stage, detail);
                return item;
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kon downloads.json niet lezen");
            return Array.Empty<DownloadItem>();
        }
    }

    /// <summary>Maps a pre-stage status string onto a stage.</summary>
    internal static DownloadStage StageFromLegacyStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) return DownloadStage.Unknown;
        if (status.StartsWith('\u2713') || status.Contains("voltooid", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.Success;
        if (status.StartsWith("Fout", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.Failure;
        if (status.StartsWith("Geannuleerd", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.Cancelled;
        if (status.StartsWith("NZB", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.NzbSaved;
        if (status.StartsWith("Gepauzeerd", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.Paused;
        if (status.StartsWith("Downloaden", StringComparison.OrdinalIgnoreCase))
            return DownloadStage.Queued;
        return DownloadStage.Unknown;
    }

    /// <summary>Keeps the useful half of a legacy failure message as the detail.</summary>
    internal static string DetailFromLegacyStatus(DownloadStage stage, string? status)
    {
        if (stage != DownloadStage.Failure || string.IsNullOrEmpty(status)) return "";
        int colon = status.IndexOf(':');
        return colon >= 0 && colon + 1 < status.Length ? status[(colon + 1)..].Trim() : status;
    }

    /// <summary>A download that was still running when we quit is left queued.</summary>
    internal static DownloadStage InterruptedStage(DownloadStage stage) => stage switch
    {
        DownloadStage.Downloading => DownloadStage.Queued,
        DownloadStage.Pausing => DownloadStage.Paused,
        _ when DownloadStageText.IsPostProcessing(stage) => DownloadStage.Queued,
        _ => stage
    };

    public void Save(IEnumerable<DownloadItem> items)
    {
        try
        {
            var entries = items.Select(i => new Entry(
                i.Title, i.MsgId, i.NzbPath, i.Status, i.SizeBytes, i.AddedUtc,
                i.UnpackPassword, i.DownloadDir,
                // A row caught mid-flight comes back as "wachten", not as a half-done
                // download: nothing resumes an interrupted job across a restart.
                InterruptedStage(i.Stage), i.StatusDetail));
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kon downloads.json niet schrijven");
        }
    }
}
