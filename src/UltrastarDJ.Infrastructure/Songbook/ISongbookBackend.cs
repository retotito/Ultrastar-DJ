namespace UltrastarDJ.Infrastructure.Songbook;

/// <summary>What guests see of a song.</summary>
public sealed record SongbookSong(string Id, string Artist, string Title, int? Year, string? Language, string Source);

/// <summary>Live state shown at the top of the guest page.</summary>
public sealed record SongbookState(SongbookSong? NowPlaying, IReadOnlyList<SongbookSong> Queue, int SongCount);

/// <summary>
/// What the guest server needs from the app. Implemented by the App layer; every member may be called from
/// Kestrel worker threads and must marshal itself where needed.
/// </summary>
public interface ISongbookBackend
{
    IReadOnlyList<SongbookSong> Search(string query, int limit);
    SongbookState State();
    /// <summary>False when the id is unknown (song removed since the guest searched).</summary>
    bool Request(string songId, string guestName);
}
