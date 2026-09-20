namespace UltrastarDJ.Core.Songs;

/// <summary>File-system seam so the validator stays pure and testable.</summary>
public interface IFileExistence
{
    bool Exists(string path);
    /// <summary>Reads a text file, or null if unreadable.</summary>
    string? ReadText(string path);
}

public sealed record SongValidationError(string Field, string Message);

/// <summary><see cref="Song"/> is the patched copy — always load that one, not the original.</summary>
public sealed record SongValidationResult(bool IsValid, IReadOnlyList<SongValidationError> Errors, Song Song);

/// <summary>
/// Runs before preview / queue / load. Missing optional files are nulled out of the returned copy so the
/// media resolver's fallback chain applies; only a song with nothing left to play is rejected.
/// </summary>
public sealed class SongValidator(IFileExistence files)
{
    public SongValidationResult Validate(Song song)
    {
        List<SongValidationError> errors = [];
        Song patched = song;

        if (string.IsNullOrWhiteSpace(song.Title))
        {
            errors.Add(new("title", "Missing required tag: #TITLE"));
        }

        if (string.IsNullOrWhiteSpace(song.Artist))
        {
            errors.Add(new("artist", "Missing required tag: #ARTIST"));
        }

        if (song.Bpm <= 0 || !double.IsFinite(song.Bpm))
        {
            errors.Add(new("bpm", "Missing or invalid #BPM — notes cannot be timed"));
        }

        if (song.TxtPath is { } txt && files.ReadText(txt) is { } text && !HasNoteLine(text))
        {
            errors.Add(new("notes", "Song has no singable notes"));
        }

        if (song.AudioPath is { } audio && !IsRemote(audio) && !files.Exists(audio))
        {
            if (song.HasLocalVideo || song.HasYouTube)
            {
                patched = patched with { AudioPath = null };
            }
            else
            {
                errors.Add(new("audioPath", $"Audio file not found: {audio}"));
            }
        }

        if (song.VideoPath is { } video && !IsRemote(video) && !files.Exists(video))
        {
            if (!song.HasLocalAudio && !song.HasYouTube)
            {
                errors.Add(new("videoPath", $"Video file not found (sole audio source): {video}"));
            }
            else
            {
                patched = patched with { VideoPath = null };
            }
        }

        if (song.CoverPath is { } cover && !files.Exists(cover))
        {
            patched = patched with { CoverPath = null };
        }

        if (song.BackgroundPath is { } bg && !files.Exists(bg))
        {
            patched = patched with { BackgroundPath = null };
        }

        if (!patched.HasLocalAudio && !patched.HasLocalVideo && !patched.HasYouTube)
        {
            errors.Add(new("audio", "No playable audio source — all files are missing"));
        }

        return new SongValidationResult(errors.Count == 0, errors, patched);
    }

    private static bool HasNoteLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string t = line.TrimStart();
            if (t.Length > 1 && t[0] is ':' or '*' or 'F' or 'R' or 'G' && t[1] == ' ')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRemote(string p) => p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
