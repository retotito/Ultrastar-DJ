namespace UltrastarDJ.Core.Songs;

/// <summary>
/// Problems with a song's file, for the Details popup: what <see cref="SongValidator"/> rejects (the song won't
/// play) plus things that play but are wrong (missing pictures, overlapping notes, a #VIDEOGAP without video …).
/// </summary>
public static class SongCheck
{
    // Enough to locate the overlaps in an editor; the rest are counted.
    private const int MaxOverlapsListed = 3;

    public static IReadOnlyList<string> Problems(Song song, IReadOnlyList<NoteTrack>? tracks, IFileExistence files)
    {
        List<string> problems = [.. new SongValidator(files).Validate(song).Errors.Select(e => e.Message)];

        void Missing(string what, string? path)
        {
            if (!string.IsNullOrEmpty(path) && !IsRemote(path) && !files.Exists(path) && !problems.Any(p => p.Contains(path, StringComparison.Ordinal)))
            {
                problems.Add($"{what} not found: {Path.GetFileName(path)}");
            }
        }

        Missing("Audio file", song.AudioPath);
        Missing("Video file", song.VideoPath);
        Missing("Cover", song.CoverPath);
        Missing("Background", song.BackgroundPath);

        if (song.VideoGapSec is { } vg && vg != 0 && !song.HasLocalVideo && !song.HasYouTube)
        {
            problems.Add("#VIDEOGAP is set, but the song has no video");
        }

        if (string.IsNullOrWhiteSpace(song.Language))
        {
            problems.Add("No #LANGUAGE — the song cannot be found with the language filter");
        }

        if (tracks is null)
        {
            return problems;
        }

        List<int> overlaps = [];
        int zeroLength = 0;
        int first = int.MaxValue, last = int.MinValue;
        foreach (NoteTrack track in tracks)
        {
            Note? prev = null;
            foreach (Note n in track.AllNotes)
            {
                if (n.LengthBeats <= 0)
                {
                    zeroLength++;
                }

                if (prev is not null && n.StartBeat < prev.EndBeat)
                {
                    overlaps.Add(n.StartBeat);
                }

                first = Math.Min(first, n.StartBeat);
                last = Math.Max(last, n.EndBeat);
                prev = n;
            }
        }

        if (overlaps.Count > 0)
        {
            string at = string.Join(", ", overlaps.Take(MaxOverlapsListed));
            string more = overlaps.Count > MaxOverlapsListed ? $" and {overlaps.Count - MaxOverlapsListed} more" : "";
            problems.Add($"Notes overlap at beat {at}{more}");
        }

        if (zeroLength > 0)
        {
            problems.Add(zeroLength == 1 ? "1 note has length 0" : $"{zeroLength} notes have length 0");
        }

        if (song.Bpm > 0 && first != int.MaxValue)
        {
            double firstSec = Timing.BeatMath.SecondsAt(first, song.Bpm, song.GapMs);
            double lastSec = Timing.BeatMath.SecondsAt(last, song.Bpm, song.GapMs);
            if (song.StartSec is { } start && start >= lastSec)
            {
                problems.Add("#START is after the last note — nothing is sung");
            }

            if (song.EndMs is { } end && end / 1000.0 <= firstSec)
            {
                problems.Add("#END is before the first note — nothing is sung");
            }
        }

        return problems;
    }

    private static bool IsRemote(string path) => path.Contains("://", StringComparison.Ordinal);
}
