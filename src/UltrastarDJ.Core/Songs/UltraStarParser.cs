using System.Globalization;

namespace UltrastarDJ.Core.Songs;

/// <summary>Raw header tags of a <c>.txt</c>, before path resolution.</summary>
public sealed record SongHeader
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public double? Bpm { get; init; }
    public double? GapMs { get; init; }
    public string? Mp3 { get; init; }
    public string? Cover { get; init; }
    public string? Background { get; init; }
    public string? Video { get; init; }
    public string? YouTubeId { get; init; }
    public double? VideoGapSec { get; init; }
    public double? StartSec { get; init; }
    public double? EndMs { get; init; }
    public int? Year { get; init; }
    public string? Language { get; init; }
    public string? Genre { get; init; }
    public string? Edition { get; init; }
    public string? Creator { get; init; }
    public string? Comment { get; init; }
    /// <summary><c>#RELATIVE:yes</c> — legacy relative beat numbering. Detected, not supported.</summary>
    public bool Relative { get; init; }
}

/// <summary>
/// UltraStar <c>.txt</c> parser. Header and notes are separate passes: library scans need the header only,
/// notes are parsed when a song is loaded.
/// </summary>
public static class UltraStarParser
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".webm", ".mov", ".mpg", ".mpeg", ".avi", ".mkv", ".wmv", ".flv" };

    public static SongHeader ParseHeader(string text)
    {
        SongHeader h = new();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('#'))
            {
                break; // header ends at the first non-tag line
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            string key = line[1..colon].Trim().ToUpperInvariant();
            string value = line[(colon + 1)..].Trim();
            h = key switch
            {
                "TITLE" => h with { Title = value },
                "ARTIST" => h with { Artist = value },
                "BPM" => h with { Bpm = Num(value) },
                "GAP" => h with { GapMs = Num(value) },
                "MP3" or "AUDIO" => h with { Mp3 = value },
                "COVER" => h with { Cover = value },
                "BACKGROUND" => h with { Background = value },
                // #VIDEO may be a local file or a YouTube URL/id.
                "VIDEO" => Songs.YouTubeId.TryExtract(value) is { } yt ? h with { YouTubeId = yt } : h with { Video = value },
                "YOUTUBE" => h with { YouTubeId = Songs.YouTubeId.TryExtract(value) ?? value },
                "VIDEOGAP" => h with { VideoGapSec = Num(value) },
                "START" => h with { StartSec = Num(value) },
                "END" => h with { EndMs = Num(value) },
                "YEAR" => h with { Year = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ? y : null },
                "LANGUAGE" => h with { Language = value },
                "GENRE" => h with { Genre = value },
                "EDITION" => h with { Edition = value },
                "CREATOR" or "AUTHOR" => h with { Creator = value },
                "COMMENT" => h with { Comment = value },
                "RELATIVE" => h with { Relative = value.Equals("yes", StringComparison.OrdinalIgnoreCase) },
                _ => h,
            };
        }

        return h;
    }

    /// <summary>
    /// Parses the note body into tracks. Syllable text keeps leading/trailing spaces — UltraStar encodes
    /// word boundaries with them.
    /// </summary>
    public static IReadOnlyList<NoteTrack> ParseNotes(string text)
    {
        List<NoteTrack> tracks = [];
        List<LyricLine> lines = [];
        List<Note> notes = [];
        int player = 0;
        int lineStart = 0;

        void FlushLine()
        {
            if (notes.Count > 0)
            {
                lines.Add(new LyricLine(lineStart, notes));
                notes = [];
            }
        }

        void FlushTrack()
        {
            FlushLine();
            if (lines.Count > 0)
            {
                tracks.Add(new NoteTrack(player, lines));
                lines = [];
            }
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line is "P1" or "P 1")
            {
                FlushTrack();
                player = 0;
                continue;
            }

            if (line is "P2" or "P 2")
            {
                FlushTrack();
                player = 1;
                continue;
            }

            if (line == "E")
            {
                break;
            }

            NoteType? type = line[0] switch
            {
                ':' => NoteType.Normal,
                '*' => NoteType.Golden,
                'F' => NoteType.Freestyle,
                'R' => NoteType.Rap,
                'G' => NoteType.RapGolden,
                _ => null,
            };

            if (type is { } t)
            {
                // Tokenise by hand: the first four fields may be separated by runs of spaces/tabs, but the
                // syllable starts right after ONE separator and keeps any further leading/trailing spaces.
                if (TryParseNoteLine(raw.TrimEnd('\r'), out int start, out int length, out int pitch, out string syllable))
                {
                    notes.Add(new Note(t, start, length, pitch, syllable));
                }

                continue;
            }

            if (line[0] == '-')
            {
                string[] parts = line[1..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                FlushLine();
                if (parts.Length > 0 && TryInt(parts[0], out int next))
                {
                    lineStart = next;
                }
            }
        }

        FlushTrack();
        return tracks;
    }

    /// <summary>Header → <see cref="Song"/> with sibling paths resolved. Null when title or artist is missing.</summary>
    public static Song? ParseSong(string txtPath, string sourceId, string text)
    {
        SongHeader h = ParseHeader(text);
        if (string.IsNullOrWhiteSpace(h.Title) || string.IsNullOrWhiteSpace(h.Artist))
        {
            return null;
        }

        string dir = Path.GetDirectoryName(txtPath) ?? "";
        string? Sibling(string? file) => string.IsNullOrWhiteSpace(file) ? null : Path.Combine(dir, file);
        bool IsVideoFile(string f) => VideoExtensions.Contains(Path.GetExtension(f));

        return new Song
        {
            Id = $"{sourceId}::{txtPath}",
            SourceId = sourceId,
            Title = h.Title,
            Artist = h.Artist,
            Bpm = h.Bpm ?? 120,
            GapMs = h.GapMs ?? 0,
            Year = h.Year,
            Language = h.Language,
            Genre = h.Genre,
            Edition = h.Edition,
            Creator = h.Creator,
            Comment = h.Comment,
            TxtPath = txtPath,
            AudioPath = Sibling(h.Mp3),
            CoverPath = Sibling(h.Cover),
            BackgroundPath = Sibling(h.Background),
            VideoPath = h.Video is { } v && IsVideoFile(v) ? Sibling(v) : null,
            YouTubeId = h.YouTubeId,
            VideoGapSec = h.VideoGapSec,
            StartSec = h.StartSec,
            EndMs = h.EndMs,
        };
    }

    private static bool TryParseNoteLine(string line, out int start, out int length, out int pitch, out string syllable)
    {
        start = length = pitch = 0;
        syllable = "";
        int i = 0;
        static bool IsSep(char c) => c is ' ' or '\t';

        while (i < line.Length && IsSep(line[i]))
        {
            i++;
        }

        i++; // type character
        Span<int> nums = stackalloc int[3];
        for (int n = 0; n < 3; n++)
        {
            while (i < line.Length && IsSep(line[i]))
            {
                i++;
            }

            int startIdx = i;
            while (i < line.Length && !IsSep(line[i]))
            {
                i++;
            }
            if (startIdx == i || !TryInt(line[startIdx..i], out nums[n]))
            {
                return false;
            }
        }

        if (i < line.Length && IsSep(line[i]))
        {
            i++;
        }

        start = nums[0];
        length = nums[1];
        pitch = nums[2];
        syllable = i < line.Length ? line[i..] : "";
        return true;
    }

    /// <summary>Tolerates both "," and "." decimal separators (files come from every locale).</summary>
    private static double? Num(string value)
        => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : null;

    private static bool TryInt(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
}
