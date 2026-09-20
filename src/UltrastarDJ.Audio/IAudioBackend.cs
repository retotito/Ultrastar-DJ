namespace UltrastarDJ.Audio;

/// <summary>
/// An audio device as seen by the backend. <see cref="Id"/> is the device name — stable across
/// hot-plug (PortAudio indexes are not) and identical to what CoreAudio/WASAPI show the user.
/// </summary>
public sealed record AudioDeviceInfo(string Id, string Name, int MaxInputChannels, int MaxOutputChannels, double DefaultSampleRateHz, bool IsDefaultInput, bool IsDefaultOutput)
{
    public bool IsInput => MaxInputChannels > 0;
    public bool IsOutput => MaxOutputChannels > 0;
}

/// <summary>
/// Called on the backend's realtime thread. <paramref name="interleaved"/> holds <paramref name="frames"/> ×
/// channels samples. Must not allocate, lock, log or throw.
/// </summary>
public delegate void AudioInputCallback(ReadOnlySpan<float> interleaved, int frames, int channels);

/// <summary>Fills <paramref name="interleaved"/> (zeroed) with <paramref name="frames"/> × channels samples. Same rules as input.</summary>
public delegate void AudioOutputCallback(Span<float> interleaved, int frames, int channels);

public interface IAudioLine : IDisposable
{
    bool IsActive { get; }
    double SampleRateHz { get; }
    int Channels { get; }
}

/// <summary>
/// Device enumeration and stream opening. One implementation per platform library (PortAudio).
/// Streams are callback-driven; everything else in the app consumes their output through lock-free buffers.
/// </summary>
public interface IAudioBackend : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> Devices { get; }

    /// <summary>
    /// Re-enumerates devices. Only possible while no stream is open (PortAudio must re-initialise);
    /// returns false when streams are active and the list is left unchanged.
    /// </summary>
    bool RefreshDevices();

    AudioDeviceInfo? Find(string deviceId);

    /// <summary>Opens all input channels of a device at its default rate. Callback delivers interleaved frames.</summary>
    IAudioLine OpenInput(string deviceId, int channels, double? sampleRateHz, AudioInputCallback callback);

    /// <summary>
    /// Opens an output. <paramref name="channelOffset"/> selects the first channel of a stereo pair on multichannel
    /// interfaces; the stream is opened with enough channels and the callback writes only that pair.
    /// </summary>
    IAudioLine OpenOutput(string deviceId, int channelOffset, double? sampleRateHz, AudioOutputCallback callback);
}

public sealed class AudioBackendException(string message, Exception? inner = null) : Exception(message, inner);
