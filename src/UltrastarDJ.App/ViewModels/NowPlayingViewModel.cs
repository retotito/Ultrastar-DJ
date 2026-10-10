using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// Transport controls, status, song fader and the live mic mix for the game channel. Floating card in the
/// DJ window; visibility and position are persisted.
/// </summary>
public sealed partial class NowPlayingViewModel : ViewModelBase, IDisposable
{
    private readonly SongMarksService _marks;
    private readonly PlaybackService _playback;
    private readonly OutputsService _outputs;
    private bool _syncingGain;
    private readonly IDisplayService _displays;
    private readonly MediaService _media;
    private readonly AudioInputService _audio;
    private readonly PlayersService _players;
    private readonly AppSettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly ILogger<NowPlayingViewModel> _log;
    private readonly DispatcherTimer _poll;

    [ObservableProperty] private string _title = "No song loaded";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _status = "";
    // Time strip at the bottom of the box, as at the bottom of the beamer (Core.Timing.SongTimeline).
    [ObservableProperty] private string _elapsed = "";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasTimes;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private double _gain = 1.0;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private double _cardX;
    [ObservableProperty] private double _cardY;

    public NowPlayingViewModel(PlaybackService playback, IDisplayService displays, MediaService media, AudioInputService audio,
        PlayersService players, AppSettingsService settings, OutputsService outputs, NotificationService notifications,
        SongbookService songbook, YtDlpService ytDlp, SongMarksService marks, ILogger<NowPlayingViewModel> log)
    {
        _marks = marks;
        marks.Changed += OnMarksChanged;
        _ytDlp = ytDlp;
        _songbook = songbook;
        // Accepted, cancelled, loaded: who asked for the loaded song may change.
        songbook.Changed += () => OnPropertyChanged(nameof(RequestedBy));
        _outputs = outputs;
        _playback = playback;
        _displays = displays;
        _media = media;
        _audio = audio;
        _players = players;
        _settings = settings;
        _notifications = notifications;
        _log = log;
        // Same setting as the Game volume in Audio Output: persisted there, both sliders follow each other.
        _gain = outputs.Game.Gain;
        outputs.Changed += () =>
        {
            _syncingGain = true;
            Gain = outputs.Game.Gain;
            _syncingGain = false;
        };
        _isVisible = !settings.NowPlayingHidden;
        if (settings.NowPlayingPosition is { } pos)
        {
            (_cardX, _cardY) = pos;
            HasSavedPosition = true;
        }

        _playback.StateChanged += OnStateChanged;
        _playback.DuetChoiceChanged += () => OnPropertyChanged(nameof(VoicesUnconfirmed));
        _displays.PlayersChanged += () => OnPropertyChanged(nameof(VoicesUnconfirmed));
        _countdown = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => CountdownTick());
        _playback.PicturesChanged += RefreshBox;
        _displays.OpenStateChanged += (_, _) => Refresh();
        _players.Changed += OnPlayerChanged;
        media.Game.Frames.SourceChanged += () => Dispatcher.UIThread.Post(() => HasVideo = media.Game.Frames.HasSource);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
        Refresh();
    }

    public PlaybackState State => _playback.State;

    private readonly SongbookService _songbook;
    private readonly YtDlpService _ytDlp;

    /// <summary>The guest who requested the loaded song on the songbook (as in the queue) — the DJ calls them up.</summary>
    public string? RequestedBy => _playback.Song is { } song ? _songbook.RequesterOf(song.Id) : null;

    // ── Countdown and pause in the box, as on the beamers ──
    private readonly DispatcherTimer _countdown;
    /// <summary>3, 2, 1 — started on the same state change as the beamers' countdown, so both count together.</summary>
    [ObservableProperty] private int _countdownValue;
    public bool IsCountdown => _playback.State == PlaybackState.Countdown;
    public bool IsPaused => _playback.State == PlaybackState.Paused;

    private void OnStateChanged(PlaybackState state)
    {
        _countdown.Stop();
        if (state == PlaybackState.Countdown)
        {
            CountdownValue = 3;
            _countdown.Start();
        }

        OnPropertyChanged(nameof(IsCountdown));
        OnPropertyChanged(nameof(IsPaused));
        Refresh();
    }

    // The beamers end the countdown (PlaybackService.CountdownDone); the box only shows it and stops at 1.
    private void CountdownTick()
    {
        if (CountdownValue > 1)
        {
            CountdownValue--;
        }
        else
        {
            _countdown.Stop();
        }
    }
    public bool HasSong => _playback.Song is not null;
    /// <summary>The DJ's marks on the loaded song — set right here, where a bad song shows (SongMarksService).</summary>
    public bool IsFavourite => _playback.Song is { } s && _marks.FavouriteIds.Contains(s.Id);
    public bool IsBroken => _playback.Song is { } s && _marks.BrokenIds.Contains(s.Id);

    [RelayCommand]
    private void ToggleFavourite()
    {
        if (_playback.Song is { } s)
        {
            _marks.SetFavourite(s, !IsFavourite);
        }
    }

    [RelayCommand]
    private void ToggleBroken()
    {
        if (_playback.Song is { } s)
        {
            _marks.SetBroken(s, !IsBroken);
        }
    }

    private void OnMarksChanged()
    {
        OnPropertyChanged(nameof(IsFavourite));
        OnPropertyChanged(nameof(IsBroken));
    }
    /// <summary>The song in the Game Player (a failed load keeps the previous one).</summary>
    public string? LoadedSongId => _playback.Song?.Id;
    /// <summary>The game channel has video (cases 2, 3, 4, 6).</summary>
    [ObservableProperty] private bool _hasVideo;

    // What the box shows: Core.Playback.StageView — the same rule as the beamers' background.
    private StageLayer Layer => StageView.GamePlayerBox(_playback.State, HasVideo);
    /// <summary>Song picture, or the backdrop while a song without video plays. Under the video when there is one.</summary>
    public Bitmap? BoxImage => Layer == StageLayer.Backdrop ? _playback.Backdrop ?? _playback.Picture : _playback.Picture;
    public bool ShowBoxImage => Layer != StageLayer.None && BoxImage is not null;
    public bool ShowVideo => Layer == StageLayer.Video;

    partial void OnHasVideoChanged(bool value) => RefreshBox();

    private void RefreshBox()
    {
        OnPropertyChanged(nameof(BoxImage));
        OnPropertyChanged(nameof(ShowBoxImage));
        OnPropertyChanged(nameof(ShowVideo));
    }
    /// <summary>What the song fader lets through: mpv meters before its (cubic) volume, the meter shows after it.</summary>
    public double SongLevel => _media.Game.LevelRms * VolumeCurve.MpvAmplitude(Gain);
    /// <summary>Small monitor of what the beamers show.</summary>
    public Media.FrameBus GameFrames => _media.Game.Frames;
    /// <summary>One row per player that will sing (mic bound, assigned to an open beamer).</summary>
    public ObservableCollection<MixRowViewModel> MixRows { get; } = [];
    public bool HasMixRows => MixRows.Count > 0;
    /// <summary>False until the user has dragged the card once; the view then places it at its default spot.</summary>
    public bool HasSavedPosition { get; private set; }

    partial void OnGainChanged(double value)
    {
        OnPropertyChanged(nameof(SongLevel));
        if (!_syncingGain)
        {
            _outputs.SetGameGain(value);
        }
    }
    partial void OnIsVisibleChanged(bool value) => _settings.SetNowPlayingHidden(!value);

    public void ToggleVisible() => IsVisible = !IsVisible;

    [RelayCommand] private void Hide() => IsVisible = false;

    /// <summary>Called by the view when a drag ends.</summary>
    public void SavePosition()
    {
        HasSavedPosition = true;
        _settings.SetNowPlayingPosition(CardX, CardY);
    }

    private void OnPlayerChanged(PlayerConfig p)
    {
        MixRows.FirstOrDefault(r => r.Id == p.Id)?.Reload(p);
        SyncRows();
        OnPropertyChanged(nameof(VoicesUnconfirmed));   // a player renamed, or a singer lost their mic
    }

    /// <summary>Rows mirror the players that will actually sing: mic bound and assigned to an open beamer.</summary>
    private void SyncRows()
    {
        IReadOnlyList<PlayerConfig> active = _playback.ActivePlayers();
        // A duet: only who sings (the others sit out — no slider), each with the voice they sing under it.
        IReadOnlyList<(int PlayerId, int Voice)> singers = _playback.IsDuet ? _playback.Singers() : [];
        if (_playback.IsDuet)
        {
            active = [.. active.Where(p => singers.Any(s => s.PlayerId == p.Id))];
        }

        if (active.Count != MixRows.Count || !active.Zip(MixRows).All(pair => pair.First.Id == pair.Second.Id))
        {
            MixRows.Clear();
            foreach (PlayerConfig p in active)
            {
                MixRows.Add(new MixRowViewModel(p, _players));
            }
        }

        // Which display each sings on (a player may have moved without the rows changing).
        foreach (MixRowViewModel row in MixRows)
        {
            row.Display = _displays.GetConfig(DisplayId.Beamer1).PlayerIds.Contains(row.Id) ? 1 : 2;
            row.VoiceText = singers.FirstOrDefault(x => x.PlayerId == row.Id) is { PlayerId: > 0 } sung ? VoiceName(sung.Voice) : "";
        }

        OnPropertyChanged(nameof(HasMixRows));
    }

    // "Bradley Cooper" (the file's singer name, else "Voice 1"), or "Both voices".
    private string VoiceName(int voice) => voice == DuetVoices.BothVoices ? "Both voices"
        : (voice == 0 ? _playback.Song?.Voice1 : _playback.Song?.Voice2) is { Length: > 0 } name ? name : $"Voice {voice + 1}";

    private void Refresh()
    {
        Song? song = _playback.Song;
        RefreshBox();
        Title = song?.Title ?? "No song loaded";
        Artist = song?.Artist ?? "";
        SyncRows();
        Status = _playback.State switch
        {
            PlaybackState.Idle => "Load a song from the library",
            PlaybackState.Loaded => "Ready",
            PlaybackState.Preview => "Get-ready screen on the displays",
            PlaybackState.Countdown => "3 – 2 – 1 …",
            PlaybackState.Playing => "Playing",
            PlaybackState.Paused => "Paused",
            PlaybackState.Score => "Score screen — Play to sing it again, Home for the start view",
            _ => "",
        };
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(HasSong));
        OnPropertyChanged(nameof(IsDuet));
        OnPropertyChanged(nameof(VoicesUnconfirmed));
        ChangeDuetSingersCommand.NotifyCanExecuteChanged();
        OnMarksChanged();
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(IsPauseButton));
        OnPropertyChanged(nameof(NoBeamerOpen));
        OnPropertyChanged(nameof(PlayTip));
        LoadCommand.NotifyCanExecuteChanged();
        NotifyLoadAvailability();
        HomeCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void Poll()
    {
        OnPropertyChanged(nameof(SongLevel));
        SyncRows();
        foreach (MixRowViewModel row in MixRows)
        {
            MicPipeline? pipe = _audio.Mics.Pipeline(row.Id);
            row.Level = pipe?.LevelRms ?? 0;
            row.IsGated = pipe?.IsGated ?? false;
        }

        if (_playback.Clock is { } clock && _playback.Timeline is { } timeline && _playback.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            double t = clock.PositionSec;
            Elapsed = ClockText(timeline.Elapsed(t));
            Remaining = ClockText(timeline.Remaining(t));
            Progress = timeline.Fraction(t);
            HasTimes = true;
        }
        else
        {
            HasTimes = false;
        }
    }

    private static string ClockText(double seconds)
    {
        int s = (int)Math.Floor(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Called by the library (double-click / Load button).</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        Loading = true;
        IsVisible = true;
        Status = $"Loading {song.Title}…";
        try
        {
            await _playback.LoadAsync(song);
        }
        catch (SongLoadException ex)
        {
            _log.LogWarning("Load failed: {Error}", ex.Message);
            _notifications.ShowError("Song cannot be loaded", _ytDlp.WithHint(ex.Reasons, ex.YtDlpMayHelp), ex.Details);
        }
        finally
        {
            Loading = false;
            Refresh();
        }
    }

    private bool CanLoad(Song? song) => CanLoadSong;

    /// <summary>
    /// Whether a song can be loaded into the game now. Every "Load" entry point (library menus, preview Load,
    /// queue Load / Load next) disables itself with this and shows <see cref="LoadSongTip"/>.
    /// </summary>
    public bool CanLoadSong => _playback.CanLoad && !Loading;

    public string LoadSongTip => Loading
        ? "A song is loading…"
        : _playback.CanLoad ? "Load into the Game Player" : "A song is playing — stop it (⏹) to load another one";

    /// <summary>Raised when <see cref="CanLoadSong"/> may have changed.</summary>
    public event Action? LoadAvailabilityChanged;

    partial void OnLoadingChanged(bool value) => NotifyLoadAvailability();

    private void NotifyLoadAvailability()
    {
        OnPropertyChanged(nameof(CanLoadSong));
        OnPropertyChanged(nameof(LoadSongTip));
        LoadAvailabilityChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        Loading = true;
        try
        {
            await _playback.PreviewAsync();
        }
        finally
        {
            Loading = false;
        }
    }
    private bool CanPreview() => _playback.CanPreview;

    [RelayCommand(CanExecute = nameof(CanHome))]
    private async Task HomeAsync()
    {
        // Loader while a song that ran to its end is loaded again (PlaybackService.RewindIfFinishedAsync).
        Loading = true;
        try
        {
            await _playback.HomeAsync();
        }
        finally
        {
            Loading = false;
        }
    }

    private bool CanHome() => _playback.CanHome;

    /// <summary>One button: play (countdown) → pause while playing → resume while paused. Disabled during the countdown.</summary>
    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private async Task PlayPauseAsync()
    {
        switch (PlaybackRules.PlayButtonFor(_playback.State))
        {
            case PlayButton.Pause:
                _playback.Pause();
                break;
            case PlayButton.Resume:
                _playback.Resume();
                break;
            default:
                // A duet whose singers changed since the DJ confirmed them (a player added / removed, a mic gone):
                // ask first; the DJ window starts the song once they are confirmed.
                if (!_playback.SingersConfirmed && _playback.ActivePlayers().Count > 0)
                {
                    SingersToConfirmBeforePlay?.Invoke(PlayAfterConfirmAsync);
                    return;
                }

                await PlayAfterConfirmAsync();
                break;
        }
    }

    /// <summary>Play pressed on a duet with unconfirmed singers: the DJ window asks, then calls the given action to start.</summary>
    public event Action<Func<Task>>? SingersToConfirmBeforePlay;

    private async Task PlayAfterConfirmAsync()
    {
        switch (PlaybackRules.PlayButtonFor(_playback.State))
        {
            case PlayButton.Pause or PlayButton.Resume:
                return;
            default:
                Loading = true;
                try
                {
                    await _playback.PlayAsync();
                }
                finally
                {
                    Loading = false;
                }

                break;
        }
    }

    private bool CanPlayPause() => PlaybackRules.PlayButtonEnabled(_playback.State, _playback.AnyDisplayOpen) && !_playback.IsBusy;

    /// <summary>The play button shows pause (light blue) instead of play (blue).</summary>
    public bool IsPauseButton => PlaybackRules.PlayButtonFor(_playback.State) == PlayButton.Pause;

    /// <summary>No beamer open: the transport is replaced by one "Select displays" button.</summary>
    public bool NoBeamerOpen => !_playback.AnyDisplayOpen;

    /// <summary>"Select displays": the DJ window opens its Displays panel (beamers, players per screen).</summary>
    public event Action? DisplaysRequested;

    [RelayCommand] private void SelectDisplays() => DisplaysRequested?.Invoke();

    /// <summary>The players changed since the duet's voices were assigned: ⚠ next to "Duet" until assigned again.</summary>
    public bool VoicesUnconfirmed => IsDuet && !_playback.SingersConfirmed;

    public bool IsDuet => _playback.IsDuet && _playback.Song is not null;

    /// <summary>"change" next to the duet line: the DJ window reopens the pick (2+ players, not while it runs).</summary>
    public event Action? DuetSingersRequested;

    [RelayCommand(CanExecute = nameof(CanChangeDuetSingers))]
    private void ChangeDuetSingers() => DuetSingersRequested?.Invoke();

    private bool CanChangeDuetSingers() => IsDuet && _playback.ActivePlayers().Count >= 2
        && _playback.State is not (PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused);

    public string PlayGlyph => PlaybackRules.PlayButtonFor(_playback.State) == PlayButton.Pause ? "pause" : "play_arrow";
    public string PlayTip => _playback.State switch
    {
        PlaybackState.Playing => "Pause",
        PlaybackState.Paused => "Resume",
        PlaybackState.Countdown => "Starting…",
        PlaybackState.Score => "Play again from the start (countdown on the displays)",
        _ => "Play (countdown on the displays)",
    };

    [RelayCommand(CanExecute = nameof(CanStop))] private void Stop() => _playback.Stop();
    private bool CanStop() => _playback.CanStop;

    public void Dispose()
    {
        _poll.Stop();
        _players.Changed -= OnPlayerChanged;
    }
}

