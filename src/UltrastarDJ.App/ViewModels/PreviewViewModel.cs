using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// Preview player (DJ headphones). Plays on the preview channel with its own output device and fader;
/// hands songs on to the queue or the game.
/// </summary>
public sealed partial class PreviewViewModel : ViewModelBase, IDisposable
{
    private readonly MediaService _media;
    private readonly OutputsService _outputs;
    private readonly SongResolver _resolver;
    private readonly QueueViewModel _queue;
    private readonly NowPlayingViewModel _nowPlaying;
    private readonly NotificationService _notifications;
    private readonly ILogger<PreviewViewModel> _log;
    private readonly DispatcherTimer _poll;
    private bool _polling;

    [ObservableProperty] private Song? _song;
    [ObservableProperty] private Bitmap? _cover;
    [ObservableProperty] private bool _hasVideo;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _timeText = "0:00 / 0:00";
    [ObservableProperty] private double _gain;
    [ObservableProperty] private double _level;
    [ObservableProperty] private string _error = "";

    public PreviewViewModel(MediaService media, OutputsService outputs, SongResolver resolver, QueueViewModel queue, NowPlayingViewModel nowPlaying, NotificationService notifications, ILogger<PreviewViewModel> log)
    {
        _media = media;
        _outputs = outputs;
        _resolver = resolver;
        _queue = queue;
        _nowPlaying = nowPlaying;
        _notifications = notifications;
        _log = log;
        _gain = outputs.Preview.Gain;
        outputs.Changed += () => { _polling = true; Gain = outputs.Preview.Gain; _polling = false; };
        media.Preview.Frames.SourceChanged += () => Dispatcher.UIThread.Post(() => HasVideo = media.Preview.Frames.HasSource);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
    }

    public FrameBus Frames => _media.Preview.Frames;
    public bool HasSong => Song is not null;
    public bool ShowCover => HasSong && !HasVideo;
    public string Title => Song?.Title ?? "Preview player";
    public string Artist => Song?.Artist ?? "Double-click a song in the library";

    partial void OnSongChanged(Song? value)
    {
        OnPropertyChanged(nameof(HasSong));
        OnPropertyChanged(nameof(ShowCover));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        AddToQueueCommand.NotifyCanExecuteChanged();
        LoadIntoGameCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasVideoChanged(bool value) => OnPropertyChanged(nameof(ShowCover));

    partial void OnGainChanged(double value)
    {
        if (!_polling)
        {
            _outputs.SetPreviewGain(value);
        }
    }

    /// <summary>User dragged the progress slider.</summary>
    partial void OnFractionChanged(double value)
    {
        if (!_polling && _media.Preview.Duration is { } d && d.TotalSeconds > 0)
        {
            _media.Preview.Seek(value * d.TotalSeconds - (_media.Preview.Plan?.AudioOriginSec ?? 0));
        }
    }

    [RelayCommand]
    public async Task LoadAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        Error = "";
        Loading = true;
        try
        {
            Song resolved = await _resolver.ResolveAsync(song);
            Song = resolved;
            Cover = LoadBitmap(resolved.CoverPath ?? resolved.BackgroundPath);
            MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia
            {
                AudioPath = resolved.AudioPath,
                VideoPath = resolved.VideoPath,
                YouTubeId = resolved.YouTubeId,
                BackgroundPath = resolved.BackgroundPath,
                CoverPath = resolved.CoverPath,
                VideoGapSec = resolved.VideoGapSec ?? 0,
            }, maxHeight: 360);
            await _media.Preview.LoadAsync(plan);
            _media.Preview.Play();
        }
        catch (SongLoadException ex)
        {
            Error = ex.Message;
            _notifications.ShowDialog("Song cannot be previewed", ex.Message);
        }
        catch (MediaException ex)
        {
            Error = ex.Message;
            _log.LogWarning("Preview failed: {Error}", ex.Message);
        }
        finally
        {
            Loading = false;
        }
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (_media.Preview.State == MediaState.Playing)
        {
            _media.Preview.Pause();
        }
        else
        {
            _media.Preview.Play();
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _media.Preview.UnloadAsync();
        Song = null;
        Cover = null;
    }

    [RelayCommand(CanExecute = nameof(HasSong))]
    private void AddToQueue() => _queue.Add(Song!);

    [RelayCommand(CanExecute = nameof(HasSong))]
    private Task LoadIntoGameAsync() => _nowPlaying.LoadCommand.ExecuteAsync(Song);

    private void Poll()
    {
        MediaChannel ch = _media.Preview;
        IsPlaying = ch.State == MediaState.Playing;
        Level = ch.LevelRms;
        _polling = true;
        double pos = ch.Clock?.PositionSec ?? 0;
        double dur = ch.Duration?.TotalSeconds ?? 0;
        Fraction = dur > 0 ? Math.Clamp(pos / dur, 0, 1) : 0;
        TimeText = $"{Fmt(pos)} / {Fmt(dur)}";
        _polling = false;
    }

    private static string Fmt(double s) => $"{(int)s / 60}:{(int)s % 60:00}";

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

    public void Dispose() => _poll.Stop();
}
