using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Game;

/// <summary>Static score rules (prototype-compatible; USDX-like).</summary>
public static class ScoreRules
{
    public const int LineBonusPoints = 1000;

    public static int PointsPerBeat(NoteType type) => type switch
    {
        NoteType.Normal or NoteType.Rap => 100,
        NoteType.Golden or NoteType.RapGolden => 200,
        _ => 0,
    };

    /// <summary>All scorable beats × their multiplier + one line bonus per line that has scorable notes.</summary>
    public static int MaxScore(NoteTrack track)
    {
        int max = 0;
        foreach (LyricLine line in track.Lines)
        {
            bool scorable = false;
            foreach (Note n in line.Notes)
            {
                if (!n.IsScorable)
                {
                    continue;
                }

                max += PointsPerBeat(n.Type) * n.LengthBeats;
                scorable = true;
            }

            if (scorable)
            {
                max += LineBonusPoints;
            }
        }

        return max;
    }
}

/// <summary>What the engine publishes per player ~20×/s. Beamers keep their own incremental fill state from it.</summary>
/// <param name="PlayerId">1-based player slot.</param>
/// <param name="Beat">Integer beat being evaluated (delay-adjusted), or -1 when not inside a note or silent.</param>
/// <param name="MidiNote">Raw smoothed MIDI note, -1 = silence.</param>
/// <param name="Correct">Beat counted as sung correctly (after joker).</param>
/// <param name="IsFirstInNote">First beat of a note — start a new fill segment.</param>
/// <param name="NoteType">Type of the note under evaluation.</param>
/// <param name="RowPitch">UltraStar pitch row to draw the sung fill on: the target when correct, the wrapped sung pitch otherwise.</param>
/// <param name="Score">Running score.</param>
/// <param name="MaxScore">Achievable maximum for this track.</param>
public sealed record PitchTick(
    int PlayerId,
    int Beat,
    double MidiNote,
    bool Correct,
    bool IsFirstInNote,
    NoteType NoteType,
    double RowPitch,
    int Score,
    int MaxScore);

/// <summary>
/// Scoring for one player and one track. Feed one <see cref="Evaluate"/> per tick; the engine records one
/// result per integer beat (last tick wins) and derives the running score from those. Deterministic:
/// the same sample sequence always yields the same score.
/// </summary>
public sealed class PlayerScorer
{
    private readonly record struct BeatResult(bool Correct);

    private readonly NoteTrack _track;
    private readonly double _tolerance;
    private readonly Dictionary<int, BeatResult> _beats = [];
    private readonly Dictionary<int, Note> _noteAtBeat = [];
    private bool _joker;

    public PlayerScorer(int playerId, NoteTrack track, Difficulty difficulty)
    {
        PlayerId = playerId;
        _track = track;
        _tolerance = difficulty.ToleranceSemitones();
        MaxScore = ScoreRules.MaxScore(track);
        foreach (Note n in track.AllNotes)
        {
            for (int b = n.StartBeat; b < n.EndBeat; b++)
            {
                _noteAtBeat[b] = n;
            }
        }
    }

    public int PlayerId { get; }
    public int MaxScore { get; }
    public int Score { get; private set; }

    /// <summary>Whether every scorable beat of <paramref name="line"/> has been sung correctly so far.</summary>
    public bool IsLinePerfect(LyricLine line)
    {
        bool any = false;
        foreach (Note n in line.Notes)
        {
            if (!n.IsScorable)
            {
                continue;
            }

            for (int b = n.StartBeat; b < n.EndBeat; b++)
            {
                any = true;
                if (!_beats.TryGetValue(b, out BeatResult r) || !r.Correct)
                {
                    return false;
                }
            }
        }

        return any;
    }

    /// <summary>Correct beats recorded inside <paramref name="note"/>.</summary>
    public int CorrectBeats(Note note)
    {
        int c = 0;
        for (int b = note.StartBeat; b < note.EndBeat; b++)
        {
            if (_beats.TryGetValue(b, out BeatResult r) && r.Correct)
            {
                c++;
            }
        }

        return c;
    }

    /// <param name="sungMidi">Smoothed MIDI note from the mic pipeline, -1 = silence.</param>
    /// <param name="evalBeat">Song beat the sample belongs to (already shifted back by the player's mic delay).</param>
    public PitchTick Evaluate(double sungMidi, double evalBeat)
    {
        int intBeat = (int)Math.Floor(evalBeat);
        Note? note = _noteAtBeat.GetValueOrDefault(intBeat);
        bool silent = sungMidi < 0;

        if (note is null)
        {
            // Between notes: nothing to score, joker survives (vibrato dips are within notes anyway).
            return new PitchTick(PlayerId, -1, sungMidi, false, false, NoteType.Normal, -1, Score, MaxScore);
        }

        bool correct;
        double rowPitch;
        if (note.IsRap)
        {
            correct = !silent;
            rowPitch = note.UsPitch;
        }
        else if (note.Type == NoteType.Freestyle)
        {
            correct = true;
            rowPitch = note.UsPitch;
        }
        else
        {
            correct = PitchMatching.Matches(sungMidi, note.UsPitch, _tolerance);
            // Joker (TunePerfect): a correct beat banks one forgiven miss; silence clears it.
            if (silent)
            {
                _joker = false;
            }
            else if (correct)
            {
                _joker = true;
            }
            else if (_joker)
            {
                _joker = false;
                correct = true;
            }

            rowPitch = correct ? note.UsPitch : silent ? -1 : PitchMatching.WrapToTargetOctave(sungMidi, note.UsPitch);
        }

        if (!silent && note.IsScorable)
        {
            _beats[intBeat] = new BeatResult(correct);
            Score = ComputeScore();
        }

        return new PitchTick(PlayerId, silent ? -1 : intBeat, sungMidi, correct && !silent,
            !silent && intBeat == note.StartBeat, note.Type, rowPitch, Score, MaxScore);
    }

    private int ComputeScore()
    {
        int score = 0;
        foreach (LyricLine line in _track.Lines)
        {
            bool any = false;
            bool all = true;
            foreach (Note n in line.Notes)
            {
                if (!n.IsScorable)
                {
                    continue;
                }

                int pts = ScoreRules.PointsPerBeat(n.Type);
                for (int b = n.StartBeat; b < n.EndBeat; b++)
                {
                    any = true;
                    if (_beats.TryGetValue(b, out BeatResult r) && r.Correct)
                    {
                        score += pts;
                    }
                    else
                    {
                        all = false;
                    }
                }
            }

            if (any && all)
            {
                score += ScoreRules.LineBonusPoints;
            }
        }

        return score;
    }

    public void Reset()
    {
        _beats.Clear();
        _joker = false;
        Score = 0;
    }
}
