using System.Collections.ObjectModel;
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
    [ObservableProperty] private string _position = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private double _gain = 1.0;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private double _cardX;
    [ObservableProperty] private double _cardY;

    public NowPlayingViewModel(PlaybackService playback, IDisplayService displays, MediaService media, AudioInputService audio,
        PlayersService players, AppSettingsService settings, NotificationService notifications, ILogger<NowPlayingViewModel> log)
    {
        _playback = playback;
        _displays = displays;
        _media = media;
        _audio = audio;
        _players = players;
        _settings = settings;
        _notifications = notifications;
        _log = log;
        _gain = media.Game.Gain;
        _isVisible = !settings.NowPlayingHidden;
        if (settings.NowPlayingPosition is { } pos)
        {
            (_cardX, _cardY) = pos;
            HasSavedPosition = true;
        }

        _playback.StateChanged += _ => Refresh();
        _displays.OpenStateChanged += (_, _) => Refresh();
        _players.Changed += OnPlayerChanged;
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
        Refresh();
    }

    public PlaybackState State => _playback.State;
    public bool HasSong => _playback.Song is not null;
    public double Level => _media.Game.LevelRms;
    /// <summary>Small monitor of what the beamers show.</summary>
    public Media.FrameBus GameFrames => _media.Game.Frames;
    /// <summary>One row per player that will sing (mic bound, assigned to an open beamer).</summary>
    public ObservableCollection<MixRowViewModel> MixRows { get; } = [];
    public bool HasMixRows => MixRows.Count > 0;
    /// <summary>False until the user has dragged the card once; the view then places it at its default spot.</summary>
    public bool HasSavedPosition { get; private set; }

    partial void OnGainChanged(double value) => _media.Game.Gain = value;
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
        Title = song?.Title ?? "No song loaded";
        Artist = song?.Artist ?? "";
        SyncRows();
        Status = _playback.State switch
        {
            PlaybackState.Idle => "Load a song from the library",
            PlaybackState.Loaded when !_playback.AnyDisplayOpen => "Open a beamer under Displays",
            PlaybackState.Loaded => "Ready",
            PlaybackState.Preview => "Get-ready screen on the beamers",
            PlaybackState.Countdown => "3 – 2 – 1 …",
            PlaybackState.Playing => "Playing",
            PlaybackState.Paused => "Paused",
            PlaybackState.Score => "Score screen — Dismiss to play again",
            _ => "",
        };
        Error = _playback.LastError ?? "";
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(HasSong));
        LoadCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        DismissCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
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

        if (_playback.Clock is { } clock && _playback.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            double pos = clock.PositionSec;
            Position = $"{(int)pos / 60}:{(int)pos % 60:00}";
        }
        else
        {
            Position = "";
        }
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
        Error = "";
        IsVisible = true;
        Status = $"Loading {song.Title}…";
        try
        {
            await _playback.LoadAsync(song);
        }
        catch (SongLoadException ex)
        {
            Error = ex.Message;
            _log.LogWarning("Load failed: {Error}", ex.Message);
            _notifications.ShowDialog("Song cannot be loaded", ex.Message);
        }
        finally
        {
            Loading = false;
            Refresh();
        }
    }

    private bool CanLoad(Song? song) => _playback.CanLoad && !Loading;

    [RelayCommand(CanExecute = nameof(CanPreview))] private void Preview() => _playback.Preview();
    private bool CanPreview() => _playback.CanPreview;

    [RelayCommand(CanExecute = nameof(CanPlay))] private void Play() => _playback.Play();
    private bool CanPlay() => _playback.CanPlay;

    [RelayCommand(CanExecute = nameof(CanPause))] private void Pause() => _playback.Pause();
    private bool CanPause() => _playback.CanPause;

    [RelayCommand(CanExecute = nameof(CanResume))] private void Resume() => _playback.Resume();
    private bool CanResume() => _playback.CanResume;

    [RelayCommand(CanExecute = nameof(CanStop))] private void Stop() => _playback.Stop();
    private bool CanStop() => _playback.CanStop;

    [RelayCommand(CanExecute = nameof(CanDismiss))] private void Dismiss() => _playback.Dismiss();
    private bool CanDismiss() => _playback.State == PlaybackState.Score;

    [RelayCommand(CanExecute = nameof(CanClear))] private Task ClearAsync() => _playback.ClearAsync();
    private bool CanClear() => _playback.State is not PlaybackState.Idle && !Loading;

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
