using System.Runtime;
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
using UltrastarDJ.Core.Timing;
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
    private readonly OutputsService _outputs;
    private readonly LoadFailureService _failures;
    private readonly LibraryService _library;
    private readonly YtDlpService _ytDlp;
    private readonly NotificationService _notifications;
    private readonly ThumbnailService _thumbnails;
    private readonly ILogger<PlaybackService> _log;
    private readonly HashSet<DisplayId> _countdownDone = [];
    private CancellationTokenSource? _tickCts;
    private Task? _tickLoop;
    private PlaybackState _state = PlaybackState.Idle;

    public PlaybackService(MediaService media, AudioInputService audio, PlayersService players, IDisplayService displays, AppSettingsService settings, SongResolver resolver,
        OutputsService outputs, LoadFailureService failures, LibraryService library, YtDlpService ytDlp, NotificationService notifications, ThumbnailService thumbnails, ILogger<PlaybackService> log)
    {
        _media = media;
        _audio = audio;
        _players = players;
        _displays = displays;
        _settings = settings;
        _resolver = resolver;
        _outputs = outputs;
        _failures = failures;
        _library = library;
        _ytDlp = ytDlp;
        _notifications = notifications;
        _thumbnails = thumbnails;
        _log = log;
        _media.Game.EndReached += () => Dispatcher.UIThread.Post(() => { if (State is PlaybackState.Playing or PlaybackState.Paused) { Stop(); } });
        _media.Game.ErrorOccurred += e => Dispatcher.UIThread.Post(() => OnPlaybackError(e));
        _audio.GameMicLost += OnGameMicLost;
        _outputs.GameOutputLost += OnGameOutputLost;
        _displays.OpenStateChanged += OnDisplayOpenStateChanged;
        _displays.ScreenLost += OnDisplayScreenLost;
    }

    /// <summary>The game media failed (stream dropped, YouTube refused): stop and tell the DJ why.</summary>
    private void OnPlaybackError(string error)
    {
        LastError = error;
        if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown)
        {
            Stop();
            // A pulled USB drive shows up as an mpv read error; tell the DJ what actually happened.
            if (Song is { UsdbId: null } song && !_library.IsReachableNow(song.SourceId))
            {
                _notifications.ShowError("Playback stopped", $"The drive with this song was removed ({_library.SourceLabel(song.SourceId)}).", error);
                return;
            }

            PlaybackError explained = PlaybackError.Explain(error);
            bool youTube = Plan?.Audio.Source is MediaSource.YouTube;
            _notifications.ShowError("Playback stopped", _ytDlp.WithHint([explained.Reason], explained.YtDlpMayHelp || youTube && explained.Reason == PlaybackError.Explain("").Reason), explained.Details);
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

    /// <summary>The loaded song has two voices.</summary>
    public bool IsDuet => Song?.Notes is { Count: > 1 };

    /// <summary>The DJ's pick of who sings which voice of the loaded duet (null: the first two set up). Reset per song.</summary>
    public DuetChoice? DuetChoice { get; private set; }

    /// <summary>Raised when the duet singers were picked again.</summary>
    public event Action? DuetChoiceChanged;

    public void SetDuetChoice(DuetChoice? choice)
    {
        DuetChoice = choice;
        DuetChoiceChanged?.Invoke();
    }

    // The setup the DJ last saw and confirmed (popup closed, or the one-singer note shown): who is on which display,
    // and who sings which voice.
    private string? _confirmedSetup;

    /// <summary>
    /// The duet's setup is still the one the DJ confirmed. False after a player was added, removed or moved between
    /// displays, or a singer lost their mic: the DJ is asked again (on closing that panel, at the latest at Play).
    /// </summary>
    public bool SingersConfirmed => !IsDuet || _confirmedSetup == SetupKey();

    public void ConfirmSingers()
    {
        _confirmedSetup = SetupKey();
        DuetChoiceChanged?.Invoke();
    }

    // "1@Beamer1 4@Beamer2 | 1:0 4:1" — players set up per open display, then the singers and their voices.
    private string SetupKey()
    {
        IEnumerable<string> placed = new[] { DisplayId.Beamer1, DisplayId.Beamer2 }
            .Where(_displays.IsOpen)
            .SelectMany(d => _displays.GetConfig(d).PlayerIds.Where(id => _players.Get(id).Mic is not null).Order().Select(id => $"{id}@{d}"));
        return $"{string.Join(' ', placed)} | {string.Join(' ', Singers().Select(s => $"{s.PlayerId}:{s.Voice}"))}";
    }

    /// <summary>Who sings the loaded song and which voice, as it stands now (players set up, the DJ's duet pick).</summary>
    public IReadOnlyList<(int PlayerId, int Voice)> Singers()
        => Song?.Notes is { } notes ? DuetVoices.Singers([.. ActivePlayers().Select(p => p.Id)], notes.Count, DuetChoice) : [];

    /// <summary>Song picture (cover, YouTube thumbnail, background) — get ready, countdown, score, Game Player box.</summary>
    public Bitmap? Picture { get; private set; }

    /// <summary>Backdrop (background, cover) behind the game for songs without video.</summary>
    public Bitmap? Backdrop { get; private set; }

    /// <summary>UI thread: <see cref="Picture"/> / <see cref="Backdrop"/> changed (they load after the song).</summary>
    public event Action? PicturesChanged;
    public MediaPlan? Plan { get; private set; }
    public GameSession? Session { get; private set; }

    /// <summary>Playing span of the current song (start/end in game time) — beamer times and progress, auto-stop.</summary>
    public SongTimeline? Timeline { get; private set; }
    public IGameClock? Clock => _media.Game.Clock;
    public Difficulty Difficulty => _settings.Difficulty;
    /// <summary>
    /// What the audience hears now: the clock minus the game output's latency (Bluetooth speakers play 200+ ms
    /// later than mpv decodes). Beamers draw this and the scorer judges against it (Core.Timing.LatencyModel).
    /// </summary>
    public double GamePositionSec => LatencyModel.DisplaySec(Clock?.PositionSec ?? 0, _outputs.GameLatencyMs);
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
        MediaPlan? plan = null;
        try
        {
            if (State != PlaybackState.Idle)
            {
                await ClearCoreAsync();
            }

            Song loaded = await _resolver.ResolveAsync(song, ct);

            plan = MediaSourceResolver.Resolve(new SongMedia
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
            DuetChoice = null;   // a new song: the DJ picks its duet singers again
            _confirmedSetup = null;
            Plan = plan;
            State = PlaybackState.Loaded;
            _ = LoadPicturesAsync(loaded);
            _failures.Loaded(song);
        }
        catch (SongLoadException ex) when (ex.SongProblem)
        {
            _failures.Failed(song, ex.Reasons[0]);
            throw;
        }
        catch (MediaException ex)
        {
            LastError = ex.Message;
            SongLoadException explained = Explain(ex, plan);
            if (explained.SongProblem)
            {
                _failures.Failed(song, explained.Reasons[0]);
            }

            throw explained;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// mpv's error in plain words. A local file that does not play is the song's problem; a YouTube failure only when
    /// the video itself is gone or blocked (<see cref="PlaybackError.SongProblem"/>).
    /// </summary>
    public static SongLoadException Explain(MediaException ex, MediaPlan? plan)
    {
        PlaybackError e = PlaybackError.Explain(ex.Message);
        bool local = plan is not null && plan.Audio.Source is not MediaSource.YouTube;
        // An unexplained YouTube failure (e.g. still refused after the retries) is the typical outdated-yt-dlp case too.
        bool unexplainedYouTube = !local && plan is not null && e.Reason == PlaybackError.Explain("").Reason;
        return new SongLoadException(e.Reason, e.Details, e.SongProblem || local, e.YtDlpMayHelp || unexplainedYouTube);
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

    public async Task PreviewAsync()
    {
        if (CanPreview && await RewindIfFinishedAsync())
        {
            State = PlaybackState.Preview;
        }
    }

    /// <summary>Starts the countdown on every open beamer; the first one to finish starts the media. After Stop it replays from the start.</summary>
    public async Task PlayAsync()
    {
        if (CanPlay && await RewindIfFinishedAsync())
        {
            _countdownDone.Clear();
            State = PlaybackState.Countdown;
        }
    }

    /// <summary>Beamers back to their start view; the song stays loaded (rewound) and ready to play.</summary>
    public async Task HomeAsync()
    {
        if (CanHome && await RewindIfFinishedAsync())
        {
            State = PlaybackState.Loaded;
        }
    }

    /// <summary>
    /// After a song: back to its start. Stopped in the middle, the file is only paused and a seek does it. Played to
    /// the very end of its file, mpv has closed it (keep-open=no) and a seek fails — the song was replayed on an
    /// empty player and ended at once; it is loaded again instead. False if that failed (the DJ was told).
    /// </summary>
    private async Task<bool> RewindIfFinishedAsync()
    {
        if (State != PlaybackState.Score)
        {
            return true;
        }

        if (_media.Game.State is not (MediaState.Ended or MediaState.Idle) || Plan is not { } plan)
        {
            _media.Game.Seek(0);
            return true;
        }

        _log.LogInformation("Replay: the song's file ended — loading it again");
        IsBusy = true;
        try
        {
            await _media.Game.LoadAsync(plan);
            return true;
        }
        catch (MediaException ex)
        {
            SongLoadException explained = Explain(ex, plan);
            _notifications.ShowError("Song cannot be played again", _ytDlp.WithHint(explained.Reasons, explained.YtDlpMayHelp), explained.Details);
            return false;
        }
        finally
        {
            IsBusy = false;
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

        // Who sings which voice (Core.Game.DuetVoices): solo everyone; a duet exactly two — the DJ's pick or the first
        // two set up — the others sit out (no lane, no score, no mic in the mix); one player alone sings both voices.
        IReadOnlyList<(int PlayerId, int Voice)> singers = Singers();
        IReadOnlyList<PlayerConfig> active = [.. singers.Select(s => _players.Get(s.PlayerId))];
        Session = new GameSession(Song, singers.Select(s => new GamePlayer(s.PlayerId, s.Voice, MicDelayMs(_players.Get(s.PlayerId)))).ToList(), Difficulty);
        // Media length in game time: when the video is the audio, its #VIDEOGAP intro is not part of the song.
        double? mediaLengthSec = _media.Game.Duration is { } d ? d.TotalSeconds - (Plan?.AudioOriginSec ?? 0) : null;
        Timeline = SongTimeline.For(Song, Session.LastBeat, TailAfterLastNoteSec, mediaLengthSec);

        StartMics(active);
        BeginLowLatencyGc();
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
        string? monitorOut = _outputs.GamePortAudioDeviceId();
        if (monitorOut is not null)
        {
            _audio.StartMonitor(monitorOut, _media.Game.ChannelOffset);
        }
    }

    // ── GC during a song ────────────────────────────────────────────────
    // Our PortAudio callbacks (mic monitor) are managed code on CoreAudio's IO thread, shared with mpv's output to the
    // same device: a GC pause stalls both → crackling. SustainedLowLatency avoids blocking full collections while the
    // song runs; the counts logged at the end show how quiet the song really was.
    private GCLatencyMode? _gcModeBefore;
    private (int Gen0, int Gen1, int Gen2, long Bytes) _gcAtStart;

    private void BeginLowLatencyGc()
    {
        _gcModeBefore ??= GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        _gcAtStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes());
    }

    private void EndLowLatencyGc()
    {
        if (_gcModeBefore is not { } before)
        {
            return;
        }

        GCSettings.LatencyMode = before;
        _gcModeBefore = null;
        _log.LogInformation("GC during the song: gen0 {Gen0}, gen1 {Gen1}, gen2 {Gen2}, {Mb:F1} MB allocated",
            GC.CollectionCount(0) - _gcAtStart.Gen0, GC.CollectionCount(1) - _gcAtStart.Gen1, GC.CollectionCount(2) - _gcAtStart.Gen2,
            (GC.GetTotalAllocatedBytes() - _gcAtStart.Bytes) / 1048576.0);
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
        _notifications.Warn($"Display {(int)id} closed — song stopped", "Open it again under Game Displays and press Play to sing the song again.");
    }

    /// <summary>
    /// A display's screen was unplugged. With singers on it the song stops and rewinds (they no longer see their
    /// notes); without, it plays on. The display stays open, parked next to the DJ window until the screen is back.
    /// </summary>
    private void OnDisplayScreenLost(DisplayId id, string screen)
    {
        string title = $"Display {(int)id}: {screen} disconnected";
        if (CanStop && _displays.GetConfig(id).PlayerIds.Count > 0)
        {
            Stop();
            _ = HomeAsync();
            _notifications.Warn($"{title} — song stopped", "Plug the screen back in and press Play, or move the singers to the other display.");
            return;
        }

        _notifications.Warn(title, "The display waits next to the DJ window and goes back when the screen is plugged in again.");
    }

    /// <summary>
    /// The game output was unplugged mid-song: stop and rewind (as when a mic is lost). Playing on from the default
    /// speakers would be out of sync — the latency belongs to the unplugged output. The displays stay open.
    /// </summary>
    private void OnGameOutputLost(string name)
    {
        if (State is not (PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused))
        {
            return;
        }

        Stop();
        _ = HomeAsync();
        _notifications.Warn("Game output disconnected — song stopped", $"{name}. Plug it back in and press Play, or choose another output under Audio Output.");
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
        // Stopped mid-song: the file is only paused, so this rewinds at once (no reload, nothing to await).
        _ = HomeAsync();
        foreach (DisplayId id in (ReadOnlySpan<DisplayId>)[DisplayId.Beamer1, DisplayId.Beamer2])
        {
            _displays.Close(id);
        }

        string who = players.Count > 0 ? string.Join(", ", players.Select(MicPresence.Describe)) : "A microphone";
        _notifications.Warn(
            "Microphone disconnected — song stopped",
            $"{who}. The displays were closed; plug the mic in, assign it under Audio Input and start again.");
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
        EndLowLatencyGc();
        State = PlaybackState.Score;
    }

    private async Task ClearCoreAsync()
    {
        StopTicker();
        _audio.StopAll();
        EndLowLatencyGc();
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
        // Same end as the beamers' remaining time (last note + tail, #END, media length).
        double stopAfterSec = Timeline?.EndSec ?? double.MaxValue;
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

                double pos = LatencyModel.DisplaySec(clock.PositionSec, _outputs.GameLatencyMs);
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

    /// <summary>
    /// How loud the player sings right now, 0..1 above their noise gate (<see cref="MicActivity"/>) — the beamer's
    /// mic meter. Render thread: two volatile reads, no allocation.
    /// </summary>
    public double MicActivityOf(int playerId)
        => _audio.Mics.Pipeline(playerId) is { } p ? MicActivity.Level(p.LevelRms, p.Threshold) : 0;

    /// <summary>The player's scoring delay: calibrated total minus the output latency it was calibrated through.</summary>
    public double MicDelayMs(PlayerConfig p) => LatencyModel.InputDelayMs(p.CalibratedTotalMs, _outputs.LatencyFor(p.CalibrationOutputKey));

    public void Dispose() => StopTicker();
}

/// <summary>A song cannot be loaded. <see cref="Exception.Message"/>: plain reasons, one per line; <see cref="Details"/>: raw cause.</summary>
/// <param name="message">Plain reasons, one per line.</param>
/// <param name="details">Raw cause for "Show details".</param>
/// <param name="songProblem">The song itself is the problem (broken file, no YouTube link, video removed): the
/// library marks it (<see cref="LoadFailureService"/>). False for connection trouble.</param>
/// <param name="ytDlpMayHelp">A newer yt-dlp would likely fix it (YouTube refused or blocked the stream).</param>
public sealed class SongLoadException(string message, string? details = null, bool songProblem = false, bool ytDlpMayHelp = false) : Exception(message)
{
    public string? Details { get; } = details;
    public bool SongProblem { get; } = songProblem;
    /// <summary>The dialog then points to Settings → YouTube (YtDlpService.WithHint).</summary>
    public bool YtDlpMayHelp { get; } = ytDlpMayHelp;

    public IReadOnlyList<string> Reasons => Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
