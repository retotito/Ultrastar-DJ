using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Core.Tests.Timing;

public class LatencyModelTests
{
    [Fact]
    public void DisplayTime_RunsBehindTheClockByTheOutputLatency()
    {
        // Bluetooth speaker: heard 250 ms after the app plays it → show lyrics 250 ms later.
        Assert.Equal(9.75, LatencyModel.DisplaySec(10.0, outputLatencyMs: 250), 6);
    }

    [Fact]
    public void MicDelay_CalibratedTotalMinusTheOutputLatency()
    {
        // Measured tone → note: 400 ms on a speaker whose latency is 250 ms → 150 ms are mic + analysis.
        Assert.Equal((150, true), LatencyModel.MicDelay(calibratedTotalMs: 400, outputLatencyMs: 250, OutputKind.Bluetooth));
    }

    [Fact]
    public void MicDelay_NeverNegative()
    {
        // Latency set larger than what the mic measured (wrong setting): clamp instead of scoring in the future.
        Assert.Equal((0, true), LatencyModel.MicDelay(100, 300, OutputKind.Other));
    }

    [Theory]
    [InlineData(OutputKind.Other, 0)]
    [InlineData(OutputKind.Bluetooth, 200)]
    [InlineData(OutputKind.Display, 80)]
    public void MicDelay_NotCalibrated_TypicalMicPlusTypicalSpeaker(OutputKind kind, double speakerMs)
    {
        // Nobody measured anything: the sung pitch reaches the scorer after the speaker and the mic path.
        Assert.Equal((LatencyModel.DefaultInputDelayMs + speakerMs, false), LatencyModel.MicDelay(null, 0, kind));
    }

    [Fact]
    public void MicDelay_NotCalibrated_ButSpeakerLatencySet_OnlyTheTypicalMic()
    {
        // Test sync set 250 ms: the display already runs 250 ms late, so only the mic path is left.
        Assert.Equal((LatencyModel.DefaultInputDelayMs, false), LatencyModel.MicDelay(null, 250, OutputKind.Bluetooth));
    }

    [Fact]
    public void Scoring_IsIndependentOfWhenTheLatencyWasSet()
    {
        // Calibrated with latency 0 (not set yet) on a 250 ms speaker: total 400 ms measured.
        // Later the DJ sets the true 250 ms. Scoring time = display − mic delay must not change.
        double clock = 20.0;
        double before = LatencyModel.DisplaySec(clock, 0) - LatencyModel.MicDelay(400, 0, OutputKind.Bluetooth).Ms / 1000;
        double after = LatencyModel.DisplaySec(clock, 250) - LatencyModel.MicDelay(400, 250, OutputKind.Bluetooth).Ms / 1000;

        Assert.Equal(before, after, 6);
    }

    [Theory]
    [InlineData(0.00, 0, true)]     // first click, no latency
    [InlineData(0.15, 0, false)]    // flash over
    [InlineData(1.05, 0, true)]     // next click
    [InlineData(0.10, 250, false)]  // clicks not heard yet
    [InlineData(0.30, 250, true)]   // heard 250 ms after it left the app
    [InlineData(2.27, 250, true)]
    [InlineData(2.40, 250, false)]
    public void SyncFlash_LightsWhenTheClickIsHeard(double secSinceFirstClick, double latencyMs, bool on)
    {
        Assert.Equal(on, LatencyModel.SyncFlashOn(secSinceFirstClick, latencyMs));
    }
}
