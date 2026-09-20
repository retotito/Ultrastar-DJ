namespace UltrastarDJ.Core.Timing;

/// <summary>
/// UltraStar timing. <c>#BPM</c> is written as quarter-beats: real beats per minute are 4× the tag value.
/// All methods take the tag value (<c>song.Bpm</c>) and do the ×4 internally.
/// </summary>
public static class BeatMath
{
    public static double BeatLengthSec(double bpm) => 60.0 / (bpm * 4);

    /// <summary>Beat position at a game time. Negative before <c>#GAP</c> — never clamp it.</summary>
    public static double BeatAt(double gameTimeSec, double bpm, double gapMs)
        => (gameTimeSec - gapMs / 1000.0) / BeatLengthSec(bpm);

    public static double SecondsAt(double beat, double bpm, double gapMs)
        => beat * BeatLengthSec(bpm) + gapMs / 1000.0;

    /// <summary>Milliseconds → beats, used to shift the scoring window by the mic delay.</summary>
    public static double MsToBeats(double bpm, double ms) => (ms / 1000.0) * (bpm / 60.0) * 4;

    public static double BeatsToMs(double bpm, double beats) => beats * BeatLengthSec(bpm) * 1000.0;
}
