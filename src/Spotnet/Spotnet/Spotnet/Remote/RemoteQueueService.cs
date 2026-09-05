using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Spotnet.Downloader;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Remote;

public class RemoteQueueService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<RemoteQueueService> InstanceHolder = new Lazy<RemoteQueueService>(() => new RemoteQueueService());
    public static RemoteQueueService Instance => InstanceHolder.Value;

    public QueueStatusDto GetQueue()
    {
        var dto = new QueueStatusDto();
        try
        {
            if (Sys.Downloader?.Items == null)
            {
                return dto;
            }

            var totalsRow = Sys.Downloader.TotalItems?.FirstOrDefault();
            if (totalsRow != null)
            {
                dto.OverallSpeedFormatted = totalsRow.Speed ?? "";
                dto.OverallProgress = totalsRow.Perc;
                dto.RemainingSizeFormatted = $"{totalsRow.SizeMegaBytes:F1} MB";
            }

            // Read items safely
            var itemsDict = Sys.Downloader.Items.ItemsDict;
            if (itemsDict != null)
            {
                var list = new List<DownloadItemDto>();
                foreach (var vm in itemsDict.Values.ToList())
                {
                    if (vm == null) continue;

                    bool isComplete = vm.IsHistory || vm.RawStatus == DownloadStatus.Success;
                    double progress = isComplete ? 100.0 : Math.Max(0.0, Math.Min(100.0, vm.Perc));
                    string statusText = isComplete ? "Voltooid" : (vm.Status ?? "");
                    bool isPaused = vm.IsPaused;
                    bool canPause = !isComplete && !isPaused;
                    bool canResume = !isComplete && isPaused;

                    string eta = "";
                    if (!isComplete && vm.SecondsLeft > 0)
                    {
                        TimeSpan ts = TimeSpan.FromSeconds(vm.SecondsLeft);
                        eta = ts.TotalHours >= 1 ? $"{ts.Hours}u {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
                    }

                    list.Add(new DownloadItemDto
                    {
                        Id = vm.ID.ToString(),
                        Title = vm.Titel ?? "Download",
                        MessageId = vm.MessageId ?? "",
                        Status = statusText,
                        Progress = progress,
                        SpeedBytesPerSec = 0,
                        SpeedFormatted = isComplete ? "" : (vm.Speed ?? ""),
                        TotalBytes = (long)(vm.SizeMegaBytes * 1024 * 1024),
                        DownloadedBytes = (long)(vm.SizeMegaBytes * (progress / 100.0) * 1024 * 1024),
                        TotalSizeFormatted = $"{vm.SizeMegaBytes:F1} MB",
                        EtaFormatted = eta,
                        IsPaused = isPaused,
                        IsComplete = isComplete,
                        CanPause = canPause,
                        CanResume = canResume
                    });
                }
                list.Reverse();
                dto.Items = list;
                dto.ActiveCount = list.Count(i => !i.IsComplete);
            }
        }
        catch (Exception ex)
        {
            Log.Error("GetQueue failed: {0}", ex.Message);
        }

        return dto;
    }

    public async Task<bool> EnqueueSpotAsync(long spotId, string messageId)
    {
        return await Task.Run(() =>
        {
            try
            {
                SpotEx spotEx = null;
                if (!string.IsNullOrEmpty(messageId))
                {
                    spotEx = FileCacheManager.Get(messageId);
                }

                if (spotEx == null)
                {
                    string errorMsg = "";
                    if (!Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, spotId, messageId, ref spotEx, AppHelper.HeaderSettings(false), ref errorMsg))
                    {
                        Log.Warn("Failed to fetch spot {0} for download: {1}", spotId, errorMsg);
                        return false;
                    }
                }

                if (spotEx != null)
                {
                    SpotHelper.DownloadNzbAndStartDownloadItem(spotEx);
                    Log.Info("Queued spot '{0}' via Spotnet Remote", spotEx.Title);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EnqueueSpotAsync failed for spot {0}: {1}", spotId, ex.Message);
            }
            return false;
        });
    }

    public bool PauseItem(string id)
    {
        try
        {
            var item = FindItem(id);
            if (item != null)
            {
                Sys.Downloader.PauseItemsAsync(new[] { item });
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("PauseItem failed for {0}: {1}", id, ex.Message);
        }
        return false;
    }

    public bool ResumeItem(string id)
    {
        try
        {
            var item = FindItem(id);
            if (item != null)
            {
                Sys.Downloader.ResumeItemsAsync(new[] { item });
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("ResumeItem failed for {0}: {1}", id, ex.Message);
        }
        return false;
    }

    public bool CancelItem(string id)
    {
        try
        {
            var item = FindItem(id);
            if (item != null)
            {
                Sys.Downloader.RemoveItemsAsync(new[] { item });
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("CancelItem failed for {0}: {1}", id, ex.Message);
        }
        return false;
    }

    public bool SetSpeedLimit(int kbps)
    {
        try
        {
            return Sys.Downloader.UpdateDownloadSpeedLimit(kbps);
        }
        catch (Exception ex)
        {
            Log.Error("SetSpeedLimit failed: {0}", ex.Message);
            return false;
        }
    }

    private DownloaderItemViewModel FindItem(string id)
    {
        if (Sys.Downloader?.Items?.ItemsDict == null) return null;
        var items = Sys.Downloader.Items.ItemsDict.Values;
        return items.FirstOrDefault(i => i != null && (i.ID.ToString().Equals(id, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(i.MessageId) && i.MessageId.Equals(id, StringComparison.OrdinalIgnoreCase))));
    }
}
