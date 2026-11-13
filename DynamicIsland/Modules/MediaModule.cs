using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Modules;

/// <summary>Now-playing info and transport controls for whatever app Windows considers the current media session.</summary>
class MediaModule : INotifyPropertyChanged
{
    const double CoverHeight = 82, MaxCoverWidth = 132;
    static readonly TimeSpan LongMedia = TimeSpan.FromMinutes(4); // beyond this, show the ±10s buttons

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    string _sourceId = "";

    // timeline snapshot, extrapolated between updates
    TimeSpan _start, _end, _position;
    DateTimeOffset _stamp;
    bool _tracking;

    // players that don't publish to the Windows media controls (VLC...): found through their window + audio activity
    readonly DispatcherTimer _legacyTimer = new() { Interval = LegacyEvery };
    LegacyPlayers.Found? _legacy;
    readonly VlcWeb? _vlc = VlcWeb.FromSettings(); // VLC's optional web interface: exact state, position and seeking
    bool _vlcAlive, _vlcBusy;
    static readonly TimeSpan LegacyEvery = TimeSpan.FromSeconds(2.5), LegacyEveryWhenOpen = TimeSpan.FromSeconds(1);

    bool _hasMedia, _isPlaying, _isLong, _artIsLogo, _hasTimeline;
    string _title = "", _artist = "", _elapsed = "0:00", _total = "0:00";
    ImageSource? _art;
    double _progress, _coverWidth = CoverHeight;

    public bool HasMedia { get => _hasMedia; private set => Set(ref _hasMedia, value); }

    // Skipping to the next track makes players briefly report "nothing" before the new track appears: wait a moment before the player goes away
    readonly DispatcherTimer _lost = new() { Interval = TimeSpan.FromSeconds(2.5) };
    void SetHasMedia(bool has)
    {
        _lost.Stop();
        if (has || !_hasMedia) { HasMedia = has; return; }
        _lost.Start();
    }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public ImageSource? Art { get => _art; private set => Set(ref _art, value); }
    public bool ArtIsLogo { get => _artIsLogo; private set { if (Set(ref _artIsLogo, value)) Raise(nameof(ArtIsPicture)); } }
    public bool ArtIsPicture => !ArtIsLogo;
    public double CoverWidth { get => _coverWidth; private set => Set(ref _coverWidth, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Elapsed { get => _elapsed; private set => Set(ref _elapsed, value); }
    public string Total { get => _total; private set => Set(ref _total, value); }
    public bool IsLong { get => _isLong; private set => Set(ref _isLong, value); }
    /// <summary>The player reports a length, so a progress bar makes sense (players found through their window don't).</summary>
    public bool HasTimeline { get => _hasTimeline; private set => Set(ref _hasTimeline, value); }
    public bool IsPlaying
    {
        get => _isPlaying;
        private set { if (Set(ref _isPlaying, value)) { Raise(nameof(PlayGlyph)); UpdateTick(); } }
    }
    public string PlayGlyph => IsPlaying ? "" : ""; // Segoe Fluent Icons: pause / play

    public ICommand Previous { get; }
    public ICommand PlayPause { get; }
    public ICommand Next { get; }
    public ICommand Back10 { get; }
    public ICommand Forward10 { get; }
    public ICommand OpenSource { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MediaModule()
    {
        Previous = new RelayCommand(() => { if (_legacy is { } l) LegacyCommand(l, 12, "pl_previous"); else _ = _session?.TrySkipPreviousAsync(); });
        PlayPause = new RelayCommand(() => { if (_legacy is { } l) LegacyCommand(l, 14, "pl_pause"); else _ = _session?.TryTogglePlayPauseAsync(); });
        Next = new RelayCommand(() => { if (_legacy is { } l) LegacyCommand(l, 11, "pl_next"); else _ = _session?.TrySkipNextAsync(); });
        Back10 = new RelayCommand(() => SeekBy(-10));
        Forward10 = new RelayCommand(() => SeekBy(10));
        OpenSource = new RelayCommand(() => SourceApp.Activate(_sourceId, Title));
        _tick.Tick += (_, _) => UpdateProgress();
        _lost.Tick += (_, _) => { _lost.Stop(); HasMedia = false; };
        _legacyTimer.Tick += (_, _) => LegacyTick();
        _legacyTimer.Start();
        _ = InitAsync();
    }

    /// <summary>Progress only needs to move while someone is looking at it (island expanded) and music is playing.</summary>
    public bool Tracking
    {
        set { _tracking = value; UpdateTick(); _legacyTimer.Interval = value ? LegacyEveryWhenOpen : LegacyEvery; if (value) { UpdateProgress(); LegacyTick(); } }
    }

    public void Seek(double fraction)
    {
        if (_legacy is not null && _vlcAlive && _vlc is not null && _end > _start)
        {
            var to = _start + (_end - _start) * Math.Clamp(fraction, 0, 1);
            _ = _vlc.SendAsync("seek", ((int)to.TotalSeconds).ToString());
            _position = to; _stamp = DateTimeOffset.Now;
            UpdateProgress();
            return;
        }
        if (_session is null || _end <= _start) return;
        var target = _start + (_end - _start) * Math.Clamp(fraction, 0, 1);
        _ = _session.TryChangePlaybackPositionAsync(target.Ticks);
        _position = target; _stamp = DateTimeOffset.Now;
        UpdateProgress();
    }

    void SeekBy(double seconds)
    {
        var length = _end - _start;
        if (length <= TimeSpan.Zero) return;
        Seek((Now().TotalSeconds + seconds) / length.TotalSeconds);
    }

    async Task InitAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(Attach);
        await _ui.InvokeAsync(Attach);
    }

    void Attach()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMedia;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnPlayback;
        }
        _session = _manager?.GetCurrentSession();
        if (_session is null) { _legacy = null; SetHasMedia(false); IsPlaying = false; HasTimeline = false; LegacyTick(); return; }
        _legacy = null;

        _session.MediaPropertiesChanged += OnMedia;
        _session.PlaybackInfoChanged += OnPlayback;
        _session.TimelinePropertiesChanged += OnPlayback;
        OnMedia(_session, null);
        OnPlayback(_session, null);
    }

