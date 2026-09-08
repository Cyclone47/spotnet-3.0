using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spotnet.Mac.Media;
using Spotnet.Mac.Models;
using Spotnet.Mac.PostProcessing;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Tests;

public sealed class MediaPlayerTests
{
    [Theory]
    [InlineData("film.mkv", true)]
    [InlineData("film.MP4", true)]
    [InlineData("album.flac", true)]
    [InlineData("track.mp3", true)]
    [InlineData("menu.ifo", false)]
    [InlineData("readme.txt", false)]
    public void PlaylistService_UsesWindowsMediaExtensionRules(string file, bool expected)
    {
        Assert.Equal(expected, PlayerPlaylistService.IsSupportedFile(file));
    }

    [Fact]
    public void PlaylistService_SkipsUnpackAndSortsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "spotnet-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "__unpack"));
        File.WriteAllText(Path.Combine(root, "z.mp3"), "z");
        File.WriteAllText(Path.Combine(root, "a.mkv"), "a");
        File.WriteAllText(Path.Combine(root, "__unpack", "hidden.mp4"), "hidden");
        File.WriteAllText(Path.Combine(root, "menu.ifo"), "menu");

        try
        {
            var files = PlayerPlaylistService.GetFilesToPlay(root);

            Assert.Equal(new[] { Path.Combine(root, "a.mkv"), Path.Combine(root, "z.mp3") }, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3661, "1:01:01")]
    public void PlaylistItem_FormatsDurationLikeWindows(int seconds, string expected)
    {
        Assert.Equal(expected, MediaPlaylistItem.FormatLength(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task PlayerViewModel_SynchronizesPlaylistAndPlaysNextItem()
    {
        var root = Path.Combine(Path.GetTempPath(), "spotnet-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "01.mp3");
        var secondPath = Path.Combine(root, "02.mp3");
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");
        var item = new DownloadItem
        {
            Title = "Test download",
            DownloadDir = root,
            Stage = DownloadStage.Success
        };
        var engine = new FakeMediaEngine { Length = TimeSpan.FromMinutes(3) };
        var vm = new PlayerViewModel(() => engine, createTimer: false);

        try
        {
            await vm.OpenForItemAsync(item);

            Assert.Equal(2, vm.PlaylistItems.Count);
            Assert.Equal(firstPath, vm.CurrentItem?.FileFullPath);
            Assert.True(vm.IsPlaying);
            Assert.Equal(firstPath, engine.LoadedPath);

            vm.Pause();
            Assert.False(vm.IsPlaying);
            vm.Resume();
            Assert.True(vm.IsPlaying);

            vm.TryToPlayNext();
            Assert.Equal(secondPath, vm.CurrentItem?.FileFullPath);
            Assert.Equal(secondPath, engine.LoadedPath);
        }
        finally
        {
            vm.FullStop(keepPanel: false);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeMediaEngine : IMediaPlayerEngine
    {
        public IntPtr VideoHandle => IntPtr.Zero;
        public bool IsPlaying { get; private set; }
        public TimeSpan Time { get; set; }
        public TimeSpan Length { get; set; }
        public double Position
        {
            get => Length <= TimeSpan.Zero ? 0 : Time.TotalSeconds / Length.TotalSeconds;
            set => Time = TimeSpan.FromSeconds(Math.Clamp(value, 0, 1) * Length.TotalSeconds);
        }
        public int Volume { get; set; }
        public bool IsMute { get; set; }
        public string? LoadedPath { get; private set; }

        public void Load(string filePath)
        {
            LoadedPath = filePath;
            Time = TimeSpan.Zero;
        }

        public void Resize(double width, double height) { }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Dispose() { }
    }
}
