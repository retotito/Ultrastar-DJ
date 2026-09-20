using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Game;

public class GameSessionTests
{
    // BPM 120 (→ 480 beats/min, 125 ms/beat), GAP 1000 ms. One note per player track: beats 8–16.
    private static Song Song() => new()
    {
        Id = "s", SourceId = "s", Title = "t", Artist = "a", Bpm = 120, GapMs = 1000,
        Notes =
        [
            new NoteTrack(0, [new LyricLine(8, [new Note(NoteType.Normal, 8, 8, 0, "la")])]),
            new NoteTrack(1, [new LyricLine(8, [new Note(NoteType.Normal, 8, 8, 7, "lo")])]),
        ],
    };

    [Fact]
    public void BeatAt_UsesGapAndQuadrupledBpm()
    {
        GameSession s = new(Song(), [new GamePlayer(1, 0, 0)], Difficulty.Medium);

        Assert.Equal(0, s.BeatAt(1.0), 6);
        Assert.Equal(8, s.BeatAt(2.0), 6);   // 1 s after gap = 8 beats @ 125 ms
    }

    [Fact]
    public void Tick_ScoresEachPlayerOnItsOwnTrack()
    {
        GameSession s = new(Song(), [new GamePlayer(1, 0, 0), new GamePlayer(2, 1, 0)], Difficulty.Hard);

        // Beats 8..15 → game time 2.0 .. 3.0 s. P1 sings C (0), P2 sings G (7) — both correct.
        for (double t = 2.0; t < 3.0; t += 0.125)
        {
            s.Tick(t + 0.01, id => id == 1 ? 60 : 67);
        }

        Assert.Equal(8 * 100 + 1000, s.Scorer(1).Score);
        Assert.Equal(8 * 100 + 1000, s.Scorer(2).Score);
    }

    [Fact]
    public void Tick_MicDelay_ShiftsEvaluationBackInTime()
    {
        // 125 ms delay = exactly one beat. Singing at game time 3.0 (beat 16, after the note)
        // is evaluated at beat 15 — the last beat of the note — and still counts.
        GameSession delayed = new(Song(), [new GamePlayer(1, 0, MicDelayMs: 125)], Difficulty.Hard);
        GameSession plain = new(Song(), [new GamePlayer(1, 0, 0)], Difficulty.Hard);

        PitchTick d = delayed.Tick(3.01, _ => 60)[0];
        PitchTick p = plain.Tick(3.01, _ => 60)[0];

        Assert.Equal(15, d.Beat);
        Assert.Equal(-1, p.Beat);
    }

    [Fact]
    public void Standings_OrderedByScore()
    {
        GameSession s = new(Song(), [new GamePlayer(1, 0, 0), new GamePlayer(2, 1, 0)], Difficulty.Hard);
        for (double t = 2.0; t < 3.0; t += 0.125)
        {
            s.Tick(t + 0.01, id => id == 2 ? 67 : 65); // P1 wrong, P2 right
        }

        var standings = s.Standings();
        Assert.Equal(2, standings[0].PlayerId);
        Assert.True(standings[0].Score > standings[1].Score);
    }

    [Fact]
    public void TrackIndexBeyondRange_FallsBackToLastTrack()
    {
        GameSession s = new(Song(), [new GamePlayer(3, 5, 0)], Difficulty.Medium);

        Assert.Same(Song().Notes![1].Lines[0].Notes[0].Syllable, s.TrackOf(3).Lines[0].Notes[0].Syllable);
        Assert.Equal(16, s.LastBeat);
    }
}
