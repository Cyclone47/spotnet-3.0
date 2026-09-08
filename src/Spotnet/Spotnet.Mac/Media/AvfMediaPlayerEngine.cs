using System;

namespace Spotnet.Mac.Media;

/// <summary>
/// De echte afspeel-engine: macOS AVFoundation (AVPlayer + AVPlayerLayer) via
/// objc_msgSend — het Mac-tegenhanger van Windows' <c>VlcPlayer</c>.
///
/// De engine bouwt één keer een NSView met een AVPlayerLayer (de video surface
/// die de speler-UI via NativeControlHost insluit) en één AVPlayer; elke
/// <see cref="Load"/> wisselt alleen het AVPlayerItem — precies zoals
/// Windows' <c>VlcPlayer.LoadMedia</c> alleen de media wisselt. Positie/duur
/// leest de speler-VM door polling, net zoals Windows' player met timers en
/// stop-detectie werkt.
/// </summary>
public sealed class AvfMediaPlayerEngine : IMediaPlayerEngine
{
    private static readonly Lazy<bool> Supported = new(() => MacMediaNative.TryLoadFrameworks());

    /// <summary>AVFoundation zit op elke macOS; alleen guard voor het onwaarschijnlijke geval.</summary>
    public static bool IsSupported => Supported.Value;

    private static readonly IntPtr SelAlloc = MacMediaNative.Selector("alloc");
    private static readonly IntPtr SelInit = MacMediaNative.Selector("init");
    private static readonly IntPtr SelInitWithFrame = MacMediaNative.Selector("initWithFrame:");
    private static readonly IntPtr SelPlay = MacMediaNative.Selector("play");
    private static readonly IntPtr SelPause = MacMediaNative.Selector("pause");
    private static readonly IntPtr SelRate = MacMediaNative.Selector("rate");
    private static readonly IntPtr SelSetRate = MacMediaNative.Selector("setRate:");
    private static readonly IntPtr SelSetVolume = MacMediaNative.Selector("setVolume:");
    private static readonly IntPtr SelCurrentTime = MacMediaNative.Selector("currentTime");
    private static readonly IntPtr SelSeekTo = MacMediaNative.Selector("seekTo:");
    private static readonly IntPtr SelDuration = MacMediaNative.Selector("duration");
    private static readonly IntPtr SelCurrentItem = MacMediaNative.Selector("currentItem");
    private static readonly IntPtr SelReplaceItem = MacMediaNative.Selector("replaceCurrentItemWithPlayerItem:");
    private static readonly IntPtr SelPlayerItemWithUrl = MacMediaNative.Selector("playerItemWithURL:");
    private static readonly IntPtr SelFileUrlWithPath = MacMediaNative.Selector("fileURLWithPath:");
    private static readonly IntPtr SelSetWantsLayer = MacMediaNative.Selector("setWantsLayer:");
    private static readonly IntPtr SelLayer = MacMediaNative.Selector("layer");
    private static readonly IntPtr SelBounds = MacMediaNative.Selector("bounds");
    private static readonly IntPtr SelSetFrame = MacMediaNative.Selector("setFrame:");
    private static readonly IntPtr SelSetPlayer = MacMediaNative.Selector("setPlayer:");
    private static readonly IntPtr SelSetVideoGravity = MacMediaNative.Selector("setVideoGravity:");
    private static readonly IntPtr SelSetAutoresizingMask = MacMediaNative.Selector("setAutoresizingMask:");
    private static readonly IntPtr SelAddSublayer = MacMediaNative.Selector("addSublayer:");

    private IntPtr _player;
    private IntPtr _view;
    private IntPtr _playerLayer;
    private int _volume = 100;
    private bool _isMute;
    private bool _disposed;

    /// <summary>Het NSView-oppervlak; pas geldig na de eerste Load.</summary>
    public IntPtr VideoHandle => _view;

    public bool IsPlaying => _player != IntPtr.Zero && MacMediaNative.objc_msgSend_f32(_player, SelRate) > 0.01f;

    public TimeSpan Time
    {
        get
        {
            if (_player == IntPtr.Zero)
            {
                return TimeSpan.Zero;
            }

            MacMediaNative.objc_msgSend_stret(out var t, _player, SelCurrentTime);
            return TimeSpan.FromSeconds(Math.Max(0, t.Seconds));
        }
        set
        {
            if (_player != IntPtr.Zero)
            {
                MacMediaNative.objc_msgSend_with_cmtime(_player, SelSeekTo, CMTime.FromSeconds(value.TotalSeconds));
            }
        }
    }

    public TimeSpan Length
    {
        get
        {
            var item = MacMediaNative.objc_msgSend(_player, SelCurrentItem);
            if (item == IntPtr.Zero)
            {
                return TimeSpan.Zero;
            }

            MacMediaNative.objc_msgSend_stret(out var duration, item, SelDuration);
            var seconds = duration.Seconds;
            // NaN of oneindig (nog niet geanalyseerd / live stream) → 0.
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(seconds);
        }
    }

