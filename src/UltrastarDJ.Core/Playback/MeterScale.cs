namespace UltrastarDJ.Core.Playback;

/// <summary>
/// The dB range a level meter shows, set per meter (a song sits near 0 dBFS, a mic far lower). Linear in dB:
/// <see cref="MinDb"/> is the left end, <see cref="MaxDb"/> the right; segments turn yellow from <see cref="WarnDb"/>
/// and red from <see cref="ClipDb"/>.
/// </summary>
public readonly record struct MeterScale(double MinDb, double MaxDb, double WarnDb, double ClipDb)
{
    /// <summary>Music after the fader: RMS of a loud master is around −10 dBFS.</summary>
    public static MeterScale Music { get; } = new(-40, 0, -10, -4);

    /// <summary>Linear amplitude (RMS, 1 = full scale) → 0..1 along the meter.</summary>
    public double Fraction(double amplitude) => amplitude <= 0 ? 0 : FractionOfDb(20 * Math.Log10(amplitude));

    /// <summary>dB → 0..1 along the meter, clamped.</summary>
    public double FractionOfDb(double db) => MaxDb <= MinDb ? 0 : Math.Clamp((db - MinDb) / (MaxDb - MinDb), 0, 1);

    public double WarnFraction => FractionOfDb(WarnDb);
    public double ClipFraction => FractionOfDb(ClipDb);
}

/// <summary>How a volume setting (0..1) scales the amplitude.</summary>
public static class VolumeCurve
{
    /// <summary>mpv's software volume is cubic: amplitude = (volume)³ — so 50 % is −18 dB, not −6 dB.</summary>
    public static double MpvAmplitude(double volume) => volume <= 0 ? 0 : volume * volume * volume;
}
