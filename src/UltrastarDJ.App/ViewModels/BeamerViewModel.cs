using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Game;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// View state of one singer screen. Pure observer of <see cref="PlaybackService"/>; owns only what is
/// screen-local: the countdown timer, the scene it draws and the score animation.
/// </summary>
public sealed partial class BeamerViewModel : ViewModelBase, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly PlayersService _players;
    private readonly IDisplayService _displays;
    private readonly AppSettingsService _settings;
    private readonly SongbookService _songbook;
    private readonly DispatcherTimer _countdown;
    private readonly DispatcherTimer _scoreAnim;
    private DateTime _scoreAnimStart;

    [ObservableProperty] private PlaybackState _state = PlaybackState.Idle;
    [ObservableProperty] private bool _hasVideo;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private int _countdownValue;
    /// <summary>Settings → Note bar style, applied live.</summary>
    [ObservableProperty] private NoteBarStyle _noteBarStyle;
    /// <summary>Settings → Grid lines, applied live.</summary>
    [ObservableProperty] private bool _showGridLines = true;
    [ObservableProperty] private GameScene? _scene;
    [ObservableProperty] private int _winnerId = -1;
    /// <summary>Score screen: the count-up has finished — the winner's stars start (only if they scored).</summary>
    [ObservableProperty] private bool _scoreCounted;
    /// <summary>Score screen after an early stop: what the percentages mean. Empty when the song was sung through.</summary>
    [ObservableProperty] private string _scoreNote = "";
    [ObservableProperty] private IBrush? _winnerBrush;

    public BeamerViewModel(DisplayId id, FrameBus gameFrames, PlaybackService playback, PlayersService players, IDisplayService displays,
        AppSettingsService settings, SyncTestService syncTest, SongbookService songbook)
    {
        Id = id;
        _songbook = songbook;
        songbook.Changed += OnSongbookChanged;
        SyncTest = syncTest;
        syncTest.Changed += OnSyncTestChanged;
        _settings = settings;
        _noteBarStyle = settings.NoteBarStyle;
        _showGridLines = settings.ShowGridLines;
        settings.Changed += OnSettingsChanged;
        GameFrames = gameFrames;
        _playback = playback;
        _players = players;
        _displays = displays;
        HasVideo = gameFrames.HasSource;
        gameFrames.SourceChanged += OnFramesChanged;
        _playback.StateChanged += OnPlaybackStateChanged;
        _playback.PicturesChanged += RefreshStage;
        _playback.PitchTicked += OnPitchTicked;
        _displays.PlayersChanged += OnAssignmentChanged;
        _players.Changed += OnPlayerConfigChanged;
        _countdown = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => CountdownTick());
        _scoreAnim = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => AnimateScores());
        OnSongbookChanged();
        // Opened on the score screen: this display did not take part in that song — it shows the start view.
        OnPlaybackStateChanged(_playback.State == PlaybackState.Score ? PlaybackState.Loaded : _playback.State);
        RefreshAssignedPlayers();
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        NoteBarStyle = _settings.NoteBarStyle;
        ShowGridLines = _settings.ShowGridLines;
    });

    public DisplayId Id { get; }
    public string Label => $"Display {(int)Id}";
    public FrameBus GameFrames { get; }
    public ObservableCollection<ScenePlayer> AssignedPlayers { get; } = [];
    public bool HasAssignedPlayers => AssignedPlayers.Count > 0;
    public ObservableCollection<ScoreRowViewModel> Scores { get; } = [];

    public bool IsIdle => State is PlaybackState.Idle or PlaybackState.Loaded;
    public bool IsPreview => State == PlaybackState.Preview;
    public bool IsCountdown => State == PlaybackState.Countdown;
    public bool IsGame => State is PlaybackState.Playing or PlaybackState.Paused;
    public bool IsPaused => State == PlaybackState.Paused;
    public bool IsScore => State == PlaybackState.Score;
    // Background layers: Core.Playback.StageView — the same rule as the Game Player box.
    private StageLayer Layer => StageView.Beamer(State, HasVideo);
    /// <summary>Get ready / score: the song picture, blurred.</summary>
    public Bitmap? BlurredImage => _playback.Picture;
    public bool ShowBlurredImage => Layer == StageLayer.BlurredPicture && BlurredImage is not null;
    /// <summary>Countdown and song: the picture (or, without video, the backdrop), sharp and dimmed; under the video.</summary>
    public Bitmap? StageImage => Layer == StageLayer.Backdrop ? _playback.Backdrop ?? _playback.Picture : _playback.Picture;
    public bool ShowStageImage => Layer is StageLayer.Picture or StageLayer.Backdrop or StageLayer.Video && StageImage is not null;
    public bool ShowVideo => Layer == StageLayer.Video;

    private void RefreshStage()
    {
        OnPropertyChanged(nameof(BlurredImage));
        OnPropertyChanged(nameof(ShowBlurredImage));
        OnPropertyChanged(nameof(StageImage));
        OnPropertyChanged(nameof(ShowStageImage));
        OnPropertyChanged(nameof(ShowVideo));
    }

    partial void OnStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(IsCountdown));
        OnPropertyChanged(nameof(IsGame));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsScore));
        RefreshStage();
    }

    partial void OnHasVideoChanged(bool value) => RefreshStage();

    private void OnFramesChanged() => Dispatcher.UIThread.Post(() => HasVideo = GameFrames.HasSource);

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;
        OnSongbookChanged();
        ScoreCounted = false;
        switch (state)
        {
            case PlaybackState.Idle:
                _countdown.Stop();
                _scoreAnim.Stop();
                Scene = null;
                Title = Artist = "";
                break;

            case PlaybackState.Loaded:
                Scene = null;
                LoadSongInfo();
                break;

            case PlaybackState.Preview:
                LoadSongInfo();
                RefreshAssignedPlayers();
                break;

            case PlaybackState.Countdown:
                LoadSongInfo();
                RefreshAssignedPlayers();
                CountdownValue = 3;
                _countdown.Start();
                break;

            case PlaybackState.Playing:
                _countdown.Stop();
                // A new session (Play again after Stop) gets a fresh scene; resume after Pause keeps the sung fill.
                if (_playback.Session is { } session && !ReferenceEquals(Scene?.Session, session) && _playback.Clock is not null)
                {
                    RefreshAssignedPlayers();
                    // Only who sings: in a duet the players sitting out get no lane (a display with none of the
                    // singers shows just the lyrics).
                    Scene = new GameScene(session, [.. AssignedPlayers.Where(p => session.PlayerIds.Contains(p.Id))], () => _playback.GamePositionSec,
                        _playback.Timeline, () => _playback.Clock?.PositionSec ?? 0, _playback.MicActivityOf);
                }

                break;

            case PlaybackState.Score:
                _countdown.Stop();
                BuildScores();
                break;
        }
    }

    private void LoadSongInfo()
    {
        if (_playback.Song is not { } song)
        {
            return;
        }

        Title = song.Title;
        Artist = song.Artist;
    }

    private void RefreshAssignedPlayers()
    {
        AssignedPlayers.Clear();
        IReadOnlyList<int> ids = _displays.GetConfig(Id).PlayerIds;
        foreach (PlayerConfig p in _players.All.Where(p => ids.Contains(p.Id) && p.Mic is not null))
        {
            AssignedPlayers.Add(new ScenePlayer(p.Id, p.Name, PlayerBrush(p.Id), PlayerColor(p.Id)));
        }

        OnPropertyChanged(nameof(HasAssignedPlayers));
    }

    // Assignment/name/mic edits show up live on the idle and get-ready screens; a running game keeps its scene.
    private void OnAssignmentChanged()
    {
        if (!IsGame)
        {
            RefreshAssignedPlayers();
        }
    }

    private void OnPlayerConfigChanged(PlayerConfig p) => OnAssignmentChanged();

    private static Color PlayerColor(int id)
        => Application.Current is { } app && app.TryGetResource($"ColorPlayer{id}", ThemeVariant.Default, out object? v) && v is Color c ? c : Colors.White;

    private static IBrush PlayerBrush(int id)
        => Application.Current is { } app && app.TryGetResource($"BrushPlayer{id}", ThemeVariant.Default, out object? v) && v is IBrush b ? b : Brushes.White;

    private void CountdownTick()
    {
        CountdownValue--;
        if (CountdownValue <= 0)
        {
            _countdown.Stop();
            _playback.CountdownDone(Id);
        }
    }

    private void OnPitchTicked(IReadOnlyList<PitchTick> ticks)
    {
        GameScene? scene = Scene;
        if (scene is not null)
        {
            scene.Apply(ticks, _playback.GamePositionSec);
        }
    }

    // ── Score screen: all players on every beamer, animated count-up ─────

    private void BuildScores()
    {
        Scores.Clear();
        if (_playback.Session is not { } session)
        {
            return;
        }

        IReadOnlyList<(int PlayerId, int Score, int MaxScore)> standings = session.Standings();
        // Stopped early: each score as a share of what was possible until then, not of the whole song.
        double stoppedAt = _playback.GamePositionSec;
        bool finished = standings.All(s => session.PossibleScoreAt(stoppedAt, s.PlayerId) >= s.MaxScore);
        ScoreNote = finished || _playback.Timeline is not { } timeline
            ? ""
            : $"Stopped at {TimeSpan.FromSeconds(timeline.Elapsed(_playback.Clock?.PositionSec ?? 0)):m\\:ss} — percentages of the points possible until then";
        WinnerId = standings.Count > 0 && standings[0].Score > 0 ? standings[0].PlayerId : -1;
        WinnerBrush = WinnerId > 0 ? PlayerBrush(WinnerId) : null;
        foreach ((int pid, int score, int max) in standings.OrderBy(s => s.PlayerId))
        {
            PlayerConfig cfg = _players.Get(pid);
            Scores.Add(new ScoreRowViewModel(pid, cfg.Name, PlayerBrush(pid), score, max, session.PossibleScoreAt(stoppedAt, pid), pid == WinnerId));
        }

        _scoreAnimStart = DateTime.UtcNow;
        _scoreAnim.Start();
    }

    private void AnimateScores()
    {
        const double durationSec = 1.8;
        double t = Math.Min(1, (DateTime.UtcNow - _scoreAnimStart).TotalSeconds / durationSec);
        double eased = 1 - Math.Pow(1 - t, 3);
        foreach (ScoreRowViewModel row in Scores)
        {
            row.Displayed = (int)Math.Round(row.Final * eased);
        }

        if (t >= 1)
        {
            _scoreAnim.Stop();
            ScoreCounted = WinnerId > 0;
        }
    }

    public void Dispose()
    {
        _countdown.Stop();
        _scoreAnim.Stop();
        GameFrames.SourceChanged -= OnFramesChanged;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _playback.PitchTicked -= OnPitchTicked;
        _displays.PlayersChanged -= OnAssignmentChanged;
        _players.Changed -= OnPlayerConfigChanged;
        _settings.Changed -= OnSettingsChanged;
        SyncTest.Changed -= OnSyncTestChanged;
        _songbook.Changed -= OnSongbookChanged;
    }

    // ── Songbook QR: guests scan it to request songs. Between songs only — never over lyrics or notes. ──

    /// <summary>The songbook's address for phones, or null when it is off.</summary>
    public string? SongbookUrl { get; private set; }
    /// <summary>The party PIN guests must type, or null without one.</summary>
    public string? SongbookPin { get; private set; }
    public bool HasSongbookPin => SongbookPin is not null;
    // Start view and get ready only: not over the game, not over the scores.
    public bool ShowSongbookQr => SongbookUrl is not null && State is PlaybackState.Idle or PlaybackState.Loaded or PlaybackState.Preview;

    // Re-read on the songbook's changes and on every state change (a Wi-Fi switch changes the address too).
    private void OnSongbookChanged()
    {
        SongbookUrl = _songbook.GuestUrl();
        SongbookPin = _songbook.PinEnabled ? _songbook.Pin : null;
        OnPropertyChanged(nameof(SongbookUrl));
        OnPropertyChanged(nameof(SongbookPin));
        OnPropertyChanged(nameof(HasSongbookPin));
        OnPropertyChanged(nameof(ShowSongbookQr));
    }

    /// <summary>Audio Output → Game → Test sync: this screen shows the flash.</summary>
    public SyncTestService SyncTest { get; }
    public bool SyncTestRunning => SyncTest.IsRunning;

    private void OnSyncTestChanged() => OnPropertyChanged(nameof(SyncTestRunning));
}

public sealed partial class ScoreRowViewModel(int playerId, string name, IBrush brush, int final, int max, int possible, bool isWinner) : ObservableObject
{
    [ObservableProperty] private int _displayed;

    public int PlayerId { get; } = playerId;
    public string Name { get; } = name;
    public IBrush Brush { get; } = brush;
    public int Final { get; } = final;
    public int Max { get; } = max;
    /// <summary>The most the player could have scored until the stop (the full maximum when sung through).</summary>
    public int Possible { get; } = possible;
    public bool IsWinner { get; } = isWinner;
    /// <summary>Bar and percentage: the counted-up score as a share of what was possible (count up together).</summary>
    public double Fraction => Possible > 0 ? Math.Min(1, (double)Displayed / Possible) : 0;
    public string Percent => $"{Math.Round(Fraction * 100):0} %";

    partial void OnDisplayedChanged(int value)
    {
        OnPropertyChanged(nameof(Fraction));
        OnPropertyChanged(nameof(Percent));
    }
}
