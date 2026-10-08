using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Game;

/// <summary>
/// Which voice (note track) each singer of a song sings, in the order the singers are listed. A duet alternates by
/// that order — 1st voice 1, 2nd voice 2, 3rd voice 1 … — not by player number: with players 1 and 3 both would have
/// had voice 1 and nobody voice 2. A single singer of a duet sings both voices (<see cref="BothVoices"/>).
/// </summary>
public static class DuetVoices
{
    /// <summary>The singer sings both voices, merged into one track.</summary>
    public const int BothVoices = -1;

    public static IReadOnlyList<int> Assign(int singers, int tracks)
    {
        if (tracks < 2)
        {
            return [.. Enumerable.Repeat(0, singers)];
        }

        return singers == 1 ? [BothVoices] : [.. Enumerable.Range(0, singers).Select(i => i % tracks)];
    }

    /// <summary>
    /// Both voices as one track for a single singer: every phrase in time order; where a phrase of voice 2 overlaps
    /// one of voice 1 (sung together), voice 1's is kept — one person cannot sing both lines.
    /// </summary>
    public static NoteTrack Merge(IReadOnlyList<NoteTrack> tracks)
    {
        List<LyricLine> first = [.. tracks[0].Lines];
        List<LyricLine> merged = [.. first];
        foreach (NoteTrack other in tracks.Skip(1))
        {
            merged.AddRange(other.Lines.Where(l => !first.Any(f => l.FirstNoteBeat < f.EndBeat && f.FirstNoteBeat < l.EndBeat)));
        }

        return new NoteTrack(0, [.. merged.OrderBy(l => l.FirstNoteBeat)]);
    }
}
