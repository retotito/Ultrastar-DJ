namespace UltrastarDJ.Core.Players;

/// <summary>
/// How loud a player sings, for the beamer's little mic meter: 0 at or below the player's noise gate (room noise
/// and the speakers don't move it), 1 at <see cref="RangeDb"/> above it. Logarithmic like a mixer meter.
/// </summary>
public static class MicActivity
{
    public const double RangeDb = 30;

    public static double Level(double rms, double gate)
    {
        if (rms <= 0 || rms <= gate)
        {
            return 0;
        }

        double db = 20 * Math.Log10(rms / Math.Max(gate, 1e-6));
        return Math.Clamp(db / RangeDb, 0, 1);
    }
}
