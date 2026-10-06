using UltrastarDJ.Audio.Dsp;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.Audio.Tests;

public class YinDetectorTests
{
    private const double Rate = 48000;

    private static float[] Sine(double hz, int n, double amp = 0.5, double phase = 0)
    {
        float[] s = new float[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / Rate + phase));
        }

        return s;
    }

    [Theory]
    [InlineData(82.41)]    // E2 — bass
    [InlineData(130.81)]   // C3
    [InlineData(261.63)]   // C4
    [InlineData(440.0)]    // A4
    [InlineData(659.25)]   // E5
    [InlineData(1046.5)]   // C6 — soprano
    public void Detect_PureSine_WithinThirdOfASemitone(double hz)
    {
        YinDetector yin = new(MicPipeline.WindowSize, Rate);

        (double f, double clarity) = yin.Detect(Sine(hz, MicPipeline.WindowSize));

        double cents = 1200 * Math.Log2(f / hz);
        Assert.InRange(cents, -33, 33);
        Assert.True(clarity > 0.9, $"clarity {clarity}");
    }

    [Fact]
    public void Detect_VoiceLikeHarmonics_FindsFundamentalNotHarmonic()
    {
        int n = MicPipeline.WindowSize;
        float[] s = new float[n];
        foreach ((double h, double a) in new[] { (1, 0.5), (2, 0.35), (3, 0.25), (4, 0.15) })
        {
            float[] part = Sine(220 * h, n, a);
            for (int i = 0; i < n; i++)
            {
                s[i] += part[i];
            }
        }

        (double f, _) = new YinDetector(n, Rate).Detect(s);

        Assert.InRange(f, 215, 225);
    }

    [Fact]
    public void Detect_Silence_ReturnsNoPitch()
    {
        (double f, double clarity) = new YinDetector(MicPipeline.WindowSize, Rate).Detect(new float[MicPipeline.WindowSize]);

        Assert.True(f == 0 || clarity < 0.5, $"silence produced f={f} clarity={clarity}");
    }

    [Fact]
    public void Detect_WhiteNoise_LowClarity()
    {
        Random rng = new(42);
        float[] s = new float[MicPipeline.WindowSize];
        for (int i = 0; i < s.Length; i++)
        {
            s[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;
        }

        (_, double clarity) = new YinDetector(s.Length, Rate).Detect(s);

        Assert.True(clarity < 0.9, $"noise clarity {clarity}");
    }

    [Fact]
    public void HzToMidi_RoundTripsWithDetector()
    {
        (double f, _) = new YinDetector(MicPipeline.WindowSize, Rate).Detect(Sine(440, MicPipeline.WindowSize));

        Assert.Equal(69, Math.Round(PitchMatching.HzToMidi(f)));
    }
}

public class SampleRingTests
{
    [Fact]
    public void ReadLatest_ReturnsMostRecentSamplesAcrossWrap()
    {
        SampleRing ring = new(8);
        ring.Write([1, 2, 3, 4, 5, 6]);
        ring.Write([7, 8, 9, 10]);

        float[] dst = new float[5];
        Assert.True(ring.ReadLatest(dst));
        Assert.Equal([6, 7, 8, 9, 10], dst);
    }

    [Fact]
    public void ReadLatest_FalseUntilEnoughData()
    {
        SampleRing ring = new(8);
        ring.Write([1, 2]);

        Assert.False(ring.ReadLatest(new float[4]));
    }

    [Fact]
    public void ReadFrom_StreamsInOrderAndSnapsForwardWhenBehind()
    {
        SampleRing ring = new(8);
        ring.Write([1, 2, 3, 4]);
        float[] dst = new float[4];

        Assert.Equal(4, ring.ReadFrom(0, dst));
        Assert.Equal([1, 2, 3, 4], dst);
        Assert.Equal(0, ring.ReadFrom(4, dst));

        ring.Write([5, 6, 7, 8, 9, 10, 11, 12, 13, 14]); // 10 more → oldest retained index is 6
        int n = ring.ReadFrom(0, dst);
        Assert.Equal(4, n);
        Assert.Equal([7, 8, 9, 10], dst);
    }

    [Fact]
    public void ReadFrom_NegativeStart_ReadsFromTheBeginningInsteadOfThrowing()
    {
        // Regression: the monitor asked for index -512 right after the mics opened → crash in the audio callback.
        SampleRing ring = new(8);
        ring.Write([1, 2, 3]);
        float[] dst = new float[4];

        int n = ring.ReadFrom(-512, dst);

        Assert.Equal(3, n);
        Assert.Equal([1, 2, 3], dst[..3]);
    }
}

