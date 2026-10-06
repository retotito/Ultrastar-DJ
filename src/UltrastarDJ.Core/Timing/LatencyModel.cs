namespace UltrastarDJ.Core.Timing;

/// <summary>
/// How audio latency enters the game (docs/03-game-engine.md "Latency and sync"). Two measured quantities:
/// <list type="bullet">
/// <item><b>Output latency</b> per output device: app plays → audience hears (cable ~15 ms, HDMI/TV 50–150 ms,
/// Bluetooth 150–300 ms). Set with Test sync. The beamer shows the song at <see cref="DisplaySec"/>.</item>
/// <item><b>Calibrated total</b> per player: a tone on the game output → its note recognised by the pitch analysis
/// (output + air + mic + USB + analysis). The player's input delay is that minus the latency the calibration device
/// had, so scoring = display − input delay is the same whichever was set first.</item>
/// </list>
/// </summary>
public static class LatencyModel
{
    /// <summary>Uncalibrated players: a typical USB mic plus the pitch analysis (USDX's default MicDelay is 140 ms too).</summary>
    public const double DefaultInputDelayMs = 140;

    /// <summary>Game time the audience is hearing now: the clock minus the output latency.</summary>
    public static double DisplaySec(double clockSec, double outputLatencyMs) => clockSec - outputLatencyMs / 1000.0;

    /// <summary>Mic + analysis delay of a player: calibrated total minus the calibration device's output latency.</summary>
    public static double InputDelayMs(double? calibratedTotalMs, double latencyOfCalibrationOutputMs)
        => calibratedTotalMs is { } total ? Math.Max(0, total - latencyOfCalibrationOutputMs) : DefaultInputDelayMs;

    /// <summary>Test sync: a click every this many seconds; the latency range (≤ 800 ms) fits in one period.</summary>
    public const double SyncPeriodSec = 1.0;

    /// <summary>Test sync: how long the beamer flash stays on.</summary>
    public const double SyncFlashSec = 0.1;

    /// <summary>
    /// Test sync: whether the beamer flash is on. Clicks leave the app at k · <see cref="SyncPeriodSec"/> after the
    /// first one; the audience hears each one <paramref name="outputLatencyMs"/> later, so that is when it flashes —
    /// the same shift <see cref="DisplaySec"/> gives the lyrics. Flash and click together ⇔ the latency is right.
    /// </summary>
    public static bool SyncFlashOn(double secSinceFirstClick, double outputLatencyMs)
    {
        double heard = secSinceFirstClick - outputLatencyMs / 1000.0;
        if (heard < 0)
        {
            return false;
        }

        return heard % SyncPeriodSec < SyncFlashSec;
    }
}
