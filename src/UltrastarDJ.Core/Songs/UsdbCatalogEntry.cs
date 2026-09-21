namespace UltrastarDJ.Core.Songs;

/// <summary>One row of the USDB song list. Notes and YouTube id live in the song txt, fetched on load.</summary>
public sealed record UsdbCatalogEntry
{
    public required int SongId { get; init; }
    public required string Artist { get; init; }
    public required string Title { get; init; }
    public string? Genre { get; init; }
    public int? Year { get; init; }
    public string? Language { get; init; }
    public string? Creator { get; init; }
    public string? Edition { get; init; }
    public bool GoldenNotes { get; init; }
    public double Rating { get; init; }
    public int Views { get; init; }
    public string? CoverUrl { get; init; }
    /// <summary>USDB "lastchange" unix timestamp; drives incremental sync.</summary>
    public long UsdbMtime { get; init; }

    public const string SourceId = "usdb";

    /// <summary>Library view of this entry. <see cref="Song.Bpm"/> is unknown (0) until the txt has been fetched.</summary>
    public Song ToSong() => new()
    {
        Id = $"usdb::{SongId}",
        SourceId = SourceId,
        UsdbId = SongId,
        Title = Title,
        Artist = Artist,
        Bpm = 0,
        Year = Year,
        Language = Language,
        Genre = Genre,
        Edition = Edition,
        Creator = Creator,
        UsdbViews = Views,
    };
}
