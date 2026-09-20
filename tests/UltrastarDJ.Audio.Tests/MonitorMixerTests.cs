using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Tests;

public class MonitorMixerTests
{
    /// <summary>Backend that records the output callback so the test can pull frames by hand.</summary>
    private sealed class FakeBackend : IAudioBackend
    {
        public AudioOutputCallback? Output;
        public IReadOnlyList<AudioDeviceInfo> Devices { get; } = [new("out", "out", 0, 2, 44100, false, true)];
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
