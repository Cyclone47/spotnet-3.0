using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Spotnet.Mac.Media;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;

namespace Spotnet.Mac.ViewModels;

/// <summary>
/// De speler achter het mediavoorbeeld in de Downloads-tab — de port van
/// Windows' <c>PlayerViewModel</c> + <c>Spotnet.Model.Player</c>: een playlist
/// van de afspeelbare bestanden bij een download, afspelen/pauzeren/stoppen,
/// automatisch doorgaan naar het volgende nummer, stop-detectie (positie die
/// niet vordert) en het voorbeeld dat al start tijdens het downloaden.
/// </summary>
public sealed class PlayerViewModel : ViewModelBase
{
    /// <summary>Hoe lang het voorbeeld wacht op de eerste data bij een lopende download.</summary>
    internal static readonly TimeSpan WaitForDataTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<IMediaPlayerEngine?>? _engineFactory;
    private readonly UserPreferencesService? _preferences;
    private readonly bool _createTimer;
    private readonly DispatcherTimer? _timer;
    private IMediaPlayerEngine? _engine;

    private DownloadItem? _parentItem;
    private MediaPlaylistItem? _currentItem;
    private bool _isPlaying;
    private bool _isStopDetected;
    private bool _hasMedia;
    private bool _isPlaylistVisible = true;
    private double _downloadProgress;
    private string _timeText = "0:00";
    private string _totalText = "0:00";
    private double _position;
    private bool _isSeeking;
    private int _volume = 100;
    private bool _isMute;
    private string? _lastError;

    // Stop-detectie: bij Windows gebeurt dit met een 1,5s-timer die kijkt of de
    // positie nog vordert; hier telt dezelfde logica tikken zonder vordering.
    private TimeSpan _lastTickTime;
    private int _stallTicks;
    private int _endedTickGuard;

    public PlayerViewModel(
        Func<IMediaPlayerEngine?>? engineFactory = null,
        UserPreferencesService? preferences = null,
        bool createTimer = true)
    {
        _engineFactory = engineFactory;
        _preferences = preferences;
        _createTimer = createTimer;

        if (createTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => OnTimerTick();
        }

        if (preferences != null)
        {
            _volume = Math.Clamp(preferences.Current.PlayerVolume, 0, 200);
        }

        PlayPauseCommand = new RelayCommand(() => PlayPause());
        StopCommand = new RelayCommand(() => FullStop());
        NextCommand = new RelayCommand(() => TryToPlayNext());
        TogglePlaylistCommand = new RelayCommand(() => IsPlaylistVisible = !IsPlaylistVisible);
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        PlayFromPlaylistCommand = new RelayCommand(param =>
        {
            if (param is MediaPlaylistItem item)
            {
                _ = PlayAsync(item, TimeSpan.Zero);
            }
        });
        OpenForItemCommand = new RelayCommand(param =>
        {
            if (param is DownloadItem item)
            {
                _ = OpenForItemAsync(item);
            }
        });
    }

    public ObservableCollection<MediaPlaylistItem> PlaylistItems { get; } = new();

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TogglePlaylistCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand PlayFromPlaylistCommand { get; }
    public ICommand OpenForItemCommand { get; }

    /// <summary>Het NSView-oppervlak is aangemaakt; de view bouwt hier een host omheen.</summary>
    public event Action<IntPtr>? VideoHandleCreated;

    /// <summary>Het voorbeeld is gestopt; de view ruimt de video-host op.</summary>
    public event Action? PlaybackStopped;

