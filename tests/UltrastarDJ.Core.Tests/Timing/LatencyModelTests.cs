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
    public void InputDelay_CalibratedTotalMinusTheOutputLatencyOfTheCalibrationDevice()
    {
        // Measured beep → note: 400 ms on a device whose latency is 250 ms → 150 ms are mic + analysis.
        Assert.Equal(150, LatencyModel.InputDelayMs(calibratedTotalMs: 400, latencyOfCalibrationOutputMs: 250), 6);
    }

    [Fact]
    public void InputDelay_Uncalibrated_UsesTheDefault()
    {
        Assert.Equal(LatencyModel.DefaultInputDelayMs, LatencyModel.InputDelayMs(calibratedTotalMs: null, latencyOfCalibrationOutputMs: 0), 6);
    }

    [Fact]
    public void InputDelay_NeverNegative()
    {
        // Latency set larger than what the mic measured (wrong setting): clamp instead of scoring in the future.
        Assert.Equal(0, LatencyModel.InputDelayMs(100, 300), 6);
    }

    [Fact]
    public void Scoring_IsIndependentOfWhenTheLatencyWasSet()
    {
        // Calibrated with latency 0 (not set yet) on a 250 ms speaker: total 400 ms measured.
        // Later the DJ sets the true 250 ms. Scoring time = display − input delay must not change.
        double clock = 20.0;
        double before = LatencyModel.DisplaySec(clock, 0) - LatencyModel.InputDelayMs(400, 0) / 1000;
        double after = LatencyModel.DisplaySec(clock, 250) - LatencyModel.InputDelayMs(400, 250) / 1000;

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
