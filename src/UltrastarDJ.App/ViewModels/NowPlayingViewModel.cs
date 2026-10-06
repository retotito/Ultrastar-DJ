using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Displays;
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
        ILogger<NowPlayingViewModel> log)
    {
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
        _countdown = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => CountdownTick());
        _playback.PicturesChanged += RefreshBox;
        _displays.OpenStateChanged += (_, _) => Refresh();
        _players.Changed += OnPlayerChanged;
        media.Game.Frames.SourceChanged += () => Dispatcher.UIThread.Post(() => HasVideo = media.Game.Frames.HasSource);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
        Refresh();
    }

    public PlaybackState State => _playback.State;

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
    public double Level => _media.Game.LevelRms;
    /// <summary>Small monitor of what the beamers show.</summary>
    public Media.FrameBus GameFrames => _media.Game.Frames;
    /// <summary>One row per player that will sing (mic bound, assigned to an open beamer).</summary>
    public ObservableCollection<MixRowViewModel> MixRows { get; } = [];
    public bool HasMixRows => MixRows.Count > 0;
    /// <summary>False until the user has dragged the card once; the view then places it at its default spot.</summary>
    public bool HasSavedPosition { get; private set; }

    partial void OnGainChanged(double value)
    {
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
    }

    /// <summary>Rows mirror the players that will actually sing: mic bound and assigned to an open beamer.</summary>
    private void SyncRows()
    {
        IReadOnlyList<PlayerConfig> active = _playback.ActivePlayers();
        if (active.Count == MixRows.Count && active.Zip(MixRows).All(pair => pair.First.Id == pair.Second.Id))
        {
            return;
        }

        MixRows.Clear();
        foreach (PlayerConfig p in active)
        {
            MixRows.Add(new MixRowViewModel(p, _players));
        }

        OnPropertyChanged(nameof(HasMixRows));
    }

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
            PlaybackState.Preview => "Get-ready screen on the beamers",
            PlaybackState.Countdown => "3 – 2 – 1 …",
            PlaybackState.Playing => "Playing",
            PlaybackState.Paused => "Paused",
            PlaybackState.Score => "Score screen — Play to sing it again, Home for the start view",
            _ => "",
        };
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(HasSong));
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
        OnPropertyChanged(nameof(Level));
        SyncRows();
        foreach (MixRowViewModel row in MixRows)
        {
            MicPipeline? pipe = _audio.Mics.Pipeline(row.Id);
            row.Level = pipe?.LevelRms ?? 0;
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
            _notifications.ShowError("Song cannot be loaded", ex.Reasons, ex.Details);
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

    public string PlayGlyph => PlaybackRules.PlayButtonFor(_playback.State) == PlayButton.Pause ? "pause" : "play_arrow";
    public string PlayTip => _playback.State switch
    {
        PlaybackState.Playing => "Pause",
        PlaybackState.Paused => "Resume",
        PlaybackState.Countdown => "Starting…",
        PlaybackState.Score => "Play again from the start (countdown on the beamers)",
        _ => "Play (countdown on the beamers)",
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

    public MixRowViewModel(PlayerConfig config, PlayersService players)
    {
        _players = players;
        Id = config.Id;
        _name = config.Name;
        _mixGain = config.MixGain;
        _muted = config.MixMuted;
    }

    public int Id { get; }
    public string ColorKey => $"BrushPlayer{Id}";
    public string MuteGlyph => Muted ? "mic_off" : "mic";
    /// <summary>Same dB mapping as the players panel so both meters agree.</summary>
    public double LevelDb => Level <= 0 ? 0 : Math.Clamp((20 * Math.Log10(Level) - PlayerCardViewModel.GateMinDb) / -PlayerCardViewModel.GateMinDb, 0, 1);

    public void Reload(PlayerConfig config)
    {
        _loading = true;
        Name = config.Name;
        MixGain = config.MixGain;
        Muted = config.MixMuted;
        _loading = false;
    }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    partial void OnLevelChanged(double value) => OnPropertyChanged(nameof(LevelDb));
    partial void OnMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteGlyph));
        Save(p => p with { MixMuted = value });
    }

    partial void OnMixGainChanged(double value) => Save(p => p with { MixGain = Math.Round(value, 2) });

    private void Save(Func<PlayerConfig, PlayerConfig> change)
    {
        if (!_loading)
        {
            _players.Update(Id, change);
        }
    }
}
