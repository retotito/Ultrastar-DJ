namespace UltrastarDJ.Core.Game;

public enum Difficulty
{
    Easy,
    Medium,
    Hard,
}

public static class DifficultyExtensions
{
    /// <summary>Semitone tolerance for octave-invariant matching (matches TunePerfect / USDX).</summary>
    public static double ToleranceSemitones(this Difficulty d) => d switch
    {
        Difficulty.Easy => 2,
        Difficulty.Medium => 1,
        _ => 0.5, // with integer MIDI input this requires an exact semitone
    };
}
