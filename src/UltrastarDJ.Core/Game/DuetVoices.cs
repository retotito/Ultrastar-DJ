using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Game;

/// <summary>The DJ's pick for a duet: which player sings voice 1 and which voice 2.</summary>
public sealed record DuetChoice(int Voice1PlayerId, int Voice2PlayerId);

/// <summary>
/// Who sings a song, and which voice (note track). Solo: everyone set up, voice 1. Duet: exactly two — the DJ's
/// choice, else the first two set up (players 1 and 3 → 1 sings voice 1, 3 voice 2) — and the others sit out. A
/// single player sings both voices merged (<see cref="BothVoices"/>).
/// </summary>
public static class DuetVoices
{
    /// <summary>The singer sings both voices, merged into one track.</summary>
    public const int BothVoices = -1;

    /// <param name="available">Players set up to sing (mic, on an open display), in player order.</param>
    /// <param name="tracks">Voices in the song (2 = duet).</param>
    /// <param name="chosen">The DJ's pick; ignored when one of them is no longer set up, or both are the same player.</param>
    public static IReadOnlyList<(int PlayerId, int Voice)> Singers(IReadOnlyList<int> available, int tracks, DuetChoice? chosen)
    {
        if (tracks < 2)
        {
            return [.. available.Select(id => (id, 0))];
        }

        if (available.Count == 1)
        {
            return [(available[0], BothVoices)];
        }

        if (chosen is { } c && c.Voice1PlayerId != c.Voice2PlayerId && available.Contains(c.Voice1PlayerId) && available.Contains(c.Voice2PlayerId))
        {
            return [(c.Voice1PlayerId, 0), (c.Voice2PlayerId, 1)];
        }

        return [.. available.Take(2).Select((id, i) => (id, i))];
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
