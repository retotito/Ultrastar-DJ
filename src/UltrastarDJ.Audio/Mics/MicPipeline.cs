using UltrastarDJ.Audio.Dsp;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Mics;

/// <summary>Live per-player pitch sample as consumed by the scorer.</summary>
public readonly record struct PitchSample(int PlayerId, double MidiNote, double FrequencyHz, double Clarity, double Level);

/// <summary>
/// One player's signal chain: channel pick → input gain → noise gate → level → ring → YIN → median.
/// <see cref="Process"/> runs on the audio thread (no allocations); <see cref="Analyze"/> on a worker.
/// </summary>
public sealed class MicPipeline
{
    public const int WindowSize = 2048;
    private const double MinClarity = 0.9;

    private readonly SampleRing _ring;
    private readonly float[] _window = new float[WindowSize];
    private readonly YinDetector _yin;
    private readonly PitchRingBuffer _smoother = new(5);
    private double _levelRms;
    private double _lastMidi = -1;

    public MicPipeline(int playerId, MicChannelSide channel, double sampleRateHz)
    {
        PlayerId = playerId;
        Channel = channel;
        SampleRateHz = sampleRateHz;
        _yin = new YinDetector(WindowSize, sampleRateHz);
        // Half a second of history: enough for the monitor mixer to read behind the writer.
        _ring = new SampleRing((int)(sampleRateHz / 2));
        Monitor = new SampleRing((int)(sampleRateHz / 2));
    }

    public int PlayerId { get; }
    public MicChannelSide Channel { get; }
    public double SampleRateHz { get; }

    /// <summary>Input gain 0–10, applied before the gate. SingStar-class mics sit around −50 dBFS and need 4–8×.</summary>
    public double InputGain { get; set; } = 1.0;

    /// <summary>Noise gate on peak amplitude 0–0.5 (post-gain). Below it the block is treated as silence.</summary>
    public double Threshold { get; set; } = 0.01;

    /// <summary>RMS 0..1 of the last processed block (post-gain, pre-gate) — for the UI meter.</summary>
    public double LevelRms => Volatile.Read(ref _levelRms);

    /// <summary>Gated, gained mono samples for the monitor mixer.</summary>
    public SampleRing Monitor { get; }

    /// <summary>Audio thread: picks this player's channel out of the device's interleaved block.</summary>
    public void Process(ReadOnlySpan<float> interleaved, int frames, int channels, Span<float> scratch)
    {
        Span<float> mono = scratch[..frames];
        float gain = (float)InputGain;
        int ch = Channel == MicChannelSide.Right && channels > 1 ? 1 : 0;
        float peak = 0;
        double sumSq = 0;

        for (int i = 0; i < frames; i++)
        {
            float s;
            if (Channel == MicChannelSide.Mono && channels > 1)
            {
                s = 0.5f * (interleaved[i * channels] + interleaved[i * channels + 1]);
            }
            else
            {
                s = interleaved[i * channels + ch];
            }

            s *= gain;
            mono[i] = s;
            float a = Math.Abs(s);
            if (a > peak)
            {
                peak = a;
            }

            sumSq += s * s;
        }

        Volatile.Write(ref _levelRms, Math.Sqrt(sumSq / Math.Max(1, frames)));

        if (peak < Threshold)
        {
            mono.Clear();
        }

        _ring.Write(mono);
        Monitor.Write(mono);
    }

    /// <summary>Worker thread (~30 Hz): runs YIN on the latest window and updates the smoothed note.</summary>
    public PitchSample Analyze()
    {
        double freq = 0, clarity = 0, midi = -1;
        if (_ring.ReadLatest(_window))
        {
            (freq, clarity) = _yin.Detect(_window);
            bool voiced = freq > 0 && clarity >= MinClarity && !IsSilent(_window);
            _smoother.Push(voiced ? Math.Round(PitchMatching.HzToMidi(freq)) : -1);
            midi = _smoother.Median();
        }

        _lastMidi = midi;
        return new PitchSample(PlayerId, midi, freq, clarity, LevelRms);
    }

    public double LastMidiNote => _lastMidi;

    public void Reset() => _smoother.Reset();

    private static bool IsSilent(ReadOnlySpan<float> w)
    {
        // A gated (zeroed) window must not produce a pitch: YIN on all-zeros yields a spurious "perfect" period.
        for (int i = 0; i < w.Length; i += 64)
        {
            if (w[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
