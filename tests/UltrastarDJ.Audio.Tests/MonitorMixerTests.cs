using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Tests;

public class MonitorMixerTests
{
    /// <summary>Backend that records the output callback so the test can pull frames by hand.</summary>
    private sealed class FakeBackend(double outRate = 44100) : IAudioBackend
    {
        public AudioOutputCallback? Output;
        public IReadOnlyList<AudioDeviceInfo> Devices { get; } = [new("out", "out", 0, 2, outRate, false, true)];
        public bool RefreshDevices() => true;
        public AudioDeviceInfo? Find(string id) => Devices.FirstOrDefault(d => d.Id == id);
        public IAudioLine OpenInput(string deviceId, int channels, double? rate, AudioInputCallback cb) => throw new NotSupportedException();
        public IAudioLine OpenOutput(string deviceId, int channelOffset, double? rate, AudioOutputCallback cb)
        {
            Output = cb;
            return new Line();
        }

        public void Dispose() { }

        private sealed class Line : IAudioLine
        {
            public bool IsActive => true;
            public double SampleRateHz => 44100;
            public int Channels => 2;
            public void Dispose() { }
        }
    }

    private static void Feed(MicPipeline p, double hz, double amp, int frames)
    {
        float[] block = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            block[2 * i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / p.SampleRateHz));
        }

        p.Process(block, frames, 2, new float[frames]);
    }

    [Fact]
    public void Mixer_ResamplesMicIntoOutput_WithGain()
    {
        FakeBackend backend = new();
        MicPipeline mic = new(1, MicChannelSide.Left, 48000) { Threshold = 0 };
        Feed(mic, 440, 0.5, 48000); // one second of tone at the mic rate

        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 0, [mic]);
        mixer.SetGain(1, 0.5);

        float[] outBuf = new float[512 * 2];
        backend.Output!(outBuf, 512, 2);

        double rms = Math.Sqrt(outBuf.Select(v => (double)v * v).Average());
        Assert.InRange(rms, 0.12, 0.22);              // 0.5 amp × 0.5 gain → sine RMS ≈ 0.177
        Assert.Equal(outBuf[0], outBuf[1]);            // stereo pair gets the same signal
    }

    [Fact]
    public void Mixer_StartedBeforeEnoughMicData_OutputsSilenceThenTheMic()
    {
        // Regression (song replay): the monitor opened 2 ms after the mics, before its 512-sample lead existed.
        FakeBackend backend = new();
        MicPipeline mic = new(1, MicChannelSide.Left, 48000) { Threshold = 0 };
        Feed(mic, 440, 0.5, 100);

        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 0, [mic]);

        float[] outBuf = new float[256 * 2];
        backend.Output!(outBuf, 256, 2);
        Assert.All(outBuf, v => Assert.Equal(0, v));

        Feed(mic, 440, 0.5, 4800);
        float[] later = new float[256 * 2];
        backend.Output!(later, 256, 2);
        Assert.Contains(later, v => v != 0);
    }

    /// <summary>
    /// Runs mic and output callbacks interleaved in time, as the two devices do: the mic delivers
    /// <paramref name="micBlock"/> frames at <paramref name="micRate"/>, the output pulls <paramref name="outBlock"/>
    /// at <paramref name="outRate"/> (nominal rates equal; a real clock differs slightly). Returns the output samples
    /// from second 2 on (after start-up), left channel.
    /// </summary>
    private static List<float> RunLive(double micRate, double outRate, int micBlock, int outBlock, double seconds)
    {
        // Both devices say 48 kHz (what the mixer plans with); micRate / outRate are their real clocks.
        FakeBackend backend = new(48000);
        MicPipeline mic = new(1, MicChannelSide.Left, 48000) { Threshold = 0 };
        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 0, [mic]);

        List<float> heard = [];
        float[] inBuf = new float[micBlock * 2];
        float[] outBuf = new float[outBlock * 2];
        float[] scratch = new float[micBlock];
        long micFrames = 0;
        double tIn = 0, tOut = 0;
        while (Math.Min(tIn, tOut) < seconds)
        {
            if (tIn <= tOut)
            {
                // A block is delivered once it has been recorded.
                for (int i = 0; i < micBlock; i++)
                {
                    inBuf[2 * i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * (micFrames + i) / 48000.0));
                }

                micFrames += micBlock;
                mic.Process(inBuf, micBlock, 2, scratch);
                tIn += micBlock / micRate;
            }
            else
            {
                Array.Clear(outBuf);
                backend.Output!(outBuf, outBlock, 2);
                if (tOut >= 2)
                {
                    for (int i = 0; i < outBlock; i++)
                    {
                        heard.Add(outBuf[2 * i]);
                    }
                }

                tOut += outBlock / outRate;
            }
        }

        return heard;
    }

    // A 440 Hz tone at 0.5 never has 8 samples in a row at exactly 0: such a run is a dropout (the reader
    // overtook the writer and played silence) — heard as a metallic / robotic voice.
    private static int Dropouts(List<float> s)
    {
        int count = 0, run = 0;
        foreach (float v in s)
        {
            run = v == 0 ? run + 1 : 0;
            if (run == 8)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Like <see cref="RunLive"/>, but as a real Mac does it: the mic in 64-frame blocks, the output asking for varying
    /// amounts (cycling through <paramref name="outBlocks"/>), and now and then an output callback late (a GC pause or a
    /// busy system) followed by the next one right away. Returns the left channel from second 2 on.
    /// </summary>
    private static List<float> RunJittery(int[] outBlocks, double seconds, int lateEvery = 0, double lateSec = 0, int micPauseEvery = 0, double micPauseSec = 0)
    {
        FakeBackend backend = new(48000);
        MicPipeline mic = new(1, MicChannelSide.Left, 48000) { Threshold = 0 };
        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 0, [mic]);

        const int micBlock = 64;
        List<float> heard = [];
        float[] inBuf = new float[micBlock * 2];
        float[] scratch = new float[micBlock];
        long micFrames = 0;
        double tIn = 0, tOut = 0;
        int calls = 0;
        double micPausedUntil = 0;
        while (Math.Min(tIn, tOut) < seconds)
        {
            // A GC pause: the mic's callback is held up; afterwards its blocks arrive in one burst.
            if (tIn <= tOut && tOut >= micPausedUntil)
            {
                for (int i = 0; i < micBlock; i++)
                {
                    inBuf[2 * i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * (micFrames + i) / 48000.0));
                }

                micFrames += micBlock;
                mic.Process(inBuf, micBlock, 2, scratch);
                tIn += micBlock / 48000.0;
            }
            else
            {
                int block = outBlocks[calls % outBlocks.Length];
                float[] outBuf = new float[block * 2];
                backend.Output!(outBuf, block, 2);
                if (tOut >= 2)
                {
                    for (int i = 0; i < block; i++)
                    {
                        heard.Add(outBuf[2 * i]);
                    }
                }

                calls++;
                if (micPauseEvery > 0 && calls % micPauseEvery == 0)
                {
                    micPausedUntil = tOut + micPauseSec;
                }

                // A late callback: the output's time moves on as usual, but this call happened late — the mic kept
                // writing meanwhile (modelled by letting the mic run ahead of the output clock for a moment).
                tOut += block / 48000.0;
                if (lateEvery > 0 && calls % lateEvery == 0)
                {
                    tIn -= lateSec;
                }
            }
        }

        return heard;
    }

    // A 440 Hz tone at 0.5 moves at most ~0.03 between two samples: a bigger step is a jump in the stream — a click.
    private static int Clicks(List<float> s)
    {
        int count = 0;
        for (int i = 1; i < s.Count; i++)
        {
            if (Math.Abs(s[i] - s[i - 1]) > 0.1)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void Mixer_OutputAsksForVaryingAmounts_AtMostOneClickWhileItLearns()
        // Found live: 43 jumps ("ran dry") in a two-minute song with 64-frame mic blocks — each one a click while singing.
        // The first unusually large block may still catch it out; after that the lead covers it.
        => Assert.InRange(Clicks(RunJittery([512, 512, 1024, 256, 512, 2048, 512], 30)), 0, 1);

    [Fact]
    public void Mixer_MicHeldUpByGcPauses_ClicksOnlyWhileItLearns()
        // Found live: one jump per gen1 GC (6/6, 28/33, 43/53). A pause holds the mic's callback up, the output asks
        // first, the mixer runs dry and jumps. It must learn the pause and keep enough lead: at most the first few.
        => Assert.InRange(Clicks(RunJittery([512], 60, micPauseEvery: 300, micPauseSec: 0.015)), 0, 3);

    [Fact]
    public void Mixer_LateCallbacks_NoClicks()
        => Assert.Equal(0, Clicks(RunJittery([512], 30, lateEvery: 200, lateSec: 0.02)));

    [Theory]
    [InlineData(512, 256)]    // USB mics often deliver 512+ frame blocks: the old fixed 512-sample lead was used up
    [InlineData(1024, 256)]
    [InlineData(256, 512)]
    public void Mixer_LiveBlocks_NoDropouts(int micBlock, int outBlock)
        => Assert.Equal(0, Dropouts(RunLive(48000, 48000, micBlock, outBlock, 10)));

    [Theory]
    [InlineData(47990)]       // mic clock 0.02 % slow: the reader slowly catches up with the writer
    [InlineData(48010)]       // mic clock fast: the reader falls behind until the ring overflows
    public void Mixer_ClockDrift_NoDropouts(double micRate)
        => Assert.Equal(0, Dropouts(RunLive(micRate, 48000, 256, 256, 120)));

    [Fact]
    public void Mixer_Muted_OutputsSilence()
    {
        FakeBackend backend = new();
        MicPipeline mic = new(1, MicChannelSide.Left, 44100) { Threshold = 0 };
        Feed(mic, 440, 0.5, 44100);

        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 0, [mic]);
        mixer.SetMuted(1, true);

        float[] outBuf = new float[256 * 2];
        backend.Output!(outBuf, 256, 2);

        Assert.All(outBuf, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Mixer_ChannelOffset_WritesOnlyThatPair()
    {
        FakeBackend backend = new();
        MicPipeline mic = new(1, MicChannelSide.Left, 44100) { Threshold = 0 };
        Feed(mic, 440, 0.5, 44100);

        using MonitorMixer mixer = new(backend, NullLogger<MonitorMixer>.Instance);
        mixer.Start("out", 2, [mic]);

        float[] outBuf = new float[128 * 4];
        backend.Output!(outBuf, 128, 4);

        Assert.All(Enumerable.Range(0, 128), i => { Assert.Equal(0, outBuf[i * 4]); Assert.Equal(0, outBuf[i * 4 + 1]); });
        Assert.Contains(Enumerable.Range(0, 128), i => outBuf[i * 4 + 2] != 0);
    }
}
