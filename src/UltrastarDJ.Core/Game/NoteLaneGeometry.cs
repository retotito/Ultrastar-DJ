using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Game;

/// <summary>Pure geometry for the beamer's note lane; UI multiplies the 0..1 values by pixels.</summary>
public static class NoteLaneGeometry
{
    public static int RowCount(int playersOnScreen) => playersOnScreen <= 2 ? 16 : 12;

    /// <summary>Average pitch of a phrase — the lane is centred on it.</summary>
    public static double AveragePitch(LyricLine line)
    {
        int n = 0;
        double sum = 0;
        foreach (Note note in line.Notes)
        {
            if (note.IsScorable)
            {
                sum += note.UsPitch;
                n++;
            }
        }

        return n == 0 ? 0 : sum / n;
    }

    /// <summary>
    /// Row (0 = top) for a pitch, wrapping by octaves until it fits the visible window around <paramref name="avg"/>.
    /// Ported unchanged from the prototype / tuneperfect.
    /// </summary>
    public static int PitchToRow(int usPitch, double avg, int rowCount)
    {
        int p = usPitch;
        int min = (int)Math.Floor(avg - rowCount / 2.0);
        int max = min + rowCount - 1;
        while (p > max)
        {
            p -= 12;
        }

        while (p < min)
        {
            p += 12;
        }

        double offset = p - avg;
        return Math.Abs((int)Math.Ceiling(rowCount / 2.0 + offset) - rowCount) - 1;
    }

    /// <summary>Horizontal extent of a note within its phrase, both 0..1.</summary>
    public static (double X, double Width) NoteSpan(Note note, LyricLine line)
    {
        double phraseStart = line.FirstNoteBeat;
        double phraseBeats = Math.Max(1, line.EndBeat - phraseStart);
        return ((note.StartBeat - phraseStart) / phraseBeats, note.LengthBeats / phraseBeats);
    }

    /// <summary>
    /// The phrase to show at <paramref name="beat"/>: the one containing it, else the next upcoming one, else the last.
    /// <paramref name="extendBeats"/> keeps a phrase active a little past its end so late mic data still lands in it.
    /// </summary>
    public static LyricLine? ActiveLine(NoteTrack track, double beat, double extendBeats = 0)
    {
        IReadOnlyList<LyricLine> lines = track.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            LyricLine l = lines[i];
            double gapToNext = i + 1 < lines.Count ? lines[i + 1].FirstNoteBeat - l.EndBeat : double.MaxValue;
            double end = l.EndBeat + Math.Min(extendBeats, gapToNext / 2);
            if (beat < end)
            {
                return l;
            }
        }

        return lines.Count > 0 ? lines[^1] : null;
    }
}
