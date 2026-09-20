namespace UltrastarDJ.Media;

/// <summary>
/// One media pipeline (one libmpv handle). Thread-safe for control calls; events are raised on
/// background threads and carry no UI affinity.
/// </summary>
public interface IMediaPlayer : IAsyncDisposable
{
    /// <summary>Human-readable role used in logs ("game-audio", "game-visual", "preview").</summary>
    string Name { get; }

    /// <summary>
    /// Opens <paramref name="source"/> and returns when the file is loaded and, for network sources,
    /// enough is cached to start (state <see cref="MediaState.Ready"/>), or throws <see cref="MediaException"/>.
    /// </summary>
    Task LoadAsync(MediaSource source, MediaLoadOptions options, CancellationToken ct = default);

    void Play();
    void Pause();
    /// <summary>Stops and unloads the file. The player can be reused with <see cref="LoadAsync"/>.</summary>
    void Unload();
    void Seek(TimeSpan position);

    /// <summary>Position inside the file (not game time). Updated from mpv's <c>time-pos</c>.</summary>
    TimeSpan Position { get; }
    TimeSpan? Duration { get; }
    MediaState State { get; }

    /// <summary>0..1 linear. Mapped to mpv's 0..100.</summary>
    double Volume { get; set; }
    bool Muted { get; set; }

    /// <summary>Playback speed (1.0 normal). Used by the clock follower to nudge drift.</summary>
    double Speed { get; set; }

    /// <summary>mpv <c>audio-device</c> id. <c>"auto"</c> = system default. Can be changed while playing.</summary>
    string AudioDevice { get; set; }

    /// <summary>Latest RMS level 0..1 of the output audio (0 when not metering).</summary>
    double LevelRms { get; }

    /// <summary>Audio outputs the backend can address. First entry is always the system default.</summary>
    IReadOnlyList<AudioOutputDevice> ListAudioDevices();

    /// <summary>Non-null when the player decodes video; publish through a <see cref="FrameBus"/>.</summary>
    IFrameSource? Frames { get; }

    event Action<MediaState>? StateChanged;
    event Action<string>? ErrorOccurred;
    event Action? EndReached;
}

public sealed class MediaException(string message) : Exception(message);

/// <summary>Creates players at runtime; channels own the instances.</summary>
public interface IMediaPlayerFactory
{
    IMediaPlayer Create(string name, bool video);
}
