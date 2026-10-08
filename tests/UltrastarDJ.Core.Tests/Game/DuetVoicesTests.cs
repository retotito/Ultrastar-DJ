using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Game;

public class DuetVoicesTests
{
    [Fact]
    public void Solo_EveryoneSingsTheOneVoice() => Assert.Equal([0, 0, 0], DuetVoices.Assign(3, tracks: 1));

    [Fact]
    public void Duet_TwoSingers_OneVoiceEach() => Assert.Equal([0, 1], DuetVoices.Assign(2, tracks: 2));

    [Fact]
    public void Duet_ByOrderOfTheSingers_NotByPlayerNumber()
        // Players 1 and 3 sing (no player 2): the second singer still gets voice 2 — by number both had voice 1
        // and nobody sang voice 2.
        => Assert.Equal([0, 1], DuetVoices.Assign(2, tracks: 2));

    [Fact]
    public void Duet_ThreeAndFour_Alternate() => Assert.Equal([0, 1, 0, 1], DuetVoices.Assign(4, tracks: 2));

    [Fact]
    public void Duet_OneSinger_IsMarkedForBothVoices() => Assert.Equal([DuetVoices.BothVoices], DuetVoices.Assign(1, tracks: 2));

    private static LyricLine Line(int start, int length, string text) => new(start, [new Note(NoteType.Normal, start, length, 0, text)]);

    [Fact]
    public void Merge_TakesBothVoicesInTimeOrder()
    {
        NoteTrack v1 = new(0, [Line(0, 8, "him"), Line(100, 8, "him again")]);
        NoteTrack v2 = new(1, [Line(40, 8, "her")]);

        NoteTrack merged = DuetVoices.Merge([v1, v2]);

        Assert.Equal(["him", "her", "him again"], merged.Lines.Select(l => l.Notes[0].Syllable));
    }

    [Fact]
    public void Merge_BothSingAtOnce_VoiceOneCounts()
    {
        // The refrain sung together: one singer cannot sing both lines — they sing voice 1's.
        NoteTrack v1 = new(0, [Line(0, 20, "together (him)")]);
        NoteTrack v2 = new(1, [Line(4, 20, "together (her)"), Line(60, 8, "her alone")]);

        NoteTrack merged = DuetVoices.Merge([v1, v2]);

        Assert.Equal(["together (him)", "her alone"], merged.Lines.Select(l => l.Notes[0].Syllable));
    }
}
