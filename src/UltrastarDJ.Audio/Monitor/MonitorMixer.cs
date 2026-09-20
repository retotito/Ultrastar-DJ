using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio.Mics;

namespace UltrastarDJ.Audio.Monitor;

/// <summary>
/// Sums the players' gated mic signals (× mix gain, mute-aware) into one stereo output on the game
/// device. Mic and output rates may differ; a linear resampler bridges them.
/// </summary>
public sealed class MonitorMixer : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly ILogger<MonitorMixer> _log;
    private readonly Lock _gate = new();
    private Source[] _sources = [];
    private IAudioLine? _line;
    private int _channelOffset;

    public MonitorMixer(IAudioBackend backend, ILogger<MonitorMixer> log)
    {
        _backend = backend;
        _log = log;
    }

    public bool IsRunning => _line is not null;

    /// <summary>Opens the output and starts mixing the given pipelines.</summary>
    public void Start(string outputDeviceId, int channelOffset, IReadOnlyCollection<MicPipeline> pipelines)
    {
        Stop();
        AudioDeviceInfo? info = _backend.Find(outputDeviceId);
        if (info is null || !info.IsOutput)
        {
            _log.LogWarning("Monitor output missing: {Device}", outputDeviceId);
            return;
        }

        double outRate = info.DefaultSampleRateHz;
        lock (_gate)
        {
            _channelOffset = channelOffset;
            _sources = pipelines.Select(p => new Source(p, p.SampleRateHz / outRate)).ToArray();
        }

        try
        {
            _line = _backend.OpenOutput(info.Id, channelOffset, outRate, OnOutput);
            _log.LogInformation("Monitor mixer started on {Device} ({Players} mics)", info.Id, _sources.Length);
        }
        catch (AudioBackendException ex)
        {
            _log.LogError(ex, "Cannot open monitor output {Device}", info.Id);
        }
    }

    public void SetGain(int playerId, double gain)
    {
        foreach (Source s in _sources)
        {
            if (s.Pipeline.PlayerId == playerId)
            {
                s.Gain = (float)Math.Clamp(gain, 0, 2);
            }
        }
    }

    public void SetMuted(int playerId, bool muted)
    {
        foreach (Source s in _sources)
        {
            if (s.Pipeline.PlayerId == playerId)
            {
                s.Muted = muted;
            }
        }
    }

    // Audio thread.
    private void OnOutput(Span<float> interleaved, int frames, int channels)
    {
        Source[] sources = _sources;
        int off = _channelOffset;
        foreach (Source s in sources)
        {
            if (s.Muted || s.Gain <= 0)
            {
                s.SkipAhead(frames);
                continue;
            }

            for (int i = 0; i < frames; i++)
            {
                float v = s.Next() * s.Gain;
                interleaved[i * channels + off] += v;
                interleaved[i * channels + off + 1] += v;
            }
        }
    }

    public void Stop()
    {
        _line?.Dispose();
        _line = null;
        lock (_gate)
        {
            _sources = [];
        }
    }

    public void Dispose() => Stop();

    /// <summary>Reads one pipeline's monitor ring at the output rate (linear interpolation), keeping a small lead to absorb jitter.</summary>
    private sealed class Source(MicPipeline pipeline, double ratio)
    {
        private const int LeadSamples = 512; // read this far behind the writer so the callback never starves
        private readonly float[] _buf = new float[8192];
        private double _pos = -1;    // absolute (input-rate) read position
        private long _bufStart;
        private int _bufLen;

        public MicPipeline Pipeline { get; } = pipeline;
        public float Gain { get; set; } = 1f;
        public bool Muted { get; set; }

        public float Next()
        {
            if (_pos < 0)
            {
                _pos = Pipeline.Monitor.TotalWritten - LeadSamples;
            }

            long i0 = (long)Math.Floor(_pos);
            float a = Sample(i0);
            float b = Sample(i0 + 1);
            float frac = (float)(_pos - i0);
            _pos += ratio;
            return a + (b - a) * frac;
        }

        public void SkipAhead(int frames) => _pos = _pos < 0 ? -1 : _pos + frames * ratio;

        private float Sample(long index)
        {
            if (index < _bufStart || index >= _bufStart + _bufLen)
            {
                // Refill from the ring; if we fell behind, ReadFrom snaps forward and so do we.
                long written = Pipeline.Monitor.TotalWritten;
                if (index >= written)
                {
                    return 0;
                }

                _bufStart = index;
                _bufLen = Pipeline.Monitor.ReadFrom(index, _buf);
                if (_bufLen == 0)
                {
                    return 0;
                }

                if (index < written - Pipeline.Monitor.Capacity)
                {
                    _bufStart = written - Pipeline.Monitor.Capacity;
                    _pos = _bufStart;
                    index = _bufStart;
                }
            }

            return _buf[index - _bufStart];
        }
    }
}
