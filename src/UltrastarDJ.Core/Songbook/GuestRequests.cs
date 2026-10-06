namespace UltrastarDJ.Core.Songbook;

/// <summary>Where a guest's request stands, as their phone shows it.</summary>
public enum GuestRequestStatus
{
    /// <summary>The DJ has not decided yet; the guest may still cancel.</summary>
    Waiting,
    /// <summary>Accepted, in the queue after the current song.</summary>
    Queued,
    /// <summary>Loaded in the Game Player — the guest is up.</summary>
    OnStage,
    /// <summary>Was on stage and the queue moved on.</summary>
    Sung,
    /// <summary>The DJ dismissed it, or removed it from the queue before it was sung.</summary>
    Declined,
}

/// <summary>A song a guest asked for. <see cref="ClientId"/> is a random id their phone keeps, so "my requests" survive a renamed guest.</summary>
public sealed class GuestRequest(string id, string songId, string guest, string clientId, DateTime at)
{
    public string Id { get; } = id;
    public string SongId { get; } = songId;
    public string Guest { get; } = guest;
    public string ClientId { get; } = clientId;
    public DateTime At { get; } = at;
    public bool Accepted { get; internal set; }
    public bool Dismissed { get; internal set; }
    /// <summary>Its song has been the active (loaded) one at least once since it was accepted.</summary>
    public bool WasOnStage { get; internal set; }
}

/// <param name="Request">The new request, or null when refused.</param>
/// <param name="Refusal">Why not, in words for the guest.</param>
public sealed record RequestOutcome(GuestRequest? Request, string? Refusal);

/// <summary>
/// The songbook's requests for one party: no song requested twice or while already queued, at most
/// <see cref="MaxOpenPerGuest"/> open requests per guest, cancel while waiting, and the status of each request read
/// from the DJ's queue (song ids in order, the active one = loaded in the Game Player). UI thread only.
/// </summary>
public sealed class GuestRequests
{
    public const int MaxOpenPerGuest = 3;

    private readonly List<GuestRequest> _all = [];

    public IEnumerable<GuestRequest> Waiting => _all.Where(r => !r.Accepted && !r.Dismissed);

    public RequestOutcome Add(string songId, string guest, string clientId, DateTime now, IReadOnlyList<string> queue, int activeIndex)
    {
        int at = IndexAfter(queue, songId, activeIndex - 1);
        if (at == activeIndex && at >= 0)
        {
            return new(null, "This song is on stage right now.");
        }

        if (at > activeIndex)
        {
            return new(null, $"Already in the queue (#{at - activeIndex}).");
        }

        if (Waiting.FirstOrDefault(r => r.SongId == songId) is { } pending)
        {
            return new(null, pending.ClientId == clientId ? "You already requested this song." : $"Already requested by {pending.Guest}.");
        }

        int open = _all.Count(r => r.ClientId == clientId && StatusOf(r, queue, activeIndex, out _) is GuestRequestStatus.Waiting or GuestRequestStatus.Queued or GuestRequestStatus.OnStage);
        if (open >= MaxOpenPerGuest)
        {
            return new(null, $"You have {MaxOpenPerGuest} open requests — wait until one has been sung.");
        }

        GuestRequest request = new(Guid.NewGuid().ToString("N")[..12], songId, guest, clientId, now);
        _all.Add(request);
        return new(request, null);
    }

    /// <summary>Only the guest's own, and only while the DJ has not decided.</summary>
    public bool Cancel(string id, string clientId)
    {
        GuestRequest? r = _all.FirstOrDefault(x => x.Id == id && x.ClientId == clientId && !x.Accepted && !x.Dismissed);
        return r is not null && _all.Remove(r);
    }

    public GuestRequest? Accept(string id) => Decide(id, accept: true);
    public GuestRequest? Dismiss(string id) => Decide(id, accept: false);

    private GuestRequest? Decide(string id, bool accept)
    {
        GuestRequest? r = _all.FirstOrDefault(x => x.Id == id && !x.Accepted && !x.Dismissed);
        if (r is not null)
        {
            r.Accepted = accept;
            r.Dismissed = !accept;
        }

        return r;
    }

    /// <summary>Call after every queue change: remembers which accepted requests have been on stage (→ later "sung").</summary>
    public void Observe(IReadOnlyList<string> queue, int activeIndex)
    {
        string? active = activeIndex >= 0 && activeIndex < queue.Count ? queue[activeIndex] : null;
        foreach (GuestRequest r in _all.Where(r => r.Accepted && r.SongId == active))
        {
            r.WasOnStage = true;
        }
    }

    /// <param name="r">The request.</param>
    /// <param name="queue">Song ids in queue order.</param>
    /// <param name="activeIndex">The loaded song's index, -1 for none.</param>
    /// <param name="position">For <see cref="GuestRequestStatus.Queued"/>: 1 = next.</param>
    public static GuestRequestStatus StatusOf(GuestRequest r, IReadOnlyList<string> queue, int activeIndex, out int position)
    {
        position = 0;
        if (r.Dismissed)
        {
            return GuestRequestStatus.Declined;
        }

        if (!r.Accepted)
        {
            return GuestRequestStatus.Waiting;
        }

        int at = IndexAfter(queue, r.SongId, activeIndex - 1);
        if (at >= 0 && at == activeIndex)
        {
            return GuestRequestStatus.OnStage;
        }

        if (at > activeIndex)
        {
            position = at - activeIndex;
            return GuestRequestStatus.Queued;
        }

        return r.WasOnStage ? GuestRequestStatus.Sung : GuestRequestStatus.Declined;
    }

    public IReadOnlyList<GuestRequest> Of(string clientId) => [.. _all.Where(r => r.ClientId == clientId)];

    /// <summary>Who asked for a queued or on-stage song — shown in the DJ's queue so they can call the singer up.</summary>
    public string? RequesterOf(string songId, IReadOnlyList<string> queue, int activeIndex)
        => _all.LastOrDefault(r => r.SongId == songId && StatusOf(r, queue, activeIndex, out _) is GuestRequestStatus.Queued or GuestRequestStatus.OnStage)?.Guest;

    // First occurrence of the song after index `from` (queue entries before the active one are already sung).
    private static int IndexAfter(IReadOnlyList<string> queue, string songId, int from)
    {
        for (int i = Math.Max(0, from + 1); i < queue.Count; i++)
        {
            if (queue[i] == songId)
            {
                return i;
            }
        }

        return -1;
    }
}
