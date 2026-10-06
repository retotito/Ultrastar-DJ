using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Core.Songs;

/// <summary>
/// What the Details popup tells the DJ about a song's notes: how long the singing lasts, solo or duet, how much is
/// golden / rap / freestyle, and the pitch range ("can I sing this?").
/// </summary>
public sealed record SongSummary(
    double SingingEndsSec,
    int Tracks,
    int Phrases,
    int Notes,
    double GoldenShare,
    int RapNotes,
    int FreestyleNotes,
    string? LowestNote,
    string? HighestNote)
{
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public bool IsDuet => Tracks > 1;

    /// <summary>UltraStar pitch (0 = C4) as a note name with octave: 0 → "C4", -3 → "A3", 13 → "C#5".</summary>
    public static string NoteName(int usPitch)
    {
        int midi = usPitch + 60;
        return Names[((midi % 12) + 12) % 12] + (int)Math.Floor(midi / 12.0 - 1);
    }

    public static SongSummary Of(Song song, IReadOnlyList<NoteTrack> tracks)
    {
        int phrases = 0, notes = 0, rap = 0, free = 0, goldenBeats = 0, scorableBeats = 0, lastBeat = int.MinValue;
        int? low = null, high = null;
        foreach (NoteTrack track in tracks)
        {
            foreach (LyricLine line in track.Lines)
            {
                if (line.Notes.Count > 0)
                {
                    phrases++;
                }

                foreach (Note n in line.Notes)
                {
                    notes++;
                    lastBeat = Math.Max(lastBeat, n.EndBeat);
                    if (n.IsRap)
                    {
                        rap++;
                    }

                    if (n.Type == NoteType.Freestyle)
                    {
                        free++;
                        continue;
                    }

                    scorableBeats += n.LengthBeats;
                    if (n.IsGolden)
                    {
                        goldenBeats += n.LengthBeats;
                    }

                    // Rap notes have no real pitch; they would widen the range for nothing.
                    if (!n.IsRap)
                    {
                        low = Math.Min(low ?? n.UsPitch, n.UsPitch);
                        high = Math.Max(high ?? n.UsPitch, n.UsPitch);
                    }
                }
            }
        }

        double ends = lastBeat == int.MinValue || song.Bpm <= 0 ? 0 : BeatMath.SecondsAt(lastBeat, song.Bpm, song.GapMs);
        return new SongSummary(ends, tracks.Count, phrases, notes, scorableBeats == 0 ? 0 : (double)goldenBeats / scorableBeats,
            rap, free, low is { } l ? NoteName(l) : null, high is { } h ? NoteName(h) : null);
    }
}
