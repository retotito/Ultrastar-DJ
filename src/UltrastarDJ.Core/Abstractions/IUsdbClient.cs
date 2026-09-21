using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Abstractions;

/// <summary>Progress of a catalog sync: rows fetched so far (total is unknown — USDB pages until a short page).</summary>
public readonly record struct UsdbSyncProgress(int Fetched);

/// <summary>Thrown when USDB rejects or fails a request (network, not logged in, song deleted).</summary>
public sealed class UsdbException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>usdb.animux.de over HTTP. Stateful: <see cref="LoginAsync"/> establishes the session cookie.</summary>
public interface IUsdbClient
{
    bool IsLoggedIn { get; }

    /// <summary>False on wrong credentials; throws <see cref="UsdbException"/> on network failure.</summary>
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Forgets the session; the next request needs <see cref="LoginAsync"/> again.</summary>
    void Logout();

    /// <summary>Every song, oldest id first, one page (≤100 rows) at a time so callers can persist as they go. Slow (~270 pages).</summary>
    IAsyncEnumerable<IReadOnlyList<UsdbCatalogEntry>> FetchAllPagesAsync(CancellationToken ct = default);

    /// <summary>Songs changed after the watermark, newest first; stops at the first already-known row.</summary>
    Task<IReadOnlyList<UsdbCatalogEntry>> FetchUpdatedAsync(long lastMtime, IReadOnlySet<int> idsAtLastMtime, IProgress<UsdbSyncProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Raw UltraStar txt of a song.</summary>
    Task<string> GetSongTxtAsync(int songId, CancellationToken ct = default);
}

/// <summary>Local copy of the USDB song list.</summary>
public interface IUsdbCatalog
{
    int Count { get; }
    IReadOnlyList<UsdbCatalogEntry> GetAll();
    /// <summary>Insert or update; existing rows with the same id are replaced.</summary>
    void Upsert(IReadOnlyList<UsdbCatalogEntry> entries);
    void Clear();
    /// <summary>Newest <see cref="UsdbCatalogEntry.UsdbMtime"/> and the ids sharing it (0, empty when the catalog is empty).</summary>
    (long LastMtime, IReadOnlySet<int> Ids) Watermark();
}
