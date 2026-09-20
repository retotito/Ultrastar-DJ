using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// Sprint 1 spike UI: drive both channels by hand and watch state, position, level and drift.
/// Replaced by the real preview player / Now Playing card in Sprint 5.
/// </summary>
public sealed partial class MediaLabPanelViewModel : ViewModelBase, IDisposable
{
    private readonly MediaService _media;
    private readonly ILogger<MediaLabPanelViewModel> _log;
    private readonly DispatcherTimer _poll;

    public MediaLabPanelViewModel(MediaService media, ILogger<MediaLabPanelViewModel> log)
    {
        _media = media;
        _log = log;
        Game = new ChannelLabViewModel(media.Game, log);
        Preview = new ChannelLabViewModel(media.Preview, log) { MaxHeight = 360 };
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => { Game.Poll(); Preview.Poll(); });
        _poll.Start();
        _ = RefreshDevicesAsync();
    }

    public ChannelLabViewModel Game { get; }
    public ChannelLabViewModel Preview { get; }
    public ObservableCollection<AudioOutputDevice> Devices { get; } = [];

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        try
        {
            IReadOnlyList<AudioOutputDevice> list = await _media.Game.ListAudioDevicesAsync();
            Devices.Clear();
            foreach (AudioOutputDevice d in list)
            {
                Devices.Add(d);
            }

            Game.SyncSelectedDevice(Devices);
            Preview.SyncSelectedDevice(Devices);
        }
        catch (MediaException ex)
        {
            _log.LogError(ex, "Device list failed");
        }
    }

    public void Dispose() => _poll.Stop();
}

public sealed partial class ChannelLabViewModel : ObservableObject
{
    private readonly MediaChannel _channel;
    private readonly ILogger _log;

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _audioPath = "";
    [ObservableProperty] private AudioOutputDevice? _selectedDevice;
    [ObservableProperty] private double _gain = 1.0;
    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _position = "0:00 / 0:00";
    [ObservableProperty] private double _level;
    [ObservableProperty] private string _drift = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _busy;

    public ChannelLabViewModel(MediaChannel channel, ILogger log)
    {
        _channel = channel;
        _log = log;
        _channel.ErrorOccurred += e => Dispatcher.UIThread.Post(() => Error = e);
    }

    public string Title => _channel.Kind.ToString();
    public IFrameSource Frames => _channel.Frames;
    public int MaxHeight { get; init; } = 720;

    public void SyncSelectedDevice(IEnumerable<AudioOutputDevice> devices)
        => SelectedDevice = devices.FirstOrDefault(d => d.Id == _channel.DeviceId) ?? devices.FirstOrDefault();

    partial void OnSelectedDeviceChanged(AudioOutputDevice? value)
    {
        if (value is not null)
        {
            _channel.DeviceId = value.Id;
        }
    }

    partial void OnGainChanged(double value) => _channel.Gain = value;

    /// <summary>
    /// Input is a YouTube id/URL or a local media path. If <see cref="AudioPath"/> is also set, the
    /// input becomes the muted visual and the audio path the audio authority (cases 2/3).
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        Error = "";
        Busy = true;
        try
        {
            SongMedia song = BuildSong();
            MediaPlan plan = MediaSourceResolver.Resolve(song, MaxHeight);
            Status = $"Loading (case {plan.Case})…";
            await _channel.LoadAsync(plan);
        }
        catch (MediaException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private SongMedia BuildSong()
    {
        string input = Input.Trim();
        string? yt = ExtractYouTubeId(input);
        string? audio = string.IsNullOrWhiteSpace(AudioPath) ? null : AudioPath.Trim();
        return new SongMedia
        {
            AudioPath = audio,
            YouTubeId = yt,
            VideoPath = yt is null && input.Length > 0 ? input : null,
        };
    }

    private static string? ExtractYouTubeId(string s)
    {
        if (s.Length == 11 && !s.Contains('/') && !s.Contains('.'))
        {
            return s;
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out Uri? uri) && uri.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsolutePath.Trim('/');
            }

            string? v = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
            return string.IsNullOrEmpty(v) ? null : v;
        }

        return null;
    }

    [RelayCommand] private void Play() => _channel.Play();
    [RelayCommand] private void Pause() => _channel.Pause();
    [RelayCommand] private async Task UnloadAsync() => await _channel.UnloadAsync();
    [RelayCommand] private void SeekBack() => _channel.Seek(Math.Max(0, (_channel.Clock?.PositionSec ?? 0) - 10));
    [RelayCommand] private void SeekForward() => _channel.Seek((_channel.Clock?.PositionSec ?? 0) + 10);

    public void Poll()
    {
        Status = _channel.State.ToString();
        Level = _channel.LevelRms;
        double pos = _channel.Clock?.PositionSec ?? 0;
        double? dur = _channel.Duration?.TotalSeconds;
        Position = $"{Fmt(pos)} / {(dur is { } d ? Fmt(d) : "?")}";
        Drift = _channel.Plan?.Visual is null ? "" : $"drift {_channel.VisualDriftSec * 1000:+0;-0} ms";
    }

    private static string Fmt(double sec) => $"{(int)sec / 60}:{(int)sec % 60:00}";
}