    async void OnMedia(GlobalSystemMediaTransportControlsSession s, object? _)
    {
        var props = await s.TryGetMediaPropertiesAsync();
        var art = await LoadArt(props.Thumbnail);
        var id = s.SourceAppUserModelId;
        await _ui.InvokeAsync(() =>
        {
            if (props.Title.Length == 0 && props.Artist.Length == 0 && _hasMedia) { SetHasMedia(false); return; } // between tracks: keep showing the last one
            _sourceId = id;
            Title = props.Title; Artist = props.Artist;

            // No artwork (e.g. VLC): show the player's own logo instead of an empty tile
            var logo = art is null ? SourceApp.Icon(id) : null;
            Art = art ?? logo;
            ArtIsLogo = logo is not null;
            // Wide thumbnails (YouTube) get a wider cover so they aren't cropped to a square
            CoverWidth = art is { Height: > 0 } ? Math.Clamp(CoverHeight * art.Width / art.Height, CoverHeight, MaxCoverWidth) : CoverHeight;

            SetHasMedia(Title.Length > 0 || Artist.Length > 0);
        });
    }

    void OnPlayback(GlobalSystemMediaTransportControlsSession s, object? _)
    {
        var playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var tl = s.GetTimelineProperties();
        _ui.InvokeAsync(() =>
        {
            _start = tl.StartTime; _end = tl.EndTime; _position = tl.Position; _stamp = tl.LastUpdatedTime;
            IsLong = _end - _start > LongMedia;
            HasTimeline = _end > _start;
            IsPlaying = playing;
            UpdateProgress();
        });
    }

