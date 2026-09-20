namespace UltrastarDJ.Core.Game;

/// <summary>
/// Pitch scales: MIDI (A4 = 69, C4 = 60) is what detectors produce; UltraStar pitch has C4 = 0.
/// Matching is octave-invariant — a singer an octave off still scores.
/// </summary>
public static class PitchMatching
{
    public const double MidiC4 = 60;

    public static double HzToMidi(double hz) => 69 + 12 * Math.Log2(hz / 440.0);

    public static double MidiToUsPitch(double midi) => midi - MidiC4;

    /// <summary>Semitone distance folded into [0, 6] (ignores octave).</summary>
    public static double OctaveDistance(double sungMidi, int targetUsPitch)
    {
        double diff = Math.Abs(MidiToUsPitch(sungMidi) - targetUsPitch) % 12;
        return diff > 6 ? 12 - diff : diff;
    }

    public static bool Matches(double sungMidi, int targetUsPitch, double toleranceSemitones)
        => sungMidi >= 0 && OctaveDistance(sungMidi, targetUsPitch) <= toleranceSemitones;

    /// <summary>Shifts the sung pitch by whole octaves into ±6 semitones of the target (for drawing the wrong-row fill).</summary>
    public static double WrapToTargetOctave(double sungMidi, int targetUsPitch)
    {
        double sungUs = MidiToUsPitch(sungMidi);
        while (sungUs > targetUsPitch + 6)
        {
            sungUs -= 12;
        }

        while (sungUs < targetUsPitch - 6)
        {
            sungUs += 12;
        }

        return sungUs;
    }
}
