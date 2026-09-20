namespace UltrastarDJ.Core.Songs;

/// <summary>UltraStar note kinds. The character is the first token of a note line.</summary>
public enum NoteType
{
    /// <summary><c>:</c></summary>
    Normal,
    /// <summary><c>*</c> — double points.</summary>
    Golden,
    /// <summary><c>F</c> — no pitch check, no points.</summary>
    Freestyle,
    /// <summary><c>R</c> — any sound scores.</summary>
    Rap,
    /// <summary><c>G</c> — rap with double points.</summary>
    RapGolden,
}

/// <summary>One syllable. Beats may be negative (notes before <c>#GAP</c>).</summary>
public sealed record Note(NoteType Type, int StartBeat, int LengthBeats, int UsPitch, string Syllable)
{
    public int EndBeat => StartBeat + LengthBeats;
    public bool IsRap => Type is NoteType.Rap or NoteType.RapGolden;
    public bool IsGolden => Type is NoteType.Golden or NoteType.RapGolden;
    public bool IsScorable => Type != NoteType.Freestyle;
}

/// <summary>A phrase (between two <c>-</c> line breaks). Rendered as one lyrics line / one note-lane page.</summary>
public sealed record LyricLine(int StartBeat, IReadOnlyList<Note> Notes)
{
    /// <summary>Use this, not <see cref="StartBeat"/>, as the lower bound: the first line's StartBeat is 0 regardless of its notes.</summary>
    public int FirstNoteBeat => Notes.Count > 0 ? Notes[0].StartBeat : StartBeat;
    public int EndBeat => Notes.Count > 0 ? Notes[^1].EndBeat : StartBeat;
}

/// <summary>All phrases of one voice. <see cref="Player"/> is 0 for P1, 1 for P2 (duets).</summary>
public sealed record NoteTrack(int Player, IReadOnlyList<LyricLine> Lines)
{
    public IEnumerable<Note> AllNotes => Lines.SelectMany(l => l.Notes);
}

/// <summary>
/// A song from any source, normalised. Paths are absolute. <see cref="Notes"/> is loaded on demand
/// (library scans only parse the header).
/// </summary>
public sealed record Song
{
    /// <summary>Stable id: <c>{SourceId}::{TxtPath}</c> for local songs, <c>usdb::{id}</c> for USDB.</summary>
    public required string Id { get; init; }
    public required string SourceId { get; init; }

    public required string Title { get; init; }
    public required string Artist { get; init; }
    /// <summary>UltraStar BPM as written in the file (quarter-beats; real beats/min = 4×).</summary>
    public required double Bpm { get; init; }
    /// <summary><c>#GAP</c> — audio time of beat 0, in milliseconds.</summary>
    public double GapMs { get; init; }
    public int? Year { get; init; }
    public string? Language { get; init; }
    public string? Genre { get; init; }
    public string? Edition { get; init; }
    public string? Creator { get; init; }
    public string? Comment { get; init; }

    public string? TxtPath { get; init; }
    public string? AudioPath { get; init; }
    public string? VideoPath { get; init; }
    public string? CoverPath { get; init; }
    public string? BackgroundPath { get; init; }
    public string? YouTubeId { get; init; }

    /// <summary><c>#VIDEOGAP</c> in seconds (may be fractional).</summary>
    public double? VideoGapSec { get; init; }
    /// <summary><c>#START</c> in seconds.</summary>
    public double? StartSec { get; init; }
    /// <summary><c>#END</c> in milliseconds (as in the file).</summary>
    public double? EndMs { get; init; }

    public IReadOnlyList<NoteTrack>? Notes { get; init; }

    public int? UsdbId { get; init; }
    public int? UsdbViews { get; init; }

    public bool HasLocalAudio => !string.IsNullOrEmpty(AudioPath);
    public bool HasLocalVideo => !string.IsNullOrEmpty(VideoPath);
    public bool HasYouTube => !string.IsNullOrEmpty(YouTubeId);
    /// <summary>YouTube is the only audio source → needs internet.</summary>
    public bool RequiresInternet => HasYouTube && !HasLocalAudio && !HasLocalVideo;
}