    TimeSpan Now()
    {
        var now = _position - _start + (IsPlaying ? DateTimeOffset.Now - _stamp : TimeSpan.Zero);
        var length = _end - _start;
        if (now < TimeSpan.Zero) now = TimeSpan.Zero;
        return length > TimeSpan.Zero && now > length ? length : now;
    }

    void UpdateProgress()
    {
        var length = _end - _start;
        var now = Now();
        Progress = length > TimeSpan.Zero ? now / length : 0;
        Elapsed = Format(now);
        Total = Format(length);
    }

    // ---- players without Windows media controls (VLC, mpv, MPC, PotPlayer...) ----

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    /// <summary>Controls a player found by its window: through VLC's web interface when that is available, otherwise as a media-key message to its window.</summary>
    void LegacyCommand(LegacyPlayers.Found player, int appCommand, string webCommand)
    {
        if (_vlcAlive && _vlc is not null && player.Process == "vlc") _ = _vlc.SendAsync(webCommand);
        else PostMessage(player.Window, 0x0319 /* WM_APPCOMMAND */, player.Window, (IntPtr)(appCommand << 16));

        var recheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; // then re-read what the player is doing
        recheck.Tick += (_, _) => { recheck.Stop(); LegacyTick(); };
        recheck.Start();
    }

    /// <summary>Asks VLC's web interface (when configured) for its exact state. Its answer wins over every guess below.</summary>
    async Task PollVlcAsync(VlcWeb web)
    {
        if (_vlcBusy) return;
        _vlcBusy = true;
        try
        {
            var status = await web.GetStatusAsync();
            await _ui.InvokeAsync(() =>
            {
                if (_legacy is not { Process: "vlc" }) { _vlcAlive = false; return; }
                if (status is not { } s || s.State is not ("playing" or "paused" or "stopped")) { _vlcAlive = false; return; }

                _vlcAlive = true;
                IsPlaying = s.State == "playing";
                _start = TimeSpan.Zero; _end = TimeSpan.FromSeconds(s.Length); _position = TimeSpan.FromSeconds(s.Time); _stamp = DateTimeOffset.Now;
                HasTimeline = s.Length > 0;
                IsLong = _end > LongMedia;
                if (s.Title.Length > 0 && s.Title != Title) Title = s.Title;
                UpdateProgress();
            });
        }
        finally { _vlcBusy = false; }
    }
    void LegacyTick()
    {
        if (_session is not null) { _legacy = null; return; } // Windows knows about a player: believe it

        var found = LegacyPlayers.Find();
        if (found is not { } f)
        {
            if (_legacy is not null) { _legacy = null; _vlcAlive = false; SetHasMedia(false); IsPlaying = false; HasTimeline = false; IsLong = false; }
            return;
        }

        var newPlayer = _legacy is null || _legacy.Value.Process != f.Process;
        var newTitle = _legacy is null || _legacy.Value.Title != f.Title;
        _legacy = f;

        if (newPlayer || newTitle)
        {
            _sourceId = f.Process;
            Title = f.Title;
            Artist = f.Display;
            Art = SourceApp.Icon(f.Process); // no artwork exists for these: show the player's own logo
            ArtIsLogo = Art is not null;
            CoverWidth = CoverHeight;
        }
        if (!_vlcAlive || f.Process != "vlc")
        {
            // no exact information: a guess from the window and the sound
            _vlcAlive = false;
            HasTimeline = false; IsLong = false; Progress = 0; Elapsed = ""; Total = "";
            IsPlaying = f.Playing;
        }
        SetHasMedia(true);
        if (f.Process == "vlc" && _vlc is not null) _ = PollVlcAsync(_vlc);
    }
    void UpdateTick() => _tick.IsEnabled = _tracking && IsPlaying;

    static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    async Task<BitmapSource?> LoadArt(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;
            return await _ui.InvokeAsync(() =>
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.DecodePixelHeight = 220; // keeps memory small; width follows the aspect ratio
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return (BitmapSource)img;
            });
        }
        catch { return null; }
    }

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    void Raise(string? name) => PropertyChanged?.Invoke(this, new(name));
}
