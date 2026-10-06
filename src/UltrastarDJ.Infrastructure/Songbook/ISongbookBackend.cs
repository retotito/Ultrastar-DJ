using UltrastarDJ.Core.Songbook;

namespace UltrastarDJ.Infrastructure.Songbook;

/// <summary>What guests see of a song in "now playing" and the queue.</summary>
public sealed record SongbookSong(string Id, string Artist, string Title, int? Year, string? Language, string Source);

/// <summary>One of the guest's own requests, with its status in words ("Waiting", "In the queue #2", …).</summary>
public sealed record SongbookMine(string Id, string SongId, string Title, string Artist, string Status, bool CanCancel);

/// <summary>The outcome of a guest's request: refused with a reason, or accepted for the DJ to decide.</summary>
public sealed record SongbookRequestResult(bool Ok, bool UnknownSong, string? Message);

/// <summary>
/// Live state shown at the top of the guest page. <see cref="LibraryVersion"/> changes when the library does
/// (drive plugged in, source switched off); the phone then fetches the catalog again. <see cref="Mine"/> are this
/// phone's requests; <see cref="Taken"/> marks songs already requested or queued ("Requested", "In the queue #3").
/// </summary>
public sealed record SongbookState(SongbookSong? NowPlaying, IReadOnlyList<SongbookSong> Queue, int SongCount, int LibraryVersion,
    IReadOnlyList<SongbookMine> Mine, IReadOnlyDictionary<string, string> Taken);

/// <summary>
/// What the guest server needs from the app. Implemented by the App layer; every member may be called from
/// Kestrel worker threads and must marshal itself where needed.
/// </summary>
public interface ISongbookBackend
{
    /// <summary>Bumped whenever <see cref="Catalog"/> would change.</summary>
    int LibraryVersion { get; }

    /// <summary>The playable library for phones to search and filter themselves.</summary>
    SongbookCatalog Catalog();

    /// <param name="clientId">The phone's random id (its own requests), or null.</param>
    Task<SongbookState> StateAsync(string? clientId);

    Task<SongbookRequestResult> RequestAsync(string songId, string guestName, string clientId);

    /// <summary>Withdraws the phone's own request while the DJ has not decided.</summary>
    Task<bool> CancelAsync(string requestId, string clientId);

    /// <summary>YouTube id for a guest's preview; USDB songs fetch their text for it (cached). Null if there is none.</summary>
    Task<string?> YouTubeIdAsync(string songId, CancellationToken ct);
}
