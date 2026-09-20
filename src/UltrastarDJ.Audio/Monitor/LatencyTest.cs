using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Monitor;

/// <summary>
/// Measures mic round-trip latency: play a short beep on the output, detect its onset in the mic.
/// The result includes output latency and acoustic path — for karaoke that is the practical number.
/// </summary>
public sealed class LatencyTest(IAudioBackend backend, ILogger<LatencyTest> log)
{
    private const double BeepHz = 1000;
    private const double BeepSec = 0.06;
    private const double TrialTimeoutSec = 1.5;
    private const double GapBetweenTrialsSec = 0.4;
    // Output latency alone exceeds 10 ms; anything faster is noise, anything slower than 800 ms is not this beep.
    private const double MinPlausibleMs = 12;
    private const double MaxPlausibleMs = 800;

    public sealed record Result(IReadOnlyList<double> TrialsMs, double MedianMs, double NoiseFloor);

    /// <param name="outputDeviceId">Where the beep plays (the game output).</param>
    /// <param name="mic">The player's mic.</param>
    /// <param name="trials">Odd numbers give a clean median.</param>
    /// <param name="progress">Reports trial results as they complete.</param>
    /// <param name="ct">Cancels between trials.</param>
    public async Task<Result> RunAsync(string outputDeviceId, MicBinding mic, int trials = 5, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        AudioDeviceInfo? inInfo = backend.Find(mic.DeviceId) ?? throw new AudioBackendException($"Mic device not found: {mic.DeviceId}");
        AudioDeviceInfo? outInfo = backend.Find(outputDeviceId) ?? throw new AudioBackendException($"Output device not found: {outputDeviceId}");

        Capture capture = new(mic.Channel, inInfo.DefaultSampleRateHz);
        Beeper beeper = new(outInfo.DefaultSampleRateHz);
        List<double> results = [];

        using IAudioLine input = backend.OpenInput(inInfo.Id, Math.Min(2, inInfo.MaxInputChannels), null, capture.OnInput);
        using IAudioLine output = backend.OpenOutput(outInfo.Id, 0, null, beeper.OnOutput);

        // Let both streams settle and measure the noise floor.
        await Task.Delay(500, ct).ConfigureAwait(false);
        double floor = capture.PeakSinceReset;
        log.LogInformation("Latency test: noise floor {Floor:F5}", floor);

        for (int t = 0; t < trials; t++)
        {
            ct.ThrowIfCancellationRequested();
            double threshold = Math.Max(0.00025, floor * 6);
            capture.Arm(threshold);
            capture.ResetPeak();
            beeper.Trigger();
            Stopwatch sw = Stopwatch.StartNew();
            while (!capture.Detected && sw.Elapsed.TotalSeconds < TrialTimeoutSec)
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
            }

            if (capture.Detected)
            {
                double ms = (capture.DetectedAt - beeper.TriggeredAt).TotalMilliseconds;
                if (ms is < MinPlausibleMs or > MaxPlausibleMs)
                {
                    log.LogWarning("Latency trial {Trial}: {Ms:F0} ms rejected as implausible (peak {Peak:F3}, threshold {Threshold:F3})", t + 1, ms, capture.PeakSinceReset, threshold);
                }
                else
                {
                    results.Add(ms);
                    progress?.Report(ms);
                    log.LogInformation("Latency trial {Trial}: {Ms:F0} ms (peak {Peak:F3}, threshold {Threshold:F3})", t + 1, ms, capture.PeakSinceReset, threshold);
                }
            }
            else
            {
                log.LogWarning("Latency trial {Trial}: no echo detected (peak {Peak:F5} < threshold {Threshold:F5}; beep played {Played}/{Len} samples)",
                    t + 1, capture.PeakSinceReset, threshold, beeper.Played, beeper.Length);
            }

            await Task.Delay(TimeSpan.FromSeconds(GapBetweenTrialsSec), ct).ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            throw new AudioBackendException("No beep was detected by the microphone. Raise the speaker volume or move the mic closer.");
        }

