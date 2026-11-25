using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Queue;

/// <summary>A queued song as kept over a restart; <see cref="Guest"/> is who requested it on the songbook, if anyone.</summary>
public sealed record SavedQueueEntry(string SongId, string? Guest);

/// <summary>A guest's request the DJ had not decided yet when the app closed.</summary>
public sealed record SavedRequest(string SongId, string Guest, DateTime At);

/// <summary>
/// The queue and the waiting requests, kept so a restart or crash mid-party loses nothing. Not kept: the songbook's
/// side (which phone asked, accepted or dismissed requests) — the restored ones are no longer connected to a phone.
/// </summary>
public sealed record SavedParty(IReadOnlyList<SavedQueueEntry> Queue, IReadOnlyList<SavedRequest> Requests)
{
    public static SavedParty Empty { get; } = new([], []);

    /// <summary>
    /// The saved entries whose songs are still in the library, in their order. Songs gone completely (folder removed,
    /// USDB song deleted) are skipped; songs only unavailable right now (drive unplugged, USDB offline) are kept.
    /// </summary>
    public (IReadOnlyList<(Song Song, string? Guest)> Queue, IReadOnlyList<(Song Song, SavedRequest Request)> Requests) Restore(Func<string, Song?> find)
    {
        List<(Song, string?)> queue = [];
        foreach (SavedQueueEntry e in Queue ?? [])
        {
            if (find(e.SongId) is { } song)
            {
                queue.Add((song, string.IsNullOrWhiteSpace(e.Guest) ? null : e.Guest));
            }
        }

        List<(Song, SavedRequest)> requests = [];
        foreach (SavedRequest r in Requests ?? [])
        {
            // One wish per song, as on the songbook; a song already queued is no longer waiting.
            if (find(r.SongId) is { } song && !queue.Any(q => q.Item1.Id == song.Id) && !requests.Any(x => x.Item1.Id == song.Id))
            {
                requests.Add((song, r));
            }
        }

        return (queue, requests);
    }
}
