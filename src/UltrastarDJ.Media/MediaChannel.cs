using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Media;

public enum MediaChannelKind
{
    Game,
    Preview,
}

/// <summary>
/// One output path (game speakers or DJ headphones). Owns the players for the current song, the
/// output device, gain, metering, a <see cref="FrameBus"/> for its video and the <see cref="IGameClock"/>.
/// Long-lived; players are recreated per load.
/// </summary>
public sealed class MediaChannel : IAsyncDisposable
{
    private readonly IMediaPlayerFactory _factory;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IMediaPlayer? _audio;
    private IMediaPlayer? _visual;
    private ClockFollower? _follower;
    private MediaGameClock? _clock;
    private string _deviceId = AudioOutputDevice.Auto.Id;
    private double _gain = 1.0;
    private bool _disposed;

    public MediaChannel(MediaChannelKind kind, IMediaPlayerFactory factory, ILoggerFactory loggers)
    {
        Kind = kind;
        _factory = factory;
        _loggers = loggers;
        _log = loggers.CreateLogger($"UltrastarDJ.Media.MediaChannel.{kind}");
    }

    public MediaChannelKind Kind { get; }

    /// <summary>Video of the current song (from whichever player carries it). Surfaces subscribe once.</summary>
    public FrameBus Frames { get; } = new();

    public MediaPlan? Plan { get; private set; }

    /// <summary>Game time; valid after a successful <see cref="LoadAsync"/>.</summary>
    public IGameClock? Clock => _clock;

    public MediaState State => _audio?.State ?? MediaState.Idle;
    public TimeSpan Position => _audio?.Position ?? TimeSpan.Zero;
    public TimeSpan? Duration => _audio?.Duration;
    public double LevelRms => _audio?.LevelRms ?? 0;
    public double VisualDriftSec => _follower?.LastDriftSec ?? 0;

    public event Action<MediaState>? StateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action? EndReached;

    /// <summary>mpv audio device id. Applied immediately to the current audio player and to future ones.</summary>
    public string DeviceId
    {
        get => _deviceId;
        set
        {
            _deviceId = string.IsNullOrEmpty(value) ? AudioOutputDevice.Auto.Id : value;
            if (_audio is not null)
            {
                _audio.AudioDevice = _deviceId;
            }

            _log.LogInformation("{Channel}: output device → {Device}", Kind, _deviceId);
        }
    }

    /// <summary>0..1 linear gain.</summary>
    public double Gain
    {
        get => _gain;
        set
        {
            _gain = Math.Clamp(value, 0, 1);
            if (_audio is not null)
            {
                _audio.Volume = _gain;
            }
        }
    }

    public async Task<IReadOnlyList<AudioOutputDevice>> ListAudioDevicesAsync()
    {
        if (_audio is not null)
        {
            return _audio.ListAudioDevices();
        }

        // No player yet: spin up a throwaway one to ask mpv.
        IMediaPlayer probe = _factory.Create($"{Kind}-probe", video: false);
        await using (probe.ConfigureAwait(false))
        {
            return probe.ListAudioDevices();
        }
    }

    /// <summary>Loads a plan. Returns when every role is <see cref="MediaState.Ready"/>.</summary>
    public async Task LoadAsync(MediaPlan plan, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync().ConfigureAwait(false);
            Plan = plan;
            _log.LogInformation("{Channel}: case {Case} — audio {Audio}{Visual}", Kind, plan.Case, plan.Audio.Source,
                plan.Visual is null ? "" : $", visual {plan.Visual.Source}");

            _audio = _factory.Create($"{Kind}-audio", video: plan.AudioRoleHasVideo);
            _audio.AudioDevice = _deviceId;
            _audio.Volume = _gain;
            _audio.StateChanged += s => StateChanged?.Invoke(s);
            _audio.ErrorOccurred += e => ErrorOccurred?.Invoke(e);
            _audio.EndReached += () => EndReached?.Invoke();
            _clock = new MediaGameClock(_audio, plan.AudioOriginSec);

            Task audioLoad = _audio.LoadAsync(plan.Audio.Source, plan.Audio.Options, ct);
            Task visualLoad = Task.CompletedTask;

            if (plan.Visual is not null)
            {
                _visual = _factory.Create($"{Kind}-visual", video: true);
                _visual.Muted = true;
                _visual.ErrorOccurred += e => _log.LogWarning("{Channel}: visual failed, continuing without video: {Error}", Kind, e);
                visualLoad = _visual.LoadAsync(plan.Visual.Source, plan.Visual.Options, ct);
            }

            await audioLoad.ConfigureAwait(false);
            try
            {
                await visualLoad.ConfigureAwait(false);
            }
            catch (MediaException)
            {
                // Visual is optional: a blocked YouTube background must not stop the song.
                await DisposeVisualAsync().ConfigureAwait(false);
            }

            Frames.SetSource(plan.AudioRoleHasVideo ? _audio.Frames : _visual?.Frames);

            if (_visual is not null)
            {
                _follower = new ClockFollower(_visual, _clock, plan.VideoGapSec, _loggers.CreateLogger<ClockFollower>());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Play() => _audio?.Play();
    public void Pause() => _audio?.Pause();

    /// <summary>Seek in game time.</summary>
    public void Seek(double gameTimeSec)
    {
        if (_audio is null || Plan is null)
        {
            return;
        }

        _audio.Seek(TimeSpan.FromSeconds(gameTimeSec + Plan.AudioOriginSec));
        _visual?.Seek(TimeSpan.FromSeconds(gameTimeSec + Plan.VideoGapSec));
    }

    public async Task UnloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UnloadCoreAsync()
    {
        Frames.SetSource(null);
        if (_follower is not null)
        {
            await _follower.DisposeAsync().ConfigureAwait(false);
            _follower = null;
        }

        await DisposeVisualAsync().ConfigureAwait(false);
        if (_audio is not null)
        {
            await _audio.DisposeAsync().ConfigureAwait(false);
            _audio = null;
        }

        _clock = null;
        Plan = null;
    }

    private async Task DisposeVisualAsync()
    {
        if (_visual is not null)
        {
            await _visual.DisposeAsync().ConfigureAwait(false);
            _visual = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await UnloadAsync().ConfigureAwait(false);
        Frames.Dispose();
        _gate.Dispose();
    }
}
