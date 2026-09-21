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
    private readonly SongValidator _validator;
    private readonly ILogger<PlaybackService> _log;
    private readonly HashSet<DisplayId> _countdownDone = [];
    private CancellationTokenSource? _tickCts;
    private Task? _tickLoop;
    private PlaybackState _state = PlaybackState.Idle;

    public PlaybackService(MediaService media, AudioInputService audio, PlayersService players, IDisplayService displays, AppSettingsService settings, ILogger<PlaybackService> log)
    {
        _media = media;
        _audio = audio;
        _players = players;
        _displays = displays;
        _settings = settings;
        _validator = new SongValidator(new FileSystemExistence());
        _log = log;
        _media.Game.EndReached += () => Dispatcher.UIThread.Post(() => { if (State is PlaybackState.Playing or PlaybackState.Paused) { Stop(); } });
        _media.Game.ErrorOccurred += e => Dispatcher.UIThread.Post(() => { LastError = e; if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown) { Stop(); } });
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
    public bool CanPlay => State is PlaybackState.Loaded or PlaybackState.Preview && AnyDisplayOpen && !IsBusy;
    public bool CanPreview => State is PlaybackState.Loaded && AnyDisplayOpen;
    public bool CanPause => State == PlaybackState.Playing;
    public bool CanResume => State == PlaybackState.Paused;
    public bool CanStop => State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown;

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

            SongValidationResult v = _validator.Validate(song);
            if (!v.IsValid)
            {
                throw new SongLoadException(string.Join("\n", v.Errors.Select(e => e.Message)));
            }

            Song loaded = await Task.Run(() => SongNotesLoader.WithNotes(v.Song), ct);
            if (loaded.Notes is null || loaded.Notes.Count == 0)
            {
                throw new SongLoadException("Song has no notes.");
            }

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
        }
        catch (MediaException ex)
        {
            LastError = ex.Message;
            throw new SongLoadException(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Preview()
    {
        if (CanPreview)
        {
            State = PlaybackState.Preview;
        }
    }

    /// <summary>Starts the countdown on every open beamer; the first one to finish starts the media.</summary>
    public void Play()
    {
        if (!CanPlay)
        {
            return;
        }

        _countdownDone.Clear();
        State = PlaybackState.Countdown;
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

        if (active.Count > 0)
        {
            _audio.Mics.Start(active.Select(p => new MicSlot(p.Id, p.Mic!, p.InputGain, p.Threshold)).ToList());
            string? monitorOut = ResolveMonitorOutput();
            if (monitorOut is not null)
            {
                _audio.StartMonitor(monitorOut, _media.Game.ChannelOffset);
            }
        }

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

    /// <summary>Leaves the score screen; song rewound and ready to play again.</summary>
    public void Dismiss()
    {
        if (State == PlaybackState.Score)
        {
            _media.Game.Seek(0);
            State = PlaybackState.Loaded;
        }
    }

    /// <summary>Beamers back to idle; song unloaded.</summary>
    public async Task ClearAsync()
    {
        if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown)
        {
            Stop();
        }

        await ClearCoreAsync();
    }

    private async Task ClearCoreAsync()
    {
        StopTicker();
        _audio.StopAll();
        await _media.Game.UnloadAsync();
        Song = null;
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

public sealed class SongLoadException(string message) : Exception(message);
