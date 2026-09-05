using System;
using System.IO;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests for fase 3 item 3: remove-files decision on row removal, the par2
/// cleanup preference pass-through, and the shutdown-after-downloads gating.
/// Behaviour follows the Windows client: DownloaderItems.RunRemoveFilesFromTheDiskDialog
/// (RemoveFilesOnDownloadRemove -1/0/1), PostProcessCoordinator's
/// RemovePar2FilesAfterDownload gate, and SpotnetDownloader.ProcessShutdownPcAfterDownloads.
/// </summary>
public sealed class DownloadCleanupTests : IDisposable
{
    private readonly string _dir;
    private readonly UserPreferencesService _prefsService;

    public DownloadCleanupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spotnet-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _prefsService = new UserPreferencesService(new StandardAppPaths(_dir, _dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
    }

    // ── RemoveFilesOnDownloadRemove (Windows: RunRemoveFilesFromTheDiskDialog) ─

    [Theory]
    [InlineData(1, true, RemoveFilesAnswer.Yes)]    // always delete: no question
    [InlineData(0, true, RemoveFilesAnswer.No)]     // always keep: no question
    [InlineData(-1, true, null)]                    // ask, because files exist
    [InlineData(-1, false, RemoveFilesAnswer.No)]   // nothing on disk: no question
    public void QuestionOrDefault_follows_the_windows_preference(
        int preference, bool filesOnDisk, RemoveFilesAnswer? expected)
    {
        Assert.Equal(expected, RemoveFilesDecision.QuestionOrDefault(preference, filesOnDisk));
    }

    [Fact]
    public void Remember_stores_the_answer_like_the_windows_dialog_buttons()
    {
        Assert.Equal(1, RemoveFilesDecision.Remember(-1, RemoveFilesAnswer.Yes));   // Yes button
        Assert.Equal(0, RemoveFilesDecision.Remember(-1, RemoveFilesAnswer.No));    // No button
        Assert.Equal(-1, RemoveFilesDecision.Remember(-1, RemoveFilesAnswer.Cancel));
    }

    [Fact]
    public void Remove_preference_survives_a_save_and_reload()
    {
        var prefs = _prefsService.Current;
        prefs.RemoveFilesOnDownloadRemove = 1;
        prefs.RemovePar2FilesAfterDownload = false;
        prefs.ShutdownPcAfterDownloads = true;
        _prefsService.Save(prefs);

        var reloaded = new UserPreferencesService(new StandardAppPaths(_dir, _dir)).Load();

        Assert.Equal(1, reloaded.RemoveFilesOnDownloadRemove);
        Assert.False(reloaded.RemovePar2FilesAfterDownload);
        Assert.True(reloaded.ShutdownPcAfterDownloads);
    }

    // ── Row removal goes through the decision (DownloadsTabViewModel) ─────────

    [Fact]
    public async Task Removing_a_row_with_preference_1_deletes_the_files_without_asking()
    {
        var prefs = new UserPreferencesService(new StandardAppPaths(_dir + "-p1", _dir));
        prefs.Current.RemoveFilesOnDownloadRemove = 1;
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir + "-p1", _dir)), prefs);
        bool asked = false;
        tab.RequestAskRemoveFiles = _ => { asked = true; return Task.FromResult(true); };

        string dir = Path.Combine(_dir, "film");
        Directory.CreateDirectory(dir);
        string nzb = Path.Combine(_dir, "film.nzb");
        await File.WriteAllTextAsync(nzb, "nzb");
        tab.Add(Spot("a@x"), success: true, nzbPath: nzb, message: "ok");
        tab.Downloads[0].DownloadDir = dir;

        Assert.Equal(1, tab.EffectiveRemoveFilesPreference);
        var (confirmed, deleteFiles) = await tab.RemoveWithCleanupAsync(tab.Downloads[0]);

        Assert.True(confirmed);
        Assert.True(deleteFiles);
        Assert.False(asked, "the question must be skipped when the preference is 1");
        Assert.Empty(tab.Downloads);
        Assert.False(Directory.Exists(dir), "files on disk must be deleted");
        Assert.False(File.Exists(nzb), "the nzb must be deleted too");
    }

    [Fact]
    public async Task Removing_a_row_with_preference_0_keeps_the_files()
    {
        var prefs = new UserPreferencesService(new StandardAppPaths(_dir + "-p0", _dir));
        prefs.Current.RemoveFilesOnDownloadRemove = 0;
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir + "-p0", _dir)), prefs);

        string dir = Path.Combine(_dir, "film");
        Directory.CreateDirectory(dir);
        string nzb = Path.Combine(_dir, "film.nzb");
        await File.WriteAllTextAsync(nzb, "nzb");
        tab.Add(Spot("a@x"), success: true, nzbPath: nzb, message: "ok");
        tab.Downloads[0].DownloadDir = dir;

        var (confirmed, deleteFiles) = await tab.RemoveWithCleanupAsync(tab.Downloads[0]);

        Assert.True(confirmed);
        Assert.False(deleteFiles);
        Assert.Empty(tab.Downloads);
        Assert.True(Directory.Exists(dir), "files must stay on disk");
        Assert.True(File.Exists(nzb));
    }

    [Fact]
    public async Task Asking_once_with_remember_stops_future_questions()
    {
        var prefs = new UserPreferencesService(new StandardAppPaths(_dir + "-pa", _dir));
        prefs.Current.RemoveFilesOnDownloadRemove = -1;
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir + "-pa", _dir)), prefs);

        string nzb = Path.Combine(_dir, "a.nzb");
        await File.WriteAllTextAsync(nzb, "nzb");
        tab.Add(Spot("a@x"), success: true, nzbPath: nzb, message: "ok");
        tab.Add(Spot("b@x"), success: true, nzbPath: nzb, message: "ok");

        int asks = 0;
        tab.RequestAskRemoveFiles = _ => { asks++; return Task.FromResult(true); };
        tab.RequestRememberRemoveFilesAnswer = () => true;

        await tab.RemoveWithCleanupAsync(tab.Downloads[0]);
        Assert.Single(tab.Downloads);
        await tab.RemoveWithCleanupAsync(tab.Downloads[0]);
        Assert.Empty(tab.Downloads);

        Assert.Equal(1, asks);                       // remembered: the second removal asks nothing
        Assert.Equal(1, prefs.Current.RemoveFilesOnDownloadRemove);
        Assert.False(File.Exists(nzb));
    }

    // ── Shutdown after downloads (Windows: ProcessShutdownPcAfterDownloads) ────

    [Fact]
    public void Shutdown_is_not_requested_when_the_preference_is_off()
    {
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir, _dir)), _prefsService);
        bool requested = false;
        tab.RequestShutdownAfterDownloads += () => requested = true;

        tab.MaybeRequestShutdownAfterDownloads();

        Assert.False(requested);
    }

    [Fact]
    public void Shutdown_is_not_requested_while_a_download_is_still_running()
    {
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir, _dir)), _prefsService);
        _prefsService.Current.ShutdownPcAfterDownloads = true;
        bool requested = false;
        tab.RequestShutdownAfterDownloads += () => requested = true;

        tab.Add(Spot("busy@x"), success: true, nzbPath: null, message: "ok");
        tab.Downloads[0].IsDownloading = true;

        tab.MaybeRequestShutdownAfterDownloads();

        Assert.False(requested);
    }

    [Fact]
    public void Shutdown_is_requested_when_the_last_download_finishes()
    {
        var tab = new DownloadsTabViewModel(new DownloadHistoryService(new StandardAppPaths(_dir, _dir)), _prefsService);
        _prefsService.Current.ShutdownPcAfterDownloads = true;
        bool requested = false;
        tab.RequestShutdownAfterDownloads += () => requested = true;

        tab.MaybeRequestShutdownAfterDownloads();

        Assert.True(requested);
    }

    private static SpotItem Spot(string msgId, string subject = "Iets") =>
        new() { MsgId = msgId, Subject = subject, Filesize = 1024 };
}
