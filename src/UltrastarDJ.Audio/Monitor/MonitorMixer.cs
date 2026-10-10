using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio.Dsp;
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
                // Mix only turns a mic down (0–100 %): boosting is the input gain's job (Audio Input).
                s.Gain = (float)Math.Clamp(gain, 0, 1);
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
            s.Track(frames);
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
        // A jump back to the lead is a small click — audible only while that mic carries a voice (crackling evidence).
        foreach (Source s in _sources)
        {
            if (s.Starved + s.FarBehind > 0)
            {
                _log.LogWarning("Monitor: player {Player} jumped {Starved}× (ran dry) and {Behind}× (fell behind); largest mic block {Block}",
                    s.Pipeline.PlayerId, s.Starved, s.FarBehind, s.Pipeline.Monitor.LargestWrite);
            }
            else
            {
                _log.LogDebug("Monitor: player {Player} played without jumps; largest mic block {Block}", s.Pipeline.PlayerId, s.Pipeline.Monitor.LargestWrite);
            }
        }

        lock (_gate)
        {
            _sources = [];
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Reads one pipeline's monitor ring at the output rate (linear interpolation). Mic and output are separate
    /// devices: the mic writes in bursts (its block size), the output reads in its own, and their clocks drift apart
    /// slightly. The reader therefore stays a lead behind the writer that covers both blocks, and nudges its speed
    /// (at most ±0.3 %, inaudible) to hold that lead. A fixed 512-sample lead ran dry with 512+ frame USB blocks —
    /// the reader overtook the writer and played gaps: a metallic, robotic voice.
    /// </summary>
    private sealed class Source(MicPipeline pipeline, double ratio)
    {
        private const int Margin = 128;
        // Learned on top of the lead each time the reader ran dry (GC pauses hold the mic's callback up), up to ~50 ms.
        private const int LearnStep = 480;
        private const int MaxLearned = 2400;
        // Bursts up to this many samples are covered (USB mics deliver 256–1024); bigger ones are stalls, not blocks.
        private const int MaxBurst = 2048;
        private const double MaxNudge = 0.003;
        private readonly float[] _buf = new float[8192];
        private double _pos = -1;    // absolute (input-rate) read position
        private double _fill;        // smoothed lead (written − read position), input samples
        private double _maxPerCallback;   // the largest output block seen, input samples
        private double _learned;          // extra lead after running dry
        private long _bufStart;
        private int _bufLen;

        public MicPipeline Pipeline { get; } = pipeline;
        /// <summary>Jumps back to the lead: about to run dry / fell far behind (each one a small click).</summary>
        public int Starved { get; private set; }
        public int FarBehind { get; private set; }
        public float Gain { get; set; } = 1f;
        public bool Muted { get; set; }
        private readonly double _ratio = ratio;   // nominal input samples per output sample
        private double _step = ratio;             // the same, nudged to hold the lead

        /// <summary>Audio thread, once per output callback: (re)places the read position and adjusts the speed.</summary>
        public void Track(int outFrames)
        {
            SampleRing ring = Pipeline.Monitor;
            long written = ring.TotalWritten;
            double perCallback = outFrames * _ratio;
            // Sized for the largest output block so far (macOS asks for varying amounts), plus what running dry taught.
            _maxPerCallback = Math.Max(_maxPerCallback, perCallback);
            double target = Math.Min(ring.LargestWrite, MaxBurst) + 1.5 * _maxPerCallback + Margin + _learned;
            if (_pos < 0)
            {
                // Wait for the lead to exist: started right after the mics (song replay) the ring is still nearly
                // empty, and a negative read position crashed the audio callback.
                if (written >= target)
                {
                    Resync(written, target);
                }

                return;
            }

            double fill = written - _pos;
            if (fill < perCallback + 2 || fill > ring.Capacity / 2.0)
            {
                // About to starve, or far behind (a stall): jump back to the lead instead of playing gaps or lag.
                if (fill < perCallback + 2)
                {
                    Starved++;
                    _learned = Math.Min(_learned + LearnStep, MaxLearned);
                    target += LearnStep;
                }
                else
                {
                    FarBehind++;
                }

                Resync(written, target);
                return;
            }

            _fill += 0.05 * (fill - _fill);
            double nudge = Math.Clamp((_fill - target) / target * 0.01, -MaxNudge, MaxNudge);
            _step = _ratio * (1 + nudge);
        }

        private void Resync(long written, double target)
        {
            _pos = written - target;
            _fill = target;
            _step = _ratio;
        }

        public float Next()
        {
            if (_pos < 0)
            {
                return 0;
            }

            long i0 = (long)Math.Floor(_pos);
            float a = Sample(i0);
            float b = Sample(i0 + 1);
            float frac = (float)(_pos - i0);
            _pos += _step;
            return a + (b - a) * frac;
        }

        public void SkipAhead(int frames) => _pos = _pos < 0 ? -1 : _pos + frames * _step;

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
