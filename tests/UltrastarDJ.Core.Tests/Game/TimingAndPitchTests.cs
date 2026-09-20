using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Core.Tests.Game;

public class BeatMathTests
{
    [Fact]
    public void BpmIsQuadrupled()
    {
        // #BPM:120 → 480 beats/min → 125 ms per beat
        Assert.Equal(0.125, BeatMath.BeatLengthSec(120), 9);
        Assert.Equal(1.0, BeatMath.MsToBeats(120, 125), 9);
    }

    [Fact]
    public void BeatAt_IsNegativeBeforeGap()
    {
        Assert.True(BeatMath.BeatAt(gameTimeSec: 1.0, bpm: 120, gapMs: 2000) < 0);
        Assert.Equal(0, BeatMath.BeatAt(2.0, 120, 2000), 9);
    }

    [Fact]
    public void SecondsAt_RoundTripsBeatAt()
    {
        double sec = BeatMath.SecondsAt(37, 300.5, 16528);
        Assert.Equal(37, BeatMath.BeatAt(sec, 300.5, 16528), 6);
    }
}

public class PitchMatchingTests
{
    [Theory]
    [InlineData(60, 0, true)]     // C4 vs C4
    [InlineData(72, 0, true)]     // octave above still matches
    [InlineData(48, 0, true)]     // octave below
    [InlineData(61, 0, false)]    // semitone off on Hard
    [InlineData(-1, 0, false)]    // silence
    public void Matches_Hard_IsOctaveInvariantAndExact(double midi, int target, bool expected)
    {
        Assert.Equal(expected, PitchMatching.Matches(midi, target, Difficulty.Hard.ToleranceSemitones()));
    }

    [Fact]
    public void Matches_Easy_AllowsTwoSemitones()
    {
        Assert.True(PitchMatching.Matches(62, 0, Difficulty.Easy.ToleranceSemitones()));
        Assert.False(PitchMatching.Matches(63, 0, Difficulty.Easy.ToleranceSemitones()));
    }

    [Fact]
    public void OctaveDistance_FoldsToSixMax()
    {
        Assert.Equal(1, PitchMatching.OctaveDistance(60 + 11, 0));
        Assert.Equal(6, PitchMatching.OctaveDistance(60 + 6, 0));
    }

    [Fact]
    public void WrapToTargetOctave_LandsWithinSixSemitones()
    {
        double wrapped = PitchMatching.WrapToTargetOctave(60 + 26, 0);
        Assert.InRange(wrapped, -6, 6);
        Assert.Equal(2, wrapped);
    }

    [Fact]
    public void HzToMidi_A4Is69()
    {
        Assert.Equal(69, PitchMatching.HzToMidi(440), 9);
        Assert.Equal(60, PitchMatching.HzToMidi(261.6256), 3);
    }
}

public class PitchRingBufferTests
{
    [Fact]
    public void Median_RejectsSingleSpike()
    {
        PitchRingBuffer b = new(5);
        foreach (double v in (double[])[60, 60, 84, 60, 60])
        {
            b.Push(v);
        }

        Assert.Equal(60, b.Median());
    }

    [Fact]
    public void Median_IgnoresSilenceSamples()
    {
        PitchRingBuffer b = new(5);
        b.Push(-1);
        b.Push(62);
        b.Push(-1);

        Assert.Equal(62, b.Median());
    }

    [Fact]
    public void Median_EmptyOrAllSilent_IsMinusOne()
    {
        PitchRingBuffer b = new(3);
        Assert.Equal(-1, b.Median());
        b.Push(-1);
        Assert.Equal(-1, b.Median());
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        PitchRingBuffer b = new(3);
        b.Push(60);
        b.Reset();
        Assert.Equal(-1, b.Median());
    }
}

public class NoteLaneGeometryTests
{
    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    [InlineData(3, 12)]
    [InlineData(4, 12)]
    public void RowCount_ShrinksForThreeOrMorePlayers(int players, int rows)
        => Assert.Equal(rows, NoteLaneGeometry.RowCount(players));

    [Fact]
    public void PitchToRow_HigherPitchIsHigherOnScreen()
    {
        int low = NoteLaneGeometry.PitchToRow(0, 4, 16);
        int high = NoteLaneGeometry.PitchToRow(6, 4, 16);
        Assert.True(high < low, "row 0 is the top");
    }

    [Fact]
    public void PitchToRow_WrapsOctavesIntoWindow()
    {
        int inWindow = NoteLaneGeometry.PitchToRow(2, 0, 12);
        int octaveUp = NoteLaneGeometry.PitchToRow(14, 0, 12);
        Assert.Equal(inWindow, octaveUp);
        Assert.InRange(NoteLaneGeometry.PitchToRow(40, 0, 12), 0, 11);
    }

    [Fact]
    public void ActiveLine_PicksContainingThenUpcoming()
    {
        LyricLine l1 = new(0, [new Note(NoteType.Normal, 0, 4, 0, "a")]);
        LyricLine l2 = new(20, [new Note(NoteType.Normal, 20, 4, 0, "b")]);
        NoteTrack t = new(0, [l1, l2]);

        Assert.Same(l1, NoteLaneGeometry.ActiveLine(t, 2));
        Assert.Same(l2, NoteLaneGeometry.ActiveLine(t, 10));   // gap → next upcoming
        Assert.Same(l2, NoteLaneGeometry.ActiveLine(t, 99));   // past the end → last
    }

    [Fact]
    public void ActiveLine_ExtensionIsCappedAtHalfTheGap()
    {
        LyricLine l1 = new(0, [new Note(NoteType.Normal, 0, 4, 0, "a")]);
        LyricLine l2 = new(8, [new Note(NoteType.Normal, 8, 4, 0, "b")]);
        NoteTrack t = new(0, [l1, l2]);

        // gap = 4 beats → extension capped at 2 even though 10 requested
        Assert.Same(l1, NoteLaneGeometry.ActiveLine(t, 5.9, extendBeats: 10));
        Assert.Same(l2, NoteLaneGeometry.ActiveLine(t, 6.1, extendBeats: 10));
    }
}
