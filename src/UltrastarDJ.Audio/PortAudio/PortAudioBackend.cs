using Microsoft.Extensions.Logging;
using PortAudioSharp;
using PaNativeStream = PortAudioSharp.Stream;

namespace UltrastarDJ.Audio.PortAudio;

/// <summary>
/// <see cref="IAudioBackend"/> over PortAudio. Holds Pa_Initialize for its lifetime; device refresh
/// re-initialises, which is why it is refused while streams exist.
/// </summary>
public sealed class PortAudioBackend : IAudioBackend
{
    private static readonly Lock InitLock = new();
    private readonly ILogger<PortAudioBackend> _log;
    private readonly HashSet<PaStream> _streams = [];
    private List<AudioDeviceInfo> _devices = [];
    private Dictionary<string, int> _indexById = [];
    private bool _initialised;

    public PortAudioBackend(ILogger<PortAudioBackend> log)
    {
        _log = log;
        Initialise();
        _log.LogInformation("PortAudio {Version}: {Count} devices", PortAudioSharp.PortAudio.VersionInfo.versionText, _devices.Count);
    }

    public IReadOnlyList<AudioDeviceInfo> Devices => _devices;

    public AudioDeviceInfo? Find(string deviceId) => _devices.FirstOrDefault(d => d.Id == deviceId);

    public bool RefreshDevices()
    {
        lock (InitLock)
        {
            if (_streams.Count > 0)
            {
                return false;
            }

            PortAudioSharp.PortAudio.Terminate();
            _initialised = false;
            Initialise();
            return true;
        }
    }

    private void Initialise()
    {
        lock (InitLock)
        {
            if (!_initialised)
            {
                PortAudioSharp.PortAudio.Initialize();
                _initialised = true;
            }

            List<AudioDeviceInfo> list = [];
            Dictionary<string, int> index = [];
            int defIn = PortAudioSharp.PortAudio.DefaultInputDevice;
            int defOut = PortAudioSharp.PortAudio.DefaultOutputDevice;
            for (int i = 0; i < PortAudioSharp.PortAudio.DeviceCount; i++)
            {
                DeviceInfo d = PortAudioSharp.PortAudio.GetDeviceInfo(i);
                string id = d.name;
                // Two identical names (rare: identical dongles without serials) get a suffix so both stay addressable.
                int n = 2;
                while (index.ContainsKey(id))
                {
                    id = $"{d.name} ({n++})";
                }

                index[id] = i;
                list.Add(new AudioDeviceInfo(id, d.name, d.maxInputChannels, d.maxOutputChannels, d.defaultSampleRate, i == defIn, i == defOut));
            }

            _devices = list;
            _indexById = index;
        }
    }

    public IAudioLine OpenInput(string deviceId, int channels, double? sampleRateHz, AudioInputCallback callback)
    {
        (int index, DeviceInfo info) = Resolve(deviceId);
        channels = Math.Clamp(channels, 1, Math.Max(1, info.maxInputChannels));
        double rate = sampleRateHz ?? info.defaultSampleRate;
        StreamParameters p = new()
        {
            device = index,
            channelCount = channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = nint.Zero,
        };

        PaStream stream = new(this, deviceId, rate, channels, channelOffset: 0);
        stream.Open(p, null, callback, null);
        _log.LogInformation("Input opened: {Device} ({Channels} ch @ {Rate} Hz)", deviceId, channels, rate);
        return stream;
    }

    public IAudioLine OpenOutput(string deviceId, int channelOffset, double? sampleRateHz, AudioOutputCallback callback)
    {
        (int index, DeviceInfo info) = Resolve(deviceId);
        int channels = Math.Clamp(channelOffset + 2, 2, Math.Max(2, info.maxOutputChannels));
        double rate = sampleRateHz ?? info.defaultSampleRate;
        StreamParameters p = new()
        {
            device = index,
            channelCount = channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowOutputLatency,
            hostApiSpecificStreamInfo = nint.Zero,
        };

        PaStream stream = new(this, deviceId, rate, channels, channelOffset);
        stream.Open(null, p, null, callback);
        _log.LogInformation("Output opened: {Device} (ch {From}-{To} of {Channels} @ {Rate} Hz)", deviceId, channelOffset + 1, channelOffset + 2, channels, rate);
        return stream;
    }

    private (int, DeviceInfo) Resolve(string deviceId)
    {
        if (!_indexById.TryGetValue(deviceId, out int index))
        {
            throw new AudioBackendException($"Audio device not found: {deviceId}");
        }

        return (index, PortAudioSharp.PortAudio.GetDeviceInfo(index));
    }

    internal void Register(PaStream s)
    {
        lock (InitLock)
        {
            _streams.Add(s);
        }
    }

    internal void Unregister(PaStream s)
    {
        lock (InitLock)
        {
            _streams.Remove(s);
        }
    }

    public void Dispose()
    {
        foreach (PaStream s in _streams.ToArray())
        {
            s.Dispose();
        }

        lock (InitLock)
        {
            if (_initialised)
            {
                PortAudioSharp.PortAudio.Terminate();
                _initialised = false;
            }
        }
    }

    /// <summary>One PortAudio stream. The callback marshals raw pointers into spans and forwards them.</summary>
    internal sealed unsafe class PaStream(PortAudioBackend owner, string deviceId, double rate, int channels, int channelOffset) : IAudioLine
    {
        private PaNativeStream? _stream;
        private AudioInputCallback? _input;
        private AudioOutputCallback? _output;
        private PaNativeStream.Callback? _paCallback; // kept alive: PortAudio holds only the unmanaged thunk

        public string DeviceId => deviceId;
        public double SampleRateHz => rate;
        public int Channels => channels;
        public int ChannelOffset => channelOffset;
        public bool IsActive => _stream?.IsActive ?? false;

        public void Open(StreamParameters? inParams, StreamParameters? outParams, AudioInputCallback? input, AudioOutputCallback? output)
        {
            _input = input;
            _output = output;
            _paCallback = OnCallback;
            try
            {
                _stream = new PaNativeStream(inParams, outParams, rate, PortAudioSharp.PortAudio.FramesPerBufferUnspecified, StreamFlags.ClipOff, _paCallback, null);
                _stream.Start();
            }
            catch (PortAudioException ex)
            {
                _stream?.Dispose();
                _stream = null;
                throw new AudioBackendException($"{deviceId}: {ex.Message}", ex);
            }

            owner.Register(this);
        }

        private StreamCallbackResult OnCallback(nint input, nint output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, nint userData)
        {
            int frames = (int)frameCount;
            if (_input is not null && input != 0)
            {
                _input(new ReadOnlySpan<float>((void*)input, frames * channels), frames, channels);
            }

            if (_output is not null && output != 0)
            {
                Span<float> span = new((void*)output, frames * channels);
                span.Clear();
                _output(span, frames, channels);
            }

            return StreamCallbackResult.Continue;
        }

        public void Dispose()
        {
            if (_stream is null)
            {
                return;
            }

            try
            {
                if (!_stream.IsStopped)
                {
                    _stream.Stop();
                }
            }
            catch (PortAudioException)
            {
                // Device may already be gone; closing still releases the handle.
            }

            _stream.Dispose();
            _stream = null;
            owner.Unregister(this);
        }
    }
}
