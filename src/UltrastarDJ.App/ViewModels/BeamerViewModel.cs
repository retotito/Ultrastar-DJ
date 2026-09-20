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
    private readonly DispatcherTimer _countdown;
    private readonly DispatcherTimer _scoreAnim;
    private DateTime _scoreAnimStart;

    [ObservableProperty] private PlaybackState _state = PlaybackState.Idle;
    [ObservableProperty] private bool _hasVideo;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private Bitmap? _background;
    [ObservableProperty] private int _countdownValue;
    [ObservableProperty] private GameScene? _scene;
    [ObservableProperty] private int _winnerId = -1;

    public BeamerViewModel(DisplayId id, FrameBus gameFrames, PlaybackService playback, PlayersService players, IDisplayService displays)
    {
        Id = id;
        GameFrames = gameFrames;
        _playback = playback;
        _players = players;
        _displays = displays;
        HasVideo = gameFrames.HasSource;
        gameFrames.SourceChanged += OnFramesChanged;
        _playback.StateChanged += OnPlaybackStateChanged;
        _playback.PitchTicked += OnPitchTicked;
        _countdown = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => CountdownTick());
        _scoreAnim = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => AnimateScores());
        OnPlaybackStateChanged(_playback.State);
    }

    public DisplayId Id { get; }
    public string Label => $"Beamer {(int)Id}";
    public FrameBus GameFrames { get; }
    public ObservableCollection<ScenePlayer> AssignedPlayers { get; } = [];
    public ObservableCollection<ScoreRowViewModel> Scores { get; } = [];

    public bool IsIdle => State is PlaybackState.Idle or PlaybackState.Loaded;
    public bool IsPreview => State == PlaybackState.Preview;
    public bool IsCountdown => State == PlaybackState.Countdown;
    public bool IsGame => State is PlaybackState.Playing or PlaybackState.Paused;
    public bool IsPaused => State == PlaybackState.Paused;
    public bool IsScore => State == PlaybackState.Score;
    public bool ShowBackground => !IsIdle;
    public bool ShowVideo => HasVideo && State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused;

    partial void OnStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(IsCountdown));
        OnPropertyChanged(nameof(IsGame));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsScore));
        OnPropertyChanged(nameof(ShowBackground));
        OnPropertyChanged(nameof(ShowVideo));
    }

    partial void OnHasVideoChanged(bool value) => OnPropertyChanged(nameof(ShowVideo));

    private void OnFramesChanged() => Dispatcher.UIThread.Post(() => HasVideo = GameFrames.HasSource);

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;
        switch (state)
        {
            case PlaybackState.Idle:
                _countdown.Stop();
                _scoreAnim.Stop();
                Scene = null;
                Background = null;
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
                if (Scene is null && _playback.Session is { } session && _playback.Clock is { } clock)
                {
                    RefreshAssignedPlayers();
                    Scene = new GameScene(session, [.. AssignedPlayers], () => clock.PositionSec);
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
        string? image = _playback.Plan?.Background == StaticBackground.Image ? song.BackgroundPath : song.CoverPath ?? song.BackgroundPath;
        Background = LoadBitmap(image);
    }

    private static Bitmap? LoadBitmap(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void RefreshAssignedPlayers()
    {
        AssignedPlayers.Clear();
        IReadOnlyList<int> ids = _displays.GetConfig(Id).PlayerIds;
        foreach (PlayerConfig p in _players.All.Where(p => ids.Contains(p.Id) && p.Mic is not null))
        {
            AssignedPlayers.Add(new ScenePlayer(p.Id, p.Name, PlayerBrush(p.Id), PlayerColor(p.Id)));
        }
    }

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
        if (scene is not null && _playback.Clock is { } clock)
        {
            scene.Apply(ticks, clock.PositionSec);
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
        WinnerId = standings.Count > 0 ? standings[0].PlayerId : -1;
        foreach ((int pid, int score, int max) in standings.OrderBy(s => s.PlayerId))
        {
            PlayerConfig cfg = _players.Get(pid);
            Scores.Add(new ScoreRowViewModel(pid, cfg.Name, PlayerBrush(pid), score, max, pid == WinnerId));
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
        }
    }

    public void Dispose()
    {
        _countdown.Stop();
        _scoreAnim.Stop();
        GameFrames.SourceChanged -= OnFramesChanged;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _playback.PitchTicked -= OnPitchTicked;
    }
}

public sealed partial class ScoreRowViewModel(int playerId, string name, IBrush brush, int final, int max, bool isWinner) : ObservableObject
{
    [ObservableProperty] private int _displayed;

    public int PlayerId { get; } = playerId;
    public string Name { get; } = name;
    public IBrush Brush { get; } = brush;
    public int Final { get; } = final;
    public int Max { get; } = max;
    public bool IsWinner { get; } = isWinner;
    public double Fraction => Max > 0 ? (double)Displayed / Max : 0;

    partial void OnDisplayedChanged(int value) => OnPropertyChanged(nameof(Fraction));
}