        double[] sorted = results.Order().ToArray();
        double median = sorted[sorted.Length / 2];
        return new Result(results, median, floor);
    }

    private sealed class Beeper(double rate)
    {
        private readonly int _len = (int)(rate * BeepSec);
        private int _pos = int.MaxValue;
        private long _triggerTicks;

        public DateTime TriggeredAt => new(_triggerTicks, DateTimeKind.Utc);
        public int Length => _len;
        public int Played => Math.Min(_len, Volatile.Read(ref _pos));

        public void Trigger()
        {
            Volatile.Write(ref _triggerTicks, DateTime.UtcNow.Ticks);
            Volatile.Write(ref _pos, 0);
        }

        public void OnOutput(Span<float> interleaved, int frames, int channels)
        {
            int pos = _pos;
            if (pos >= _len)
            {
                return;
            }

            for (int i = 0; i < frames && pos < _len; i++, pos++)
            {
                // Short raised-cosine envelope avoids clicks that would trigger early.
                float env = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * pos / _len)));
                float v = 0.9f * env * (float)Math.Sin(2 * Math.PI * BeepHz * pos / rate);
                interleaved[i * channels] += v;
                interleaved[i * channels + 1] += v;
            }

            _pos = pos;
        }
    }

    /// <summary>
    /// Detects the beep with a Goertzel filter tuned to <see cref="BeepHz"/> over 5 ms sub-blocks — far more
    /// selective than a broadband peak, which is what quiet mics in a noisy room need.
    /// </summary>
    private sealed class Capture(MicChannelSide channel, double rate)
    {
        private readonly int _block = Math.Max(32, (int)(rate * 0.010));
        private readonly double _coeff = 2 * Math.Cos(2 * Math.PI * BeepHz / rate);
        private double _s1, _s2;
        private int _n;
        private float _peak;              // max tone power seen since reset (normalised per block)
        private float _threshold;
        private int _armed;
        private int _hits;                // consecutive sub-blocks above threshold; two are required
        private long _detectedTicks;
        private long _firstHitTicks;

        public double PeakSinceReset => Volatile.Read(ref _peak);
        public bool Detected => Volatile.Read(ref _armed) == 2;
        public DateTime DetectedAt => new(Volatile.Read(ref _detectedTicks), DateTimeKind.Utc);

        public void Arm(double threshold)
        {
            _threshold = (float)threshold;
            _hits = 0;
            Volatile.Write(ref _armed, 1);
        }

        public void ResetPeak() => Volatile.Write(ref _peak, 0);

        public void OnInput(ReadOnlySpan<float> interleaved, int frames, int channels)
        {
            int ch = channel == MicChannelSide.Right && channels > 1 ? 1 : 0;
            float peak = _peak;
            int armed = Volatile.Read(ref _armed);
            long blockArrival = DateTime.UtcNow.Ticks;
            for (int i = 0; i < frames; i++)
            {
                float s = channel == MicChannelSide.Mono && channels > 1
                    ? 0.5f * (interleaved[i * channels] + interleaved[i * channels + 1])
                    : interleaved[i * channels + ch];

                double s0 = s + _coeff * _s1 - _s2;
                _s2 = _s1;
                _s1 = s0;
                if (++_n < _block)
                {
                    continue;
                }

                // Tone power for this sub-block, normalised so the threshold is rate-independent.
                double power = (_s1 * _s1 + _s2 * _s2 - _coeff * _s1 * _s2) / ((double)_block * _block);
                _s1 = _s2 = 0;
                _n = 0;
                float p = (float)Math.Sqrt(Math.Max(0, power));
                if (p > peak)
                {
                    peak = p;
                }

                if (armed == 1)
                {
                    long ticks = blockArrival - (long)((frames - i) / rate * TimeSpan.TicksPerSecond);
                    if (p >= _threshold)
                    {
                        if (_hits++ == 0)
                        {
                            _firstHitTicks = ticks - (long)(_block / rate * TimeSpan.TicksPerSecond); // onset = start of the first hot block
                        }

                        if (_hits >= 2)
                        {
                            Volatile.Write(ref _detectedTicks, _firstHitTicks);
                            Volatile.Write(ref _armed, 2);
                            armed = 2;
                        }
                    }
                    else
                    {
                        _hits = 0;
                    }
                }
            }

            _peak = peak;
        }
    }
}
