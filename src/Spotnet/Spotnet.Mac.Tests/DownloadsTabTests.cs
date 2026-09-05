using System;
using System.IO;
using System.Linq;
using Spotnet.Mac.Models;
using Spotnet.Mac.PostProcessing;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class DownloadsTabTests : IDisposable
{
    private readonly string _dir;
    private readonly DownloadHistoryService _history;

    public DownloadsTabTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spotnet-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _history = new DownloadHistoryService(new StandardAppPaths(_dir, _dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
    }

    private static SpotItem Spot(string msgId, string subject = "Iets", long size = 1024) =>
        new() { MsgId = msgId, Subject = subject, Filesize = size };

    [Fact]
    public void Downloads_tab_is_permanent()
    {
        var tab = new DownloadsTabViewModel(_history);

        Assert.Equal("Downloads", tab.Header);
        Assert.False(tab.CanClose);
        Assert.True(tab.IsEmpty);
    }

    [Fact]
    public void Successful_fetch_lands_on_top_and_is_numbered_from_one()
    {
        var tab = new DownloadsTabViewModel(_history);

        tab.Add(Spot("a@spot.net", "Eerste"), success: true, nzbPath: "/tmp/eerste.nzb", message: "ok");
        tab.Add(Spot("b@spot.net", "Tweede"), success: true, nzbPath: "/tmp/tweede.nzb", message: "ok");

        Assert.Equal(new[] { "Tweede", "Eerste" }, tab.Downloads.Select(d => d.Title));
        Assert.Equal(new[] { 1, 2 }, tab.Downloads.Select(d => d.Index));
        Assert.False(tab.IsEmpty);
        Assert.Equal("NZB opgeslagen", tab.Downloads[0].Status);
        Assert.True(tab.Downloads[0].HasFile);
    }

    [Fact]
    public void Failed_fetch_keeps_the_error_and_offers_no_file()
    {
        var tab = new DownloadsTabViewModel(_history);

        tab.Add(Spot("a@spot.net"), success: false, nzbPath: null, message: "Fout: geen server");

        var row = Assert.Single(tab.Downloads);
        Assert.Equal(DownloadStage.Failure, row.Stage);
        Assert.Equal("Mislukt — Fout: geen server", row.Status);
        Assert.True(row.IsFailed);
        Assert.False(row.HasFile);
    }

    [Fact]
    public void Re_downloading_the_same_spot_replaces_its_row()
    {
        var tab = new DownloadsTabViewModel(_history);

        tab.Add(Spot("a@spot.net", "Film"), success: false, nzbPath: null, message: "Fout: geen server");
        tab.Add(Spot("a@spot.net", "Film"), success: true, nzbPath: "/tmp/film.nzb", message: "ok");

        var row = Assert.Single(tab.Downloads);
        Assert.Equal("NZB opgeslagen", row.Status);
        Assert.Equal(1, row.Index);
    }

    [Fact]
    public void History_survives_a_restart()
    {
        var first = new DownloadsTabViewModel(_history);
        first.Add(Spot("a@spot.net", "Film", size: 4L * 1024 * 1024 * 1024), success: true, nzbPath: "/tmp/film.nzb", message: "ok");

        var reopened = new DownloadsTabViewModel(_history);

        var row = Assert.Single(reopened.Downloads);
        Assert.Equal("Film", row.Title);
        Assert.Equal("/tmp/film.nzb", row.NzbPath);
        Assert.Equal("4 GB", row.FormattedSize);
        Assert.Equal(1, row.Index);
    }

    [Fact]
    public void Removing_and_clearing_persist()
    {
        var tab = new DownloadsTabViewModel(_history);
        tab.Add(Spot("a@spot.net", "Een"), success: true, nzbPath: "/tmp/a.nzb", message: "ok");
        tab.Add(Spot("b@spot.net", "Twee"), success: true, nzbPath: "/tmp/b.nzb", message: "ok");

        tab.RemoveCommand.Execute(tab.Downloads.First(d => d.Title == "Een"));

        Assert.Equal(new[] { "Twee" }, new DownloadsTabViewModel(_history).Downloads.Select(d => d.Title));

        tab.ClearCommand.Execute(null);

        Assert.True(new DownloadsTabViewModel(_history).IsEmpty);
    }

    [Fact]
    public void RemoveCommand_with_delete_files_true_deletes_directory_and_nzb()
    {
        var tab = new DownloadsTabViewModel(_history);
        string tempDir = Path.Combine(Path.GetTempPath(), "spotnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string fileInDir = Path.Combine(tempDir, "payload.bin");
        File.WriteAllBytes(fileInDir, new byte[] { 1, 2, 3, 4 });

        string tempNzb = Path.Combine(Path.GetTempPath(), "spotnet_test_" + Guid.NewGuid().ToString("N") + ".nzb");
        File.WriteAllBytes(tempNzb, new byte[] { 60, 110, 122, 98 });

        tab.Add(Spot("test@spot.net", "FileRemovalTest"), success: true, nzbPath: tempNzb, message: "ok");
        var item = tab.Downloads.First();
        item.DownloadDir = tempDir;

        tab.RequestConfirmRemove = _ => Task.FromResult((confirmed: true, deleteFiles: true));

        tab.RemoveCommand.Execute(item);

        Assert.Empty(tab.Downloads);
        Assert.False(Directory.Exists(tempDir));
        Assert.False(File.Exists(tempNzb));
    }

    [Fact]
    public void RemoveCommand_with_delete_files_false_keeps_directory_and_nzb()
    {
        var tab = new DownloadsTabViewModel(_history);
        string tempDir = Path.Combine(Path.GetTempPath(), "spotnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string fileInDir = Path.Combine(tempDir, "payload.bin");
        File.WriteAllBytes(fileInDir, new byte[] { 1, 2, 3, 4 });

        string tempNzb = Path.Combine(Path.GetTempPath(), "spotnet_test_" + Guid.NewGuid().ToString("N") + ".nzb");
        File.WriteAllBytes(tempNzb, new byte[] { 60, 110, 122, 98 });

        try
        {
            tab.Add(Spot("test@spot.net", "KeepFilesTest"), success: true, nzbPath: tempNzb, message: "ok");
            var item = tab.Downloads.First();
            item.DownloadDir = tempDir;

            tab.RequestConfirmRemove = _ => Task.FromResult((confirmed: true, deleteFiles: false));

            tab.RemoveCommand.Execute(item);

            Assert.Empty(tab.Downloads);
            Assert.True(Directory.Exists(tempDir));
            Assert.True(File.Exists(tempNzb));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (File.Exists(tempNzb)) File.Delete(tempNzb);
        }
    }

    [Fact]
    public void RemoveCommand_cancelled_keeps_item_and_files()
    {
        var tab = new DownloadsTabViewModel(_history);
        string tempNzb = Path.Combine(Path.GetTempPath(), "spotnet_test_" + Guid.NewGuid().ToString("N") + ".nzb");
        File.WriteAllBytes(tempNzb, new byte[] { 60, 110, 122, 98 });

        try
        {
            tab.Add(Spot("test@spot.net", "CancelRemoveTest"), success: true, nzbPath: tempNzb, message: "ok");
            var item = tab.Downloads.First();

            tab.RequestConfirmRemove = _ => Task.FromResult((confirmed: false, deleteFiles: true));

            tab.RemoveCommand.Execute(item);

            Assert.Single(tab.Downloads);
            Assert.True(File.Exists(tempNzb));
        }
        finally
        {
            if (File.Exists(tempNzb)) File.Delete(tempNzb);
        }
    }

    [Fact]
    public void CancelDownloadCommand_cancels_in_flight_download()
    {
        var tab = new DownloadsTabViewModel(_history);
        var cts = new System.Threading.CancellationTokenSource();

        var item = new DownloadItem
        {
            Title = "Grote Download",
            MsgId = "large@spot.net",
            Stage = DownloadStage.Downloading,
            JobCts = cts,
            IsDownloading = true
        };

        tab.Downloads.Add(item);
        tab.Selected = item;

        tab.CancelDownloadCommand.Execute(item);

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal("Geannuleerd", item.Status);
        Assert.False(item.IsDownloading);
    }

    [Fact]
    public void DownloadItem_calculates_progress_percent()
    {
        var item = new DownloadItem
        {
            BytesTotal = 1000,
            BytesDone = 450
        };

        Assert.Equal(0.45, item.Progress);
        Assert.Equal(45, item.ProgressPercent);
    }

    [Fact]
    public void DownloadMode_and_folder_persist_in_UserPreferences()
    {
        var paths = new StandardAppPaths(_dir, _dir);
        var prefs = new UserPreferencesService(paths);

        var p = prefs.Current;
        p.DownloadMode = DownloadMode.OpenNzb;
        p.DownloadFolder = "/Volumes/Downloads/Usenet";
        p.MaxDownloadConnections = 8;
        prefs.Save(p);

        var reloaded = new UserPreferencesService(paths).Load();
        Assert.Equal(DownloadMode.OpenNzb, reloaded.DownloadMode);
        Assert.Equal("/Volumes/Downloads/Usenet", reloaded.DownloadFolder);
        Assert.Equal(8, reloaded.MaxDownloadConnections);
    }

    [Fact]
    public void MoveUp_and_MoveDown_reorders_items_and_updates_indices()
    {
        var tab = new DownloadsTabViewModel(_history);
        tab.Add(Spot("a@spot.net", "A"), success: true, nzbPath: "/tmp/a.nzb", message: "ok");
        tab.Add(Spot("b@spot.net", "B"), success: true, nzbPath: "/tmp/b.nzb", message: "ok");

        // Note: Add inserts on top, so B is at 0, A is at 1
        Assert.Equal("B", tab.Downloads[0].Title);
        Assert.Equal("A", tab.Downloads[1].Title);

        var itemA = tab.Downloads[1];
        tab.MoveUpCommand.Execute(itemA);

        Assert.Equal("A", tab.Downloads[0].Title);
        Assert.Equal(1, tab.Downloads[0].Index);
        Assert.Equal("B", tab.Downloads[1].Title);
        Assert.Equal(2, tab.Downloads[1].Index);

        tab.MoveDownCommand.Execute(itemA);
        Assert.Equal("B", tab.Downloads[0].Title);
        Assert.Equal("A", tab.Downloads[1].Title);
    }

    [Fact]
    public void TogglePauseCommand_toggles_pause_state_and_updates_gate()
    {
        var tab = new DownloadsTabViewModel(_history);
        var gate = new System.Threading.ManualResetEventSlim(true);
        var item = new DownloadItem
        {
            Title = "Download",
            MsgId = "dl@spot.net",
            IsDownloading = true,
            PauseGate = gate
        };
        tab.Downloads.Add(item);

        tab.TogglePauseCommand.Execute(item);
        Assert.True(item.IsPaused);
        Assert.False(gate.IsSet);
        Assert.Equal("Gepauzeerd", item.Status);

        tab.TogglePauseCommand.Execute(item);
        Assert.False(item.IsPaused);
        Assert.True(gate.IsSet);
        Assert.Equal(DownloadStage.Downloading, item.Stage);
    }

    [Fact]
    public void UnpackPassword_persists_in_DownloadHistory()
    {
        var tab = new DownloadsTabViewModel(_history);
        tab.Add(Spot("pass@spot.net", "Geheime Spot"), success: true, nzbPath: "/tmp/secret.nzb", message: "ok");

        tab.Downloads[0].UnpackPassword = "SecretPassword123!";
        tab.SaveHistory();

        var reloaded = new DownloadsTabViewModel(_history);
        Assert.Equal("SecretPassword123!", reloaded.Downloads[0].UnpackPassword);
    }

    [Fact]
    public void IsCompleted_true_when_the_pipeline_reached_success()
    {
        var item = new DownloadItem { Stage = DownloadStage.Success };
        Assert.True(item.IsCompleted);
        Assert.Equal("Compleet", item.Status);
        Assert.Equal(1.0, item.Progress);
        Assert.Equal(100, item.ProgressPercent);

        var downloading = new DownloadItem { Stage = DownloadStage.Downloading, BytesTotal = 100, BytesDone = 45 };
        Assert.False(downloading.IsCompleted);
        Assert.Equal(45, downloading.ProgressPercent);
    }

    [Fact]
    public void DownloadDir_persists_in_DownloadHistory()
    {
        var tab = new DownloadsTabViewModel(_history);
        tab.Add(Spot("dir@spot.net", "Spot Met Dir"), success: true, nzbPath: "/tmp/test.nzb", message: "ok");

        tab.Downloads[0].DownloadDir = "/Volumes/Downloads/Spot Met Dir";
        tab.SaveHistory();

        var reloaded = new DownloadsTabViewModel(_history);
        Assert.Equal("/Volumes/Downloads/Spot Met Dir", reloaded.Downloads[0].DownloadDir);
    }
}
