using UltrastarDJ.Core.Songs;
using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Core.Tests.Timing;

public class SongTimelineTests
{
    // #BPM 75 (UltraStar beats are quarter beats: 60 / (75 × 4) = 0.2 s per beat). Last note ends at beat 500 → 100 s + gap.
    private static Song S(double gapMs = 0, double? startSec = null, double? endMs = null)
        => new() { Id = "x", SourceId = "a", Title = "T", Artist = "A", Bpm = 75, GapMs = gapMs, StartSec = startSec, EndMs = endMs };

    private const int LastBeat = 500;
    private const double Tail = 4;

    [Fact]
    public void Plain_EndsTailAfterLastNote()
    {
        SongTimeline t = SongTimeline.For(S(), LastBeat, Tail, mediaLengthSec: null);

        Assert.Equal(0, t.StartSec, 3);
        Assert.Equal(104, t.EndSec, 3);
    }

    [Fact]
    public void Gap_ShiftsTheLastNote()
    {
        Assert.Equal(116, SongTimeline.For(S(gapMs: 12000), LastBeat, Tail, null).EndSec, 3);
    }

    [Fact]
    public void Start_ElapsedCountsFromStartNotFromZero()
    {
        SongTimeline t = SongTimeline.For(S(startSec: 30), LastBeat, Tail, null);

        Assert.Equal(0, t.Elapsed(30), 3);
        Assert.Equal(10, t.Elapsed(40), 3);
        Assert.Equal(74, t.LengthSec, 3);
    }

    [Fact]
    public void End_BeforeLastNote_Wins()
    {
        Assert.Equal(80, SongTimeline.For(S(endMs: 80000), LastBeat, Tail, null).EndSec, 3);
    }

    [Fact]
    public void ShorterMedia_Wins()
    {
        // e.g. a YouTube-only song: video 160 s, #VIDEOGAP 70 s → 90 s of game time (the caller subtracts the gap).
        Assert.Equal(90, SongTimeline.For(S(), LastBeat, Tail, mediaLengthSec: 90).EndSec, 3);
    }

    [Fact]
    public void RemainingAndFraction()
    {
        SongTimeline t = SongTimeline.For(S(startSec: 4), LastBeat, Tail, null); // 4 … 104

        Assert.Equal(50, t.Remaining(54), 3);
        Assert.Equal(0.5, t.Fraction(54), 3);
        Assert.Equal(0, t.Fraction(0), 3);      // before the start: clamped
        Assert.Equal(1, t.Fraction(200), 3);    // after the end: clamped
        Assert.Equal(0, t.Remaining(200), 3);
    }

    [Fact]
    public void EndBeforeStart_IsEmptyNotNegative()
    {
        SongTimeline t = SongTimeline.For(S(startSec: 50, endMs: 20000), LastBeat, Tail, null);

        Assert.Equal(0, t.LengthSec, 3);
        Assert.Equal(0, t.Fraction(60), 3);
    }
}
