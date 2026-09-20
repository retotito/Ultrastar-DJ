using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Game;

public class PlayerScorerTests
{
    // Line 1: normal 0–3 (pitch 0), golden 4–7 (pitch 2). Line 2: rap 10–11, freestyle 12–13.
    private static NoteTrack Track() => new(0,
    [
        new LyricLine(0, [new Note(NoteType.Normal, 0, 4, 0, "a"), new Note(NoteType.Golden, 4, 4, 2, "b")]),
        new LyricLine(10, [new Note(NoteType.Rap, 10, 2, 0, "c"), new Note(NoteType.Freestyle, 12, 2, 0, "d")]),
    ]);

    private static PlayerScorer Scorer(Difficulty d = Difficulty.Medium) => new(1, Track(), d);

    /// <summary>Evaluates <paramref name="midi"/> once per beat in [from, to).</summary>
    private static void Sing(PlayerScorer s, int from, int to, double midi)
    {
        for (int b = from; b < to; b++)
        {
            s.Evaluate(midi, b + 0.5);
        }
    }

    [Fact]
    public void MaxScore_CountsGoldenTwiceAndOneBonusPerScorableLine()
    {
        // 4×100 + 4×200 + bonus 1000 | 2×100 + bonus 1000 (freestyle = 0)
        Assert.Equal(2200 + 1200, Scorer().MaxScore);
    }

    [Fact]
    public void PerfectRun_ReachesMaxScore()
    {
        PlayerScorer s = Scorer();
        Sing(s, 0, 4, 60);      // C4 vs pitch 0
        Sing(s, 4, 8, 62);      // D4 vs pitch 2
        Sing(s, 10, 12, 50);    // rap: any sound
        Sing(s, 12, 14, -1);    // freestyle: silence fine

        Assert.Equal(s.MaxScore, s.Score);
    }

    [Fact]
    public void OctaveOff_StillScores()
    {
        PlayerScorer s = Scorer();
        Sing(s, 0, 4, 72);

        Assert.Equal(400, s.Score);
    }

    [Fact]
    public void WrongPitch_ScoresNothing_AndTickReportsWrappedRow()
    {
        PlayerScorer s = Scorer(Difficulty.Hard);

        PitchTick t = s.Evaluate(60 + 5, 0.5);

        Assert.False(t.Correct);
        Assert.Equal(0, s.Score);
        Assert.Equal(5, t.RowPitch);
        Assert.Equal(0, t.Beat);
        Assert.True(t.IsFirstInNote);
    }

    [Fact]
    public void Joker_ForgivesOneMissAfterACorrectBeat()
    {
        PlayerScorer s = Scorer(Difficulty.Hard);
        s.Evaluate(60, 0.5);          // correct → banks joker
        PitchTick miss1 = s.Evaluate(65, 1.5); // wrong → joker spent, counted correct
        PitchTick miss2 = s.Evaluate(65, 2.5); // wrong → genuine miss

        Assert.True(miss1.Correct);
        Assert.False(miss2.Correct);
        Assert.Equal(200, s.Score);
    }

    [Fact]
    public void Silence_ClearsJokerAndRecordsNothing()
    {
        PlayerScorer s = Scorer(Difficulty.Hard);
        s.Evaluate(60, 0.5);
        PitchTick silent = s.Evaluate(-1, 1.5);
        PitchTick miss = s.Evaluate(65, 2.5);

        Assert.Equal(-1, silent.Beat);
        Assert.False(miss.Correct);
        Assert.Equal(100, s.Score);
    }

    [Fact]
    public void LastTickPerBeatWins()
    {
        PlayerScorer s = Scorer(Difficulty.Hard);
        s.Evaluate(60, 0.2);
        s.Evaluate(60, 0.5);
        s.Evaluate(-1, 0.9);   // silence does not overwrite
        Assert.Equal(100, s.Score);

        PlayerScorer s2 = Scorer(Difficulty.Hard);
        s2.Evaluate(65, 0.2);  // wrong (no joker yet)
        s2.Evaluate(60, 0.8);  // corrected within the same beat
        Assert.Equal(100, s2.Score);
    }

    [Fact]
    public void BetweenNotes_IsNotScored()
    {
        PlayerScorer s = Scorer();
        PitchTick t = s.Evaluate(60, 8.5);

        Assert.Equal(-1, t.Beat);
        Assert.Equal(0, s.Score);
    }

    [Fact]
    public void LineBonus_OnlyWhenEveryScorableBeatIsCorrect()
    {
        PlayerScorer s = Scorer();
        Sing(s, 0, 4, 60);
        Sing(s, 4, 7, 62);
        Assert.Equal(400 + 600, s.Score);
        Assert.False(s.IsLinePerfect(Track().Lines[0]));

        s.Evaluate(62, 7.5);
        Assert.Equal(400 + 800 + 1000, s.Score);
        Assert.True(s.IsLinePerfect(Track().Lines[0]));
    }

    [Fact]
    public void Deterministic_SameInputSameScore()
    {
        int Run()
        {
            PlayerScorer s = Scorer();
            double[] seq = [60, 61, -1, 72, 62, 65, 62, 62, 50, 50];
            for (int i = 0; i < seq.Length; i++)
            {
                s.Evaluate(seq[i], i + 0.5);
            }

            return s.Score;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Reset_ClearsScoreAndJoker()
    {
        PlayerScorer s = Scorer(Difficulty.Hard);
        s.Evaluate(60, 0.5);
        s.Reset();

        Assert.Equal(0, s.Score);
        Assert.False(s.Evaluate(65, 1.5).Correct);
    }
}
