using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Services;

/// <summary>What a beamer needs per frame.</summary>
public readonly record struct GameTickInfo(double PositionSec, double Beat);

/// <summary>
/// Transport state machine for the game channel (docs/03-game-engine.md). Single source of truth; the DJ
/// window drives it, beamers observe it. Runs the 60 Hz game tick while playing.
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(16);
    // Songs (esp. YouTube) often run on after the last note; stop this long after it.
    private const double TailAfterLastNoteSec = 4.0;

    private readonly MediaService _media;
    private readonly AudioInputService _audio;
    private readonly PlayersService _players;
    private readonly IDisplayService _displays;
    private readonly AppSettingsService _settings;
    private readonly SongResolver _resolver;
    private readonly NotificationService _notifications;
    private readonly ThumbnailService _thumbnails;
    private readonly ILogger<PlaybackService> _log;
    private readonly HashSet<DisplayId> _countdownDone = [];
    private CancellationTokenSource? _tickCts;
    private Task? _tickLoop;
    private PlaybackState _state = PlaybackState.Idle;

    public PlaybackService(MediaService media, AudioInputService audio, PlayersService players, IDisplayService displays, AppSettingsService settings, SongResolver resolver,
        NotificationService notifications, ThumbnailService thumbnails, ILogger<PlaybackService> log)
    {
        _media = media;
        _audio = audio;
        _players = players;
        _displays = displays;
        _settings = settings;
        _resolver = resolver;
        _notifications = notifications;
        _thumbnails = thumbnails;
        _log = log;
        _media.Game.EndReached += () => Dispatcher.UIThread.Post(() => { if (State is PlaybackState.Playing or PlaybackState.Paused) { Stop(); } });
        _media.Game.ErrorOccurred += e => Dispatcher.UIThread.Post(() => OnPlaybackError(e));
        _audio.GameMicLost += OnGameMicLost;
        _displays.OpenStateChanged += OnDisplayOpenStateChanged;
    }

    /// <summary>The game media failed (stream dropped, YouTube refused): stop and tell the DJ why.</summary>
    private void OnPlaybackError(string error)
    {
        LastError = error;
        if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown)
        {
            Stop();
            PlaybackError explained = PlaybackError.Explain(error);
            _notifications.ShowError("Playback stopped", explained.Reason, explained.Details);
        }
    }

    /// <summary>Raised on the UI thread.</summary>
    public event Action<PlaybackState>? StateChanged;
    /// <summary>Raised on the tick thread ~60×/s while playing. Handlers marshal themselves.</summary>
    public event Action<GameTickInfo>? Ticked;
    /// <summary>Raised on the tick thread right after <see cref="Ticked"/>.</summary>
    public event Action<IReadOnlyList<PitchTick>>? PitchTicked;

    public PlaybackState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            _log.LogInformation("Playback → {State}", value);
            StateChanged?.Invoke(value);
        }
    }

    public Song? Song { get; private set; }

    /// <summary>Song picture (cover, YouTube thumbnail, background) — get ready, countdown, score, Game Player box.</summary>
    public Bitmap? Picture { get; private set; }

    /// <summary>Backdrop (background, cover) behind the game for songs without video.</summary>
    public Bitmap? Backdrop { get; private set; }

    /// <summary>UI thread: <see cref="Picture"/> / <see cref="Backdrop"/> changed (they load after the song).</summary>
    public event Action? PicturesChanged;
    public MediaPlan? Plan { get; private set; }
    public GameSession? Session { get; private set; }
    public IGameClock? Clock => _media.Game.Clock;
    public Difficulty Difficulty => _settings.Difficulty;
    /// <summary>Global lyrics offset; the beamers and the scorer read game time as clock + this.</summary>
    public double LyricsOffsetSec => _settings.LyricsOffsetMs / 1000.0;
    /// <summary>Game time including the lyrics offset — what beamers and the scorer use.</summary>
    public double GamePositionSec => (Clock?.PositionSec ?? 0) + LyricsOffsetSec;
    public string? LastError { get; private set; }
    public bool IsBusy { get; private set; }

    public bool CanLoad => State is PlaybackState.Idle or PlaybackState.Loaded or PlaybackState.Preview or PlaybackState.Score && !IsBusy;
    public bool AnyDisplayOpen => _displays.IsOpen(DisplayId.Beamer1) || _displays.IsOpen(DisplayId.Beamer2);
    public bool CanPlay => PlaybackRules.CanPlay(State, AnyDisplayOpen) && !IsBusy;
    public bool CanPreview => PlaybackRules.CanGetReady(State, AnyDisplayOpen);
    public bool CanHome => PlaybackRules.CanHome(State);
    public bool CanPause => PlaybackRules.CanPause(State);
    public bool CanResume => PlaybackRules.CanResume(State);
    public bool CanStop => PlaybackRules.CanStop(State);

    /// <summary>Players that will sing: mic bound and assigned to an open display.</summary>
    public IReadOnlyList<PlayerConfig> ActivePlayers()
    {
        HashSet<int> onOpenDisplays = [];
        foreach (DisplayId id in (ReadOnlySpan<DisplayId>)[DisplayId.Beamer1, DisplayId.Beamer2])
        {
            if (_displays.IsOpen(id))
            {
                onOpenDisplays.UnionWith(_displays.GetConfig(id).PlayerIds);
            }
        }

        return _players.All.Where(p => p.Mic is not null && onOpenDisplays.Contains(p.Id)).ToList();
    }

    /// <summary>Validates, parses notes, resolves media and loads the game channel. Ends in Loaded (or throws).</summary>
    public async Task LoadAsync(Song song, CancellationToken ct = default)
    {
        if (!CanLoad)
        {
            return;
        }

        IsBusy = true;
        LastError = null;
        try
        {
            if (State != PlaybackState.Idle)
            {
                await ClearCoreAsync();
            }

            Song loaded = await _resolver.ResolveAsync(song, ct);

            MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia
            {
                AudioPath = loaded.AudioPath,
                VideoPath = loaded.VideoPath,
                YouTubeId = loaded.YouTubeId,
                BackgroundPath = loaded.BackgroundPath,
                CoverPath = loaded.CoverPath,
                VideoGapSec = loaded.VideoGapSec ?? 0,
                StartSec = loaded.StartSec,
                EndSec = loaded.EndMs is { } end ? end / 1000.0 : null,
            });

            await _media.Game.LoadAsync(plan, ct);
            Song = loaded;
            Plan = plan;
            State = PlaybackState.Loaded;
            _ = LoadPicturesAsync(loaded);
        }
        catch (MediaException ex)
        {
            LastError = ex.Message;
            PlaybackError explained = PlaybackError.Explain(ex.Message);
            throw new SongLoadException(explained.Reason, explained.Details);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Thumbnail download must not hold up the load: pictures follow; ignored if another song came meanwhile.</summary>
    private async Task LoadPicturesAsync(Song song)
    {
        Bitmap? picture = await SongImages.PictureAsync(song, _thumbnails);
        Bitmap? backdrop = await SongImages.BackdropAsync(song, _thumbnails);
        if (ReferenceEquals(Song, song))
        {
            SetPictures(picture, backdrop);
        }
    }

    private void SetPictures(Bitmap? picture, Bitmap? backdrop)
    {
        Picture = picture;
        Backdrop = backdrop;
        PicturesChanged?.Invoke();
    }

    public void Preview()
    {
        if (CanPreview)
        {
            RewindIfFinished();
            State = PlaybackState.Preview;
        }
    }

    /// <summary>Starts the countdown on every open beamer; the first one to finish starts the media. After Stop it replays from the start.</summary>
    public void Play()
    {
        if (!CanPlay)
        {
            return;
        }

        RewindIfFinished();
        _countdownDone.Clear();
        State = PlaybackState.Countdown;
    }

    /// <summary>Beamers back to their start view; the song stays loaded (rewound) and ready to play.</summary>
    public void Home()
    {
        if (CanHome)
        {
            RewindIfFinished();
            State = PlaybackState.Loaded;
        }
    }

    private void RewindIfFinished()
    {
        if (State == PlaybackState.Score)
        {
            _media.Game.Seek(0);
        }
    }

    /// <summary>Called by each beamer when its 3-2-1 finishes. The first call starts the song; later ones are ignored.</summary>
    public void CountdownDone(DisplayId display)
    {
        if (State != PlaybackState.Countdown || !_countdownDone.Add(display) || _countdownDone.Count > 1)
        {
            return;
        }

        StartSong();
    }

    private void StartSong()
    {
        if (Song is null)
        {
            return;
        }

        IReadOnlyList<PlayerConfig> active = ActivePlayers();
        int tracks = Song.Notes!.Count;
        Session = new GameSession(Song, active.Select(p => new GamePlayer(p.Id, tracks > 1 ? (p.Id - 1) % tracks : 0, p.MicDelayMs)).ToList(), Difficulty);

        StartMics(active);
        _media.Game.Play();
        State = PlaybackState.Playing;
        StartTicker();
        _log.LogInformation("Song started: {Song} with players {Players}", Song.Title, string.Join(",", active.Select(p => p.Id)));
    }

    public void Pause()
    {
        if (CanPause)
        {
            _media.Game.Pause();
            State = PlaybackState.Paused;
        }
    }

    public void Resume()
    {
        if (CanResume)
        {
            _media.Game.Play();
            State = PlaybackState.Playing;
        }
    }

    private void StartMics(IReadOnlyList<PlayerConfig> active)
    {
        if (active.Count == 0)
        {
            return;
        }

        _audio.StartGameMics(active.Select(p => new MicSlot(p.Id, p.Mic!, p.InputGain, p.Threshold)).ToList());
        string? monitorOut = ResolveMonitorOutput();
        if (monitorOut is not null)
        {
            _audio.StartMonitor(monitorOut, _media.Game.ChannelOffset);
        }
    }

    /// <summary>
    /// A beamer closed while a song runs (by hand, from the Displays panel, or a failure): its singers can no longer
    /// see the notes, so the song stops like with Stop (score screen, song stays loaded).
    /// </summary>
    private void OnDisplayOpenStateChanged(DisplayId id, bool isOpen)
    {
        if (isOpen || !CanStop)
        {
            return;
        }

        Stop();
        _notifications.Warn($"Beamer {(int)id} closed — song stopped", "Open it again under Displays and press Play to sing the song again.");
    }

    /// <summary>
    /// A singer's mic died mid-song: stop the song (rewound, still loaded) and close the beamers — the setup has
    /// changed, the DJ sets it up again. The mic is unassigned by <see cref="AudioInputService"/> right after.
    /// </summary>
    private void OnGameMicLost(IReadOnlyList<PlayerConfig> players)
    {
        if (State is not (PlaybackState.Playing or PlaybackState.Paused))
        {
            return;
        }

        Stop();
        Home();
        foreach (DisplayId id in (ReadOnlySpan<DisplayId>)[DisplayId.Beamer1, DisplayId.Beamer2])
        {
            _displays.Close(id);
        }

        string who = players.Count > 0 ? string.Join(", ", players.Select(MicPresence.Describe)) : "A microphone";
        _notifications.Warn(
            "Microphone disconnected — song stopped",
            $"{who}. The beamers were closed; plug the mic in, assign it under Audio Input and start again.");
    }

    /// <summary>Ends the song: media paused, mics off, score screen. The song stays loaded.</summary>
    public void Stop()
    {
        if (!CanStop)
        {
            return;
        }

        StopTicker();
        _audio.StopAll();
        _media.Game.Pause();
        State = PlaybackState.Score;
    }

    private async Task ClearCoreAsync()
    {
        StopTicker();
        _audio.StopAll();
        await _media.Game.UnloadAsync();
        Song = null;
        SetPictures(null, null);
        Plan = null;
        Session = null;
        State = PlaybackState.Idle;
    }

    // ── 60 Hz game tick ─────────────────────────────────────────────────

    private void StartTicker()
    {
        StopTicker();
        _tickCts = new CancellationTokenSource();
        CancellationToken ct = _tickCts.Token;
        _tickLoop = Task.Run(() => TickLoopAsync(ct), ct);
    }

    private void StopTicker()
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;
        _tickLoop = null;
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(TickPeriod);
        double stopAfterSec = Session is { } s0 ? Core.Timing.BeatMath.SecondsAt(s0.LastBeat, s0.Song.Bpm, s0.Song.GapMs) + TailAfterLastNoteSec : double.MaxValue;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                IGameClock? clock = Clock;
                GameSession? session = Session;
                if (clock is null || session is null)
                {
                    continue;
                }

                double pos = clock.PositionSec + LyricsOffsetSec;
                Ticked?.Invoke(new GameTickInfo(pos, session.BeatAt(pos)));

                if (State == PlaybackState.Playing)
                {
                    IReadOnlyList<PitchTick> ticks = session.Tick(pos, id => _audio.Mics.Pipeline(id)?.LastMidiNote ?? -1);
                    PitchTicked?.Invoke(ticks);

                    if (pos >= stopAfterSec)
                    {
                        Dispatcher.UIThread.Post(Stop);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Maps the game channel's mpv output device onto the PortAudio device of the same name.</summary>
    private string? ResolveMonitorOutput()
    {
        string mpvId = _media.Game.DeviceId;
        IReadOnlyList<AudioDeviceInfo> outs = _audio.OutputDevices;
        if (mpvId == AudioOutputDevice.Auto.Id)
        {
            return outs.FirstOrDefault(d => d.IsDefaultOutput)?.Id ?? (outs.Count > 0 ? outs[0].Id : null);
        }

        // mpv ids look like "coreaudio/<uid>"; its device list carries the human name we can match on.
        try
        {
            string? name = _media.Game.ListAudioDevicesAsync().GetAwaiter().GetResult().FirstOrDefault(d => d.Id == mpvId)?.Name;
            return outs.FirstOrDefault(d => d.Name == name)?.Id ?? outs.FirstOrDefault(d => d.IsDefaultOutput)?.Id;
        }
        catch (MediaException)
        {
            return null;
        }
    }

    public void Dispose() => StopTicker();
}

/// <summary>A song cannot be loaded. <see cref="Exception.Message"/>: plain reasons, one per line; <see cref="Details"/>: raw cause.</summary>
public sealed class SongLoadException(string message, string? details = null) : Exception(message)
{
    public string? Details { get; } = details;

    public IReadOnlyList<string> Reasons => Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