/// <summary>One player's line in the speaker mix: fader, mute and live mic level.</summary>
public sealed partial class MixRowViewModel : ObservableObject
{
    private readonly PlayersService _players;
    private bool _loading;

    [ObservableProperty] private string _name;
    [ObservableProperty] private double _mixGain;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _level;
    [ObservableProperty] private bool _isGated;
    /// <summary>The display this player sings on (1 or 2) — the number in the screen icon.</summary>
    [ObservableProperty] private int _display = 1;
    /// <summary>Duet: the voice this player sings ("Bradley Cooper", "Both voices"); empty for a solo song.</summary>
    [ObservableProperty] private string _voiceText = "";

    public MixRowViewModel(PlayerConfig config, PlayersService players)
    {
        _players = players;
        Id = config.Id;
        _name = config.Name;
        _mixGain = Math.Min(1, config.MixGain);   // mix is 0–100 % (older settings went to 200 %)
        _muted = config.MixMuted;
    }

    public int Id { get; }
    public string ColorKey => $"BrushPlayer{Id}";
    public string MuteGlyph => Muted ? "mic_off" : "mic";
    /// <summary>What the speakers get from this mic (as on the Audio Input card's MIX): gated signal × mix, nothing when muted.</summary>
    public double MixLevel => Muted || IsGated ? 0 : Level * MixGain;

    public void Reload(PlayerConfig config)
    {
        _loading = true;
        Name = config.Name;
        MixGain = Math.Min(1, config.MixGain);
        Muted = config.MixMuted;
        _loading = false;
    }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    partial void OnLevelChanged(double value) => OnPropertyChanged(nameof(MixLevel));
    partial void OnIsGatedChanged(bool value) => OnPropertyChanged(nameof(MixLevel));
    partial void OnMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteGlyph));
        OnPropertyChanged(nameof(MixLevel));
        Save(p => p with { MixMuted = value });
    }

    partial void OnMixGainChanged(double value)
    {
        OnPropertyChanged(nameof(MixLevel));
        Save(p => p with { MixGain = Math.Round(value, 2) });
    }

    private void Save(Func<PlayerConfig, PlayerConfig> change)
    {
        if (!_loading)
        {
            _players.Update(Id, change);
        }
    }
}
