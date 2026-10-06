using System.Diagnostics;

namespace UltrastarDJ.Audio.Monitor;

/// <summary>
/// Test sync: a short click every <see cref="PeriodSec"/> on an output, sample-exact. <see cref="FirstClickTimestamp"/>
/// is when the first click was handed to the driver (Stopwatch ticks), so a screen can flash at every click plus the
/// output latency setting — the same point in the chain mpv's clock reports for the song.
/// </summary>
public sealed class SyncClicks
{
    private const double ClickHz = 2000;
    private const double ClickSec = 0.015;

    private readonly double _rate;
    private readonly long _period;
    private readonly int _clickLen;
    private readonly Func<long> _now;
    private long _written;
    private long _firstClick = long.MaxValue;

    /// <param name="rate">Output sample rate.</param>
    /// <param name="periodSec">Click spacing.</param>
    /// <param name="now">Test seam for the Stopwatch.</param>
    public SyncClicks(double rate, double periodSec, Func<long>? now = null)
    {
        _rate = rate;
        _period = (long)Math.Round(rate * periodSec);
        _clickLen = (int)(rate * ClickSec);
        _now = now ?? Stopwatch.GetTimestamp;
        PeriodSec = _period / rate;
    }

    public double PeriodSec { get; }

    /// <summary>
    /// Stopwatch timestamp of sample 0, or null before the first callback. Callbacks come early while the driver
    /// buffer fills and late when the thread is woken late; "now − samples written" is earliest (most exact) at the
    /// steady state, so the minimum is kept.
    /// </summary>
    public long? FirstClickTimestamp => Volatile.Read(ref _firstClick) is var t && t != long.MaxValue ? t : null;

    /// <summary>Realtime output callback: no allocation, no lock.</summary>
    public void OnOutput(Span<float> interleaved, int frames, int channels)
    {
        long start = _written;
        long candidate = _now() - (long)(start * Stopwatch.Frequency / _rate);
        if (candidate < _firstClick)
        {
            Volatile.Write(ref _firstClick, candidate);
        }

        for (int i = 0; i < frames; i++)
        {
            long inPeriod = (start + i) % _period;
            if (inPeriod >= _clickLen)
            {
                continue;
            }

            // A decaying 2 kHz burst: a sharp, unmistakable onset that survives small speakers.
            float env = (float)Math.Exp(-inPeriod / (_clickLen / 5.0));
            float v = 0.7f * env * (float)Math.Sin(2 * Math.PI * ClickHz * inPeriod / _rate);
            interleaved[i * channels] += v;
            if (channels > 1)
            {
                interleaved[i * channels + 1] += v;
            }
        }

        _written = start + frames;
    }
}
