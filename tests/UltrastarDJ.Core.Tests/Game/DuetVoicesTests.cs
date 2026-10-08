using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Game;

public class DuetVoicesTests
{
    // Players set up to sing (mic, on an open display), in player order.
    private static IReadOnlyList<(int PlayerId, int Voice)> Sing(int tracks, DuetChoice? chosen, params int[] players)
        => DuetVoices.Singers(players, tracks, chosen);

    [Fact]
    public void Solo_EveryoneSingsTheOneVoice() => Assert.Equal([(1, 0), (2, 0), (3, 0)], Sing(1, null, 1, 2, 3));

    [Fact]
    public void Duet_OnlyOnePlayer_SingsBothVoices() => Assert.Equal([(1, DuetVoices.BothVoices)], Sing(2, null, 1));

    [Fact]
    public void Duet_TwoPlayers_OneVoiceEach() => Assert.Equal([(1, 0), (2, 1)], Sing(2, null, 1, 2));

    [Fact]
    public void Duet_PlayersOneAndThree_TheSecondSingsVoiceTwo() => Assert.Equal([(1, 0), (3, 1)], Sing(2, null, 1, 3));

    [Fact]
    public void Duet_ThreeOrFourPlayers_TheFirstTwoSing_TheOthersSitOut()
    {
        Assert.Equal([(1, 0), (2, 1)], Sing(2, null, 1, 2, 3));
        Assert.Equal([(1, 0), (2, 1)], Sing(2, null, 1, 2, 3, 4));
    }

    [Fact]
    public void Duet_TheDjsChoice_Wins() => Assert.Equal([(4, 0), (2, 1)], Sing(2, new DuetChoice(4, 2), 1, 2, 3, 4));

    [Fact]
    public void Duet_ChoiceNoLongerPossible_FallsBackToTheFirstTwo()
    {
        // Player 4 was chosen, but has no mic / display any more.
        Assert.Equal([(1, 0), (2, 1)], Sing(2, new DuetChoice(4, 2), 1, 2, 3));
        // The same player twice is not a choice.
        Assert.Equal([(1, 0), (2, 1)], Sing(2, new DuetChoice(2, 2), 1, 2));
    }

    [Fact]
    public void NobodySetUp_NobodySings() => Assert.Empty(Sing(2, null));

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