public class MicPipelineTests
{
    private static float[] Interleave(float[] left, float[] right)
    {
        float[] o = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            o[2 * i] = left[i];
            o[2 * i + 1] = right[i];
        }

        return o;
    }

    private static float[] Tone(double hz, int n, double amp)
    {
        float[] s = new float[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / 48000));
        }

        return s;
    }

    [Fact]
    public void LeftAndRightPipelines_SeeOnlyTheirChannel()
    {
        int n = MicPipeline.WindowSize;
        float[] block = Interleave(Tone(220, n, 0.3), Tone(440, n, 0.3));
        MicPipeline left = new(1, MicChannelSide.Left, 48000) { Threshold = 0 };
        MicPipeline right = new(2, MicChannelSide.Right, 48000) { Threshold = 0 };
        float[] scratch = new float[n];

        left.Process(block, n, 2, scratch);
        right.Process(block, n, 2, scratch);
        for (int i = 0; i < 5; i++) // fill the median smoother
        {
            left.Analyze();
            right.Analyze();
        }

        Assert.Equal(57, left.Analyze().MidiNote);   // A3
        Assert.Equal(69, right.Analyze().MidiNote);  // A4
    }

    [Fact]
    public void Gate_BelowThreshold_ProducesSilenceAndNoPitch()
    {
        int n = MicPipeline.WindowSize;
        float[] block = Interleave(Tone(220, n, 0.005), Tone(220, n, 0.005));
        MicPipeline p = new(1, MicChannelSide.Left, 48000) { Threshold = 0.01 };

        p.Process(block, n, 2, new float[n]);
        for (int i = 0; i < 5; i++)
        {
            p.Analyze();
        }

        Assert.Equal(-1, p.Analyze().MidiNote);
        Assert.True(p.LevelRms > 0, "meter still shows pre-gate level");
    }

    [Fact]
    public void InputGain_LiftsQuietSignalAboveGate()
    {
        int n = MicPipeline.WindowSize;
        float[] block = Interleave(Tone(220, n, 0.005), Tone(220, n, 0.005));
        MicPipeline p = new(1, MicChannelSide.Left, 48000) { Threshold = 0.01, InputGain = 8 };

        p.Process(block, n, 2, new float[n]);
        for (int i = 0; i < 5; i++)
        {
            p.Analyze();
        }

        Assert.Equal(57, p.Analyze().MidiNote);
    }
}

public class PitchPathDelayTests
{
    /// <summary>
    /// How long the pitch analysis needs to report a new note — the part of the latency the old onset-based
    /// calibration missed. Silence, then A4 from a known sample; analysis at the game's 30 Hz; time until the
    /// smoothed note says A4. Calibration now detects the tone through this same path.
    /// </summary>
    [Fact]
    public void ToneOnset_IsReportedAfterTheAnalysisDelay()
    {
        const double rate = 48000;
        const int block = 480;               // 10 ms audio callbacks
        const int analyzeEvery = 1584;       // 33 ms at 48 kHz, like MicEngine
        MicPipeline p = new(1, UltrastarDJ.Core.Players.MicChannelSide.Mono, rate) { Threshold = 0 };
        float[] buf = new float[block * 2];
        float[] scratch = new float[block];
        long toneStart = (long)rate;         // 1 s of silence first
        long sample = 0, nextAnalyze = analyzeEvery;
        double? reportedAt = null;
        while (sample < 3 * rate && reportedAt is null)
        {
            for (int i = 0; i < block; i++)
            {
                long n = sample + i;
                float v = n < toneStart ? 0 : (float)(0.5 * Math.Sin(2 * Math.PI * 440 * n / rate));
                buf[2 * i] = buf[2 * i + 1] = v;
            }

            p.Process(buf, block, 2, scratch);
            sample += block;
            while (sample >= nextAnalyze)
            {
                p.Analyze();
                if (Math.Abs(p.LastMidiNote - 69) < 0.5)
                {
                    reportedAt = sample;
                }

                nextAnalyze += analyzeEvery;
            }
        }

        Assert.NotNull(reportedAt);
        double delayMs = (reportedAt!.Value - toneStart) / rate * 1000;
        // Window (≈ 43 ms) + up to one analysis period (33 ms) + the median of 5 needing 3 voiced results.
        Assert.InRange(delayMs, 40, 160); // measured: 60 ms at 48 kHz
    }
}
