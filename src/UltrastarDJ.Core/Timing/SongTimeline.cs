using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Timing;

/// <summary>
/// The playing span of a song in game time (docs/02-media-engine.md "#VIDEOGAP": game time 0 = audio start, the
/// video gap is already taken out). Drives elapsed / remaining / progress on the beamers.
/// Start = <c>#START</c>; end = the earliest of <c>#END</c> and the media's own length — the outro after the last note
/// belongs to the song. Only with neither known: the last note + a tail. Times outside the span clamp.
/// </summary>
public sealed record SongTimeline(double StartSec, double EndSec)
{
    public double LengthSec => Math.Max(0, EndSec - StartSec);

    public double Elapsed(double gameTimeSec) => Math.Clamp(gameTimeSec - StartSec, 0, LengthSec);

    public double Remaining(double gameTimeSec) => Math.Clamp(EndSec - gameTimeSec, 0, LengthSec);

    public double Fraction(double gameTimeSec) => LengthSec > 0 ? Elapsed(gameTimeSec) / LengthSec : 0;

    /// <param name="song">Uses <c>#GAP</c>, <c>#BPM</c>, <c>#START</c> (seconds) and <c>#END</c> (ms).</param>
    /// <param name="lastBeat">Beat where the last note ends.</param>
    /// <param name="tailSec">How long to play on after the last note when the song gives no end at all.</param>
    /// <param name="mediaLengthSec">Media length in game time (file length minus <c>#VIDEOGAP</c> when the video is
    /// the audio); null when not known yet.</param>
    public static SongTimeline For(Song song, int lastBeat, double tailSec, double? mediaLengthSec)
    {
        double start = Math.Max(0, song.StartSec ?? 0);
        double end = double.PositiveInfinity;
        if (song.EndMs is { } endMs and > 0)
        {
            end = endMs / 1000.0;
        }

        if (mediaLengthSec is { } media and > 0)
        {
            end = Math.Min(end, media);
        }

        if (double.IsPositiveInfinity(end))
        {
            end = BeatMath.SecondsAt(lastBeat, song.Bpm, song.GapMs) + tailSec;
        }

        return new SongTimeline(start, Math.Max(start, end));
    }
}
