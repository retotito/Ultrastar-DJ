using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Transport controls + status for the game channel. Lives in the DJ window's right panel.</summary>
public sealed partial class NowPlayingViewModel : ViewModelBase, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly IDisplayService _displays;
    private readonly MediaService _media;
    private readonly ILogger<NowPlayingViewModel> _log;
    private readonly DispatcherTimer _poll;

    [ObservableProperty] private string _title = "No song loaded";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _position = "";
    [ObservableProperty] private string _players = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private double _gain = 1.0;

    public NowPlayingViewModel(PlaybackService playback, IDisplayService displays, MediaService media, ILogger<NowPlayingViewModel> log)
    {
        _playback = playback;
        _displays = displays;
        _media = media;
        _log = log;
        _gain = media.Game.Gain;
        _playback.StateChanged += _ => Refresh();
        _displays.OpenStateChanged += (_, _) => Refresh();
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => PollPosition());
        _poll.Start();
        Refresh();
    }

    public PlaybackState State => _playback.State;
    public bool HasSong => _playback.Song is not null;
    public double Level => _media.Game.LevelRms;
    /// <summary>Small monitor of what the beamers show.</summary>
    public Media.FrameBus GameFrames => _media.Game.Frames;

    partial void OnGainChanged(double value) => _media.Game.Gain = value;

    private void Refresh()
    {
        Song? song = _playback.Song;
        Title = song?.Title ?? "No song loaded";
        Artist = song?.Artist ?? "";
        Players = string.Join("  ", _playback.ActivePlayers().Select(p => p.Name));
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

    private void PollPosition()
    {
        OnPropertyChanged(nameof(Level));
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
        Status = $"Loading {song.Title}…";
        try
        {
            await _playback.LoadAsync(song);
        }
        catch (SongLoadException ex)
        {
            Error = ex.Message;
            _log.LogWarning("Load failed: {Error}", ex.Message);
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

    public void Dispose() => _poll.Stop();
}
