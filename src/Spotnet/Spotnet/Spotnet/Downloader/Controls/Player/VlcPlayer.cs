using System;
using System.IO;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace Spotnet.Downloader.Controls.Player;

/// <summary>
/// Small WPF compatibility surface around LibVLCSharp. Keeping playback details here
/// lets the existing Spotnet player UI remain unchanged while replacing the x86-only
/// legacy player control and native engine.
/// </summary>
public sealed class VlcPlayer : VideoView, IDisposable
{
    private readonly LibVLC _libVlc;
    private Media _media;
    private bool _disposed;

    public VlcPlayer()
    {
        Core.Initialize();
        _libVlc = new LibVLC(
            "--intf=dummy",
            "--quiet",
            "--ignore-config",
            "--no-video-title-show",
            "--no-sub-autodetect-file");

        VlcMediaPlayer = new MediaPlayer(_libVlc);
        MediaPlayer = VlcMediaPlayer;

        VlcMediaPlayer.PositionChanged += delegate { PositionChanged?.Invoke(this, EventArgs.Empty); };
        VlcMediaPlayer.LengthChanged += delegate { LengthChanged?.Invoke(this, EventArgs.Empty); };
        VlcMediaPlayer.VolumeChanged += delegate { VolumeChanged?.Invoke(this, EventArgs.Empty); };
        VlcMediaPlayer.Muted += delegate { IsMuteChanged?.Invoke(this, EventArgs.Empty); };
        VlcMediaPlayer.Unmuted += delegate { IsMuteChanged?.Invoke(this, EventArgs.Empty); };
    }

    public MediaPlayer VlcMediaPlayer { get; }

    public VLCState State => VlcMediaPlayer?.State ?? VLCState.NothingSpecial;

    public TimeSpan Time
    {
        get => TimeSpan.FromMilliseconds(Math.Max(0L, VlcMediaPlayer?.Time ?? 0L));
        set
        {
            if (VlcMediaPlayer != null)
            {
                VlcMediaPlayer.Time = Math.Max(0L, (long)value.TotalMilliseconds);
            }
        }
    }

    public TimeSpan Length => TimeSpan.FromMilliseconds(Math.Max(0L, VlcMediaPlayer?.Length ?? 0L));

    public double Position
    {
        get => VlcMediaPlayer?.Position ?? 0f;
        set
        {
            if (VlcMediaPlayer != null)
            {
                VlcMediaPlayer.Position = (float)Math.Max(0d, Math.Min(1d, value));
            }
        }
    }

    public int Volume
    {
        get => VlcMediaPlayer?.Volume ?? 0;
        set
        {
            if (VlcMediaPlayer != null)
            {
                VlcMediaPlayer.Volume = Math.Max(0, Math.Min(200, value));
            }
        }
    }

    public bool IsMute
    {
        get => VlcMediaPlayer?.Mute ?? false;
        set
        {
            if (VlcMediaPlayer != null)
            {
                VlcMediaPlayer.Mute = value;
            }
        }
    }

    public event EventHandler PositionChanged;
    public event EventHandler LengthChanged;
    public event EventHandler VolumeChanged;
    public event EventHandler IsMuteChanged;

    public void LoadMedia(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A media file path is required.", nameof(filePath));
        }

        Media next = new Media(_libVlc, new Uri(Path.GetFullPath(filePath)));
        Media previous = _media;
        _media = next;
        VlcMediaPlayer.Media = next;
        previous?.Dispose();
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MediaPlayer = null;
        _media?.Dispose();
        VlcMediaPlayer?.Dispose();
        _libVlc?.Dispose();
        GC.SuppressFinalize(this);
    }
}
