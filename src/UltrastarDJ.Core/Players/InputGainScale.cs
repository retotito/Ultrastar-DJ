namespace UltrastarDJ.Core.Players;

/// <summary>
/// The mic gain knob works in dB (stored as a linear factor in <see cref="PlayerConfig.InputGain"/>). USB singing
/// mics differ by 20–30 dB (SingStar / Let's Sing need about 0.1×, others several ×), so a linear 0–10× knob left
/// the useful range in its first few percent.
/// </summary>
public static class InputGainScale
{
    public const double MinDb = -40;   // 0.01×
    public const double MaxDb = 20;    // 10×

    public static double ToDb(double gain) => gain <= 0 ? MinDb : Math.Clamp(20 * Math.Log10(gain), MinDb, MaxDb);

    public static double ToGain(double db) => Math.Pow(10, Math.Clamp(db, MinDb, MaxDb) / 20);

    /// <summary>Half-dB steps.</summary>
    public static double Snap(double db) => Math.Round(db * 2, MidpointRounding.AwayFromZero) / 2;
}
