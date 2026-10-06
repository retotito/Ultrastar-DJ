using UltrastarDJ.Core.Songbook;

namespace UltrastarDJ.Core.Tests.Songbook;

public class GuestRequestsTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Request_WaitsUntilTheDjDecides()
    {
        GuestRequests g = new();
        GuestRequest r = g.Add("song1", "Reto", "phoneA", Now, [], -1).Request!;

        Assert.Equal(GuestRequestStatus.Waiting, GuestRequests.StatusOf(r, [], -1, out _));
        Assert.Single(g.Waiting);
    }

    [Fact]
    public void SameSongTwice_IsRefused()
    {
        GuestRequests g = new();
        g.Add("song1", "Anna", "phoneB", Now, [], -1);

        Assert.Equal("Already requested by Anna.", g.Add("song1", "Reto", "phoneA", Now, [], -1).Refusal);
        Assert.Equal("You already requested this song.", g.Add("song1", "Anna", "phoneB", Now, [], -1).Refusal);
    }

    [Fact]
    public void SongAlreadyQueuedOrOnStage_IsRefused()
    {
        GuestRequests g = new();
        string[] queue = ["a", "b", "song1"];

        Assert.Equal("Already in the queue (#2).", g.Add("song1", "Reto", "phoneA", Now, queue, 0).Refusal);
        Assert.Equal("This song is on stage right now.", g.Add("a", "Reto", "phoneA", Now, queue, 0).Refusal);
        Assert.Null(g.Add("a", "Reto", "phoneA", Now, queue, 1).Refusal);   // already sung: may come again
    }

    [Fact]
    public void AtMostThreeOpenRequestsPerGuest()
    {
        GuestRequests g = new();
        foreach (string s in new[] { "1", "2", "3" })
        {
            g.Add(s, "Reto", "phoneA", Now, [], -1);
        }

        Assert.StartsWith("You have 3 open requests", g.Add("4", "Reto", "phoneA", Now, [], -1).Refusal);
        Assert.Null(g.Add("4", "Anna", "phoneB", Now, [], -1).Refusal);       // per guest, not per party
    }

    [Fact]
    public void Cancel_OnlyOwnAndOnlyWhileWaiting()
    {
        GuestRequests g = new();
        GuestRequest r = g.Add("song1", "Reto", "phoneA", Now, [], -1).Request!;

        Assert.False(g.Cancel(r.Id, "phoneB"));
        g.Accept(r.Id);
        Assert.False(g.Cancel(r.Id, "phoneA"));

        GuestRequest r2 = g.Add("song2", "Reto", "phoneA", Now, [], -1).Request!;
        Assert.True(g.Cancel(r2.Id, "phoneA"));
        Assert.Empty(g.Waiting);
    }

    [Fact]
    public void Accepted_FollowsTheQueue_QueuedOnStageSung()
    {
        GuestRequests g = new();
        GuestRequest r = g.Add("song1", "Reto", "phoneA", Now, [], -1).Request!;
        g.Accept(r.Id);

        Assert.Equal(GuestRequestStatus.Queued, GuestRequests.StatusOf(r, ["a", "b", "song1"], 0, out int pos));
        Assert.Equal(2, pos);

        g.Observe(["a", "b", "song1"], 2);
        Assert.Equal(GuestRequestStatus.OnStage, GuestRequests.StatusOf(r, ["a", "b", "song1"], 2, out _));
        Assert.Equal("Reto", g.RequesterOf("song1", ["a", "b", "song1"], 2));

        Assert.Equal(GuestRequestStatus.Sung, GuestRequests.StatusOf(r, ["a", "b", "song1", "c"], 3, out _));
        Assert.Null(g.RequesterOf("song1", ["a", "b", "song1", "c"], 3));
    }

    [Fact]
    public void Dismissed_OrRemovedBeforeSinging_IsDeclined()
    {
        GuestRequests g = new();
        GuestRequest dismissed = g.Add("s1", "Reto", "phoneA", Now, [], -1).Request!;
        GuestRequest removed = g.Add("s2", "Reto", "phoneA", Now, [], -1).Request!;
        g.Dismiss(dismissed.Id);
        g.Accept(removed.Id);

        Assert.Equal(GuestRequestStatus.Declined, GuestRequests.StatusOf(dismissed, [], -1, out _));
        Assert.Equal(GuestRequestStatus.Declined, GuestRequests.StatusOf(removed, ["x"], 0, out _));   // DJ took it out of the queue
    }
}