    public DownloadItem? ParentItem
    {
        get => _parentItem;
        private set
        {
            if (!ReferenceEquals(_parentItem, value))
            {
                _parentItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ParentTitle));
            }
        }
    }

    public string ParentTitle => ParentItem?.Title ?? string.Empty;

    public MediaPlaylistItem? CurrentItem
    {
        get => _currentItem;
        private set
        {
            if (!ReferenceEquals(_currentItem, value))
            {
                _currentItem = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                if (!value)
                {
                    IsStopDetected = false;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseIcon));
                OnPropertyChanged(nameof(IsCenterPlayVisible));
            }
        }
    }

    public bool IsStopDetected
    {
        get => _isStopDetected;
        private set
        {
            if (_isStopDetected != value)
            {
                _isStopDetected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCenterPlayVisible));
            }
        }
    }

    public bool HasMedia
    {
        get => _hasMedia;
        private set
        {
            if (_hasMedia != value)
            {
                _hasMedia = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set
        {
            if (_isPlaylistVisible != value)
            {
                _isPlaylistVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaylistEmpty => PlaylistItems.Count == 0;

    /// <summary>Voortgang van de download zelf, 0..1 (balk onder de video).</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            if (Math.Abs(_downloadProgress - value) > 0.0001)
            {
                _downloadProgress = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDownloadInProgress => ParentItem is { IsCompleted: false } && DownloadProgress < 1.0;

    public string TimeText
    {
        get => _timeText;
        private set
        {
            if (_timeText != value)
            {
                _timeText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TotalText
    {
        get => _totalText;
        private set
        {
            if (_totalText != value)
            {
                _totalText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Positie als fractie 0..1 voor de sliderschuif.</summary>
    public double Position
    {
        get => _position;
        set
        {
            if (Math.Abs(_position - value) < 0.0001)
            {
                return;
            }

            _position = value;
            OnPropertyChanged();
            SeekTo(value);
        }
    }

    /// <summary>Zet de positie zonder de engine aan te raken (tijdens het slepen).</summary>
    public void SeekPreview(double value)
    {
        _position = Math.Clamp(value, 0, 1);
        OnPropertyChanged(nameof(Position));
    }

    public bool IsSeeking
    {
        get => _isSeeking;
        set
        {
            if (_isSeeking != value)
            {
                _isSeeking = value;
                OnPropertyChanged();
            }
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 200);
            if (_volume == value)
            {
                return;
            }

            _volume = value;
            if (_engine != null)
            {
                _engine.Volume = value;
                if (value > 0 && IsMute)
                {
                    IsMute = false;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
            SaveVolumePreference();
        }
    }

    public bool IsMute
    {
        get => _isMute;
        set
        {
            if (_isMute == value)
            {
                return;
            }

            _isMute = value;
            if (_engine != null)
            {
                _engine.IsMute = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
        }
    }

    private void ToggleMute()
    {
        if (!IsMute && Volume == 0)
        {
            // Zoals Windows: uit mute halen geeft het volume vóór het dempen terug.
            Volume = 100;
        }
        else
        {
            IsMute = !IsMute;
        }
    }

    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";

    public string VolumeIcon => IsMute ? "🔇" : "🔊";

    /// <summary>Centrale afspeelknop over de video, zoals Windows' IsPlayButtonInTheCenterOfVideoVisible.</summary>
    public bool IsCenterPlayVisible => HasMedia && !IsPlaying && !IsStopDetected;

    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (_lastError != value)
            {
                _lastError = value;
                OnPropertyChanged();
            }
        }
    }

    // ── Openen en playlist ──────────────────────────────────────────────────

    /// <summary>
    /// Opent het voorbeeld bij een download: playlist verversen en — net als
    /// Windows' <c>SchedulePlay</c> — het eerste bestand spelen, eventueel pas
    /// als de eerste data binnenstroomt.
    /// </summary>
    public async Task OpenForItemAsync(DownloadItem item)
    {
        LastError = null;
        if (!ReferenceEquals(ParentItem, item) || !HasMedia)
        {
            FullStop(keepPanel: false);
            ParentItem = item;
            UpdatePlaylist();
        }
        else
        {
            ParentItem = item;
            UpdatePlaylist();
        }

        var first = PlaylistItems.FirstOrDefault();
        if (first == null && ParentItem is { IsCompleted: false })
        {
            // Een geïntegreerde download kan nog geen bestand hebben. Windows'
            // SchedulePlay wacht in dat geval op de eerste bytes in plaats van
            // meteen te melden dat de playlist leeg is.
            using var waitingCancel = new CancellationTokenSource(WaitForDataTimeout);
            try
            {
                if (await PlayerPlaylistService.WaitForPlayableFileAsync(ParentItem, WaitForDataTimeout, waitingCancel.Token))
                {
                    UpdatePlaylist();
                    first = PlaylistItems.FirstOrDefault();
                }
            }
            catch (OperationCanceledException)
            {
                // De gebruikersmelding hieronder beschrijft de timeout.
            }
        }

        if (first == null)
        {
            LastError = ParentItem == null
                ? "Geen download geselecteerd."
                : "Geen afspeelbare bestanden gevonden bij deze download.";
            return;
        }

        if (HasAnyFileWithData())
        {
            await PlayAsync(first, TimeSpan.Zero);
            return;
        }

        if (ParentItem.IsCompleted)
        {
            LastError = "De bestanden van deze download zijn (nog) leeg.";
            return;
        }

        // Zoals Windows' WaitForFilesToPlay: wachten tot er data is, dan spelen.
        using var cancel = new CancellationTokenSource(WaitForDataTimeout);
        try
        {
            if (await PlayerPlaylistService.WaitForPlayableFileAsync(ParentItem, WaitForDataTimeout, cancel.Token))
            {
                UpdatePlaylist();
                first = PlaylistItems.FirstOrDefault();
                if (first != null)
                {
                    await PlayAsync(first, TimeSpan.Zero);
                }
            }
            else
            {
                LastError = "Nog geen mediadata ontvangen om af te spelen.";
            }
        }
        catch (OperationCanceledException)
        {
            LastError = "Nog geen mediadata ontvangen om af te spelen.";
        }
    }

    private bool HasAnyFileWithData()
    {
        return PlaylistItems.Any(item => item.FileSizeBytes > 0);
    }

    /// <summary>
    /// Synchroniseert de playlist met de schijf — de port van Windows'
    /// <c>UpdatePlaylist</c>: weggehaalde bestanden verdwijnen, nieuwe komen erbij,
    /// <c>__unpack</c>-mappen worden overgeslagen (dat doet de scanner).
    /// </summary>
    public void UpdatePlaylist()
    {
        if (ParentItem == null)
        {
            return;
        }

        var files = PlayerPlaylistService.GetFilesToPlay(ParentItem);
        if (files.Count == 0)
        {
            PlaylistItems.Clear();
            OnPropertyChanged(nameof(IsPlaylistEmpty));
            return;
        }

        foreach (var stale in PlaylistItems.Where(p => !files.Contains(p.FileFullPath)).ToList())
        {
            PlaylistItems.Remove(stale);
            if (ReferenceEquals(CurrentItem, stale))
            {
                CurrentItem = null;
            }
        }

        foreach (var file in files.Where(file => PlaylistItems.All(p => p.FileFullPath != file)))
        {
            PlaylistItems.Add(new MediaPlaylistItem(file));
        }

        OnPropertyChanged(nameof(IsPlaylistEmpty));
    }

    // ── Afspelen ────────────────────────────────────────────────────────────

    private IMediaPlayerEngine? EnsureEngine()
    {
        if (_engine != null)
        {
            return _engine;
        }

        var factory = _engineFactory ?? DefaultEngineFactory;
        var engine = factory();
        if (engine == null)
        {
            LastError = "Mediaweergave is op dit systeem niet beschikbaar.";
            return null;
        }

        _engine = engine;
        engine.Volume = Volume;
        engine.IsMute = IsMute;
        return engine;
    }

    private static IMediaPlayerEngine? DefaultEngineFactory() =>
        AvfMediaPlayerEngine.IsSupported ? new AvfMediaPlayerEngine() : null;

    public Task PlayAsync(MediaPlaylistItem item, TimeSpan startPosition)
    {
        var engine = EnsureEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        LastError = null;
        CurrentItem = item;
        MarkPlaylistItemAsPlaying(item);

        engine.Load(item.FileFullPath);
        VideoHandleCreated?.Invoke(engine.VideoHandle);
        engine.Play();
        if (startPosition > TimeSpan.Zero)
        {
            engine.Time = startPosition;
        }

        IsStopDetected = false;
        HasMedia = true;
        IsPlaying = true;
        _stallTicks = 0;
        _lastTickTime = engine.Time;
        _endedTickGuard = 0;
        _timer?.Start();
        OnTimerTick();
        return Task.CompletedTask;
    }

    private void MarkPlaylistItemAsPlaying(MediaPlaylistItem? item)
    {
        foreach (var playlistItem in PlaylistItems)
        {
            playlistItem.IsPlaying = ReferenceEquals(playlistItem, item);
        }
    }

    public void Resize(double width, double height) => _engine?.Resize(width, height);

    public void Resume()
    {
        if (_isPlaying || _engine == null)
        {
            return;
        }

        if (IsStopDetected || CurrentItem == null)
        {
            // Zoals Windows: na een stop-detectie opnieuw vanaf het huidige item.
            if (CurrentItem != null)
            {
                _ = PlayAsync(CurrentItem, _lastTickTime);
            }

            return;
        }

        _engine.Play();
        IsPlaying = true;
        _stallTicks = 0;
    }

    public void Pause()
    {
        if (!_isPlaying)
        {
            return;
        }

        _engine?.Pause();
        IsPlaying = false;
    }

    public void PlayPause()
    {
        if (!HasMedia)
        {
            if (ParentItem != null)
            {
                _ = OpenForItemAsync(ParentItem);
            }

            return;
        }

        if (_engine == null || IsStopDetected || (CurrentItem != null && _engine.Length == TimeSpan.Zero && !_isPlaying))
        {
            if (CurrentItem != null)
            {
                _ = PlayAsync(CurrentItem, TimeSpan.Zero);
            }
            else if (ParentItem != null)
            {
                _ = OpenForItemAsync(ParentItem);
            }

            return;
        }

        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Resume();
        }
    }

    /// <summary>Doorspoelen in seconden, zoals Windows' ChangeTime.</summary>
    public void ChangeTime(int seconds)
    {
        if (_engine == null)
        {
            return;
        }

        var target = _engine.Time + TimeSpan.FromSeconds(seconds);
        var length = _engine.Length;
        if (length > TimeSpan.Zero && target > length)
        {
            target = length - TimeSpan.FromMilliseconds(500);
        }

        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        _engine.Time = target;
        OnTimerTick();
    }

    public void SeekStarted() => IsSeeking = true;

    public void SeekCompleted()
    {
        if (IsSeeking)
        {
            IsSeeking = false;
            SeekTo(Position);
        }
    }

    public void SeekTo(double fraction)
    {
        if (_engine == null || !HasMedia)
        {
            return;
        }

        _engine.Position = fraction;
        RestartStopDetection();
        OnTimerTick();
    }

    private void RestartStopDetection()
    {
        _stallTicks = 0;
        IsStopDetected = false;
    }

    /// <summary>
    /// Volledige stop — de port van Windows' <c>FullStop</c>/<c>Dispose</c>:
    /// pauzeer, ruim de status op en laat de view de video-host weghalen.
    /// </summary>
    public void FullStop(bool keepPanel = true)
    {
        IsPlaying = false;
        _timer?.Stop();
        _engine?.Pause();
        _engine?.Dispose();
        _engine = null;
        MarkPlaylistItemAsPlaying(null);
        CurrentItem = null;
        HasMedia = keepPanel && PlaylistItems.Count > 0;
        TimeText = "0:00";
        TotalText = "0:00";
        _position = 0;
        OnPropertyChanged(nameof(Position));
        RestartStopDetection();
        PlaybackStopped?.Invoke();
    }

    /// <summary>Een verwijderde download uit de speler halen.</summary>
    public void OnItemRemoved(DownloadItem item)
    {
        if (ReferenceEquals(ParentItem, item))
        {
            FullStop(keepPanel: false);
            ParentItem = null;
        }

        for (var i = PlaylistItems.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(item.DownloadDir)
                && PlaylistItems[i].FileFullPath.StartsWith(item.DownloadDir, StringComparison.OrdinalIgnoreCase))
            {
                PlaylistItems.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(IsPlaylistEmpty));
    }

    // ── Timer / polling ─────────────────────────────────────────────────────

    /// <summary>
    /// De 250ms-tik: teksten en schuifposities verversen, de downloadvoortgang
    /// bijwerken, stop-detectie en doorgaan naar het volgende nummer.
    /// </summary>
    internal void OnTimerTick()
    {
        var engine = _engine;
        if (engine == null)
        {
            return;
        }

        var time = engine.Time;
        var length = engine.Length;

        TimeText = MediaPlaylistItem.FormatLength(time);
        TotalText = MediaPlaylistItem.FormatLength(length);
        if (CurrentItem != null)
        {
            CurrentItem.Length = length;
        }

        if (!_isSeeking && length > TimeSpan.Zero)
        {
            _position = Math.Clamp(time.TotalSeconds / length.TotalSeconds, 0, 1);
            OnPropertyChanged(nameof(Position));
        }

        // Downloadvoortgang: bij Windows uit het download-item, hier hetzelfde.
        var progress = ParentItem == null || ParentItem.IsCompleted ? 1.0 : ParentItem.Progress;
        DownloadProgress = Math.Clamp(progress, 0, 1);
        OnPropertyChanged(nameof(IsDownloadInProgress));

        if (IsPlaying)
        {
            // Eind-detectie, zoals Windows' EndReached: net voor het einde stoppen
            // en doorgaan met het volgende nummer.
            if (length > TimeSpan.Zero && time >= length - TimeSpan.FromMilliseconds(300) && _endedTickGuard == 0)
            {
                _endedTickGuard = 1;
                IsPlaying = false;
                engine.Pause();
                TryToPlayNext();
                return;
            }

            // Stop-detectie: positie die 1,5s (6 tikken) stilvalt.
            if (time == _lastTickTime)
            {
                _stallTicks++;
                if (_stallTicks >= 6)
                {
                    IsStopDetected = true;
                }
            }
            else
            {
                _stallTicks = 0;
                _endedTickGuard = 0;
                IsStopDetected = false;
                _lastTickTime = time;
            }
        }
    }

    /// <summary>Doorgaan naar het volgende playlistnummer, zoals Windows' TryToPlayNext.</summary>
    public void TryToPlayNext()
    {
        if (CurrentItem == null)
        {
            return;
        }

        var next = PlaylistItems.IndexOf(CurrentItem) + 1;
        if (next < PlaylistItems.Count)
        {
            _ = PlayAsync(PlaylistItems[next], TimeSpan.Zero);
        }
        else
        {
            // Laatste nummer klaar: like Windows' stop na de playlist.
            IsPlaying = false;
            _engine?.Pause();
        }
    }

    // ── Algemeen ────────────────────────────────────────────────────────────

    private void SaveVolumePreference()
    {
        if (_preferences == null)
        {
            return;
        }

        _preferences.Current.PlayerVolume = Volume;
        try
        {
            _preferences.Save(_preferences.Current);
        }
        catch (Exception)
        {
            // Voorkeur niet kunnen wegschrijven mag het afspelen niet blokkeren.
        }
    }
}