    public double Position
    {
        get
        {
            var length = Length.TotalSeconds;
            return length <= 0 ? 0 : Math.Clamp(Time.TotalSeconds / length, 0, 1);
        }
        set
        {
            var length = Length.TotalSeconds;
            if (length > 0)
            {
                Time = TimeSpan.FromSeconds(Math.Clamp(value, 0, 1) * length);
            }
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 200);
            ApplyVolume();
        }
    }

    public bool IsMute
    {
        get => _isMute;
        set
        {
            _isMute = value;
            ApplyVolume();
        }
    }

    private void ApplyVolume()
    {
        if (_player == IntPtr.Zero)
        {
            return;
        }

        var effective = _isMute ? 0 : _volume / 100.0;
        MacMediaNative.objc_msgSend_with_double(_player, SelSetVolume, Math.Clamp(effective, 0, 1));
    }

    /// <summary>Maakt bij de eerste keer een AVPlayer + NSView met AVPlayerLayer.</summary>
    private void EnsurePlayerAndView()
    {
        if (_player != IntPtr.Zero)
        {
            return;
        }

        _player = MacMediaNative.objc_msgSend(MacMediaNative.Class("AVPlayer"), SelAlloc);
        MacMediaNative.objc_msgSend(_player, SelInit);

        // Container-view met layer, zodat NativeControlHost hem kan insluiten.
        _view = MacMediaNative.objc_msgSend(MacMediaNative.Class("NSView"), SelAlloc);
        MacMediaNative.objc_msgSend(_view, SelInitWithFrame, CGRect.Zero);
        MacMediaNative.objc_msgSend_with_byte(_view, SelSetWantsLayer, (byte)1);

        var viewLayer = MacMediaNative.objc_msgSend(_view, SelLayer);
        _playerLayer = MacMediaNative.objc_msgSend(MacMediaNative.Class("AVPlayerLayer"), SelAlloc);
        MacMediaNative.objc_msgSend(_playerLayer, SelInit);

        MacMediaNative.objc_msgSend(_playerLayer, SelSetPlayer, _player);
        MacMediaNative.objc_msgSend(_playerLayer, SelSetFrame, CGRect.Zero);
        MacMediaNative.objc_msgSend(_playerLayer, SelSetVideoGravity, MacMediaNative.ToNSString("AVLayerVideoGravityResizeAspect"));
        // kCALayerWidthSizable (2) | kCALayerHeightSizable (16): volg de view bij resize.
        MacMediaNative.objc_msgSend(_playerLayer, SelSetAutoresizingMask, (UIntPtr)18);
        MacMediaNative.objc_msgSend(viewLayer, SelAddSublayer, _playerLayer);
    }

    public void Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Een pad naar het mediabestand is vereist.", nameof(filePath));
        }

        EnsurePlayerAndView();

        var nsPath = MacMediaNative.ToNSString(filePath);
        var url = MacMediaNative.objc_msgSend(MacMediaNative.Class("NSURL"), SelFileUrlWithPath, nsPath);
        var item = MacMediaNative.objc_msgSend(MacMediaNative.Class("AVPlayerItem"), SelPlayerItemWithUrl, url);
        MacMediaNative.objc_msgSend(_player, SelReplaceItem, item);
    }

    /// <summary>Past NSView en AVPlayerLayer aan de actuele hostgrootte aan.</summary>
    public void Resize(double width, double height)
    {
        if (_view == IntPtr.Zero)
        {
            return;
        }

        var frame = new CGRect { Width = Math.Max(0, width), Height = Math.Max(0, height) };
        MacMediaNative.objc_msgSend(_view, SelSetFrame, frame);
        if (_playerLayer != IntPtr.Zero)
        {
            MacMediaNative.objc_msgSend(_playerLayer, SelSetFrame, frame);
        }
    }

    public void Play()
    {
        if (_player != IntPtr.Zero)
        {
            MacMediaNative.objc_msgSend(_player, SelPlay);
        }
    }

    public void Pause()
    {
        if (_player != IntPtr.Zero)
        {
            MacMediaNative.objc_msgSend(_player, SelPause);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_player != IntPtr.Zero)
        {
            MacMediaNative.objc_msgSend(_player, SelPause);
            MacMediaNative.objc_msgSend_with_double(_player, SelSetRate, 0.0);
        }

        // De NSView blijft bewust in leven: Avalonia's NativeControlHost verwijdert
        // hem bij detach uit de window, en hem vrijgeven terwijl de host hem nog
        // vasthoudt zou een dangling pointer opleveren. Eén view per sessie is verwaarloosbaar.
        _player = IntPtr.Zero;
        _view = IntPtr.Zero;
        _playerLayer = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
}
