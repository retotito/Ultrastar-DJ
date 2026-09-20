using UltrastarDJ.Core.Queue;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Queue;

public class PlaylistTests
{
    private static Song S(string id) => new() { Id = id, SourceId = "s", Title = id, Artist = "a", Bpm = 120 };

    [Fact]
    public void AddRemove_MaintainsOrderAndRaisesChanged()
    {
        Playlist q = new();
        int changed = 0;
        q.Changed += () => changed++;

        q.Add(S("a"));
        q.Add(S("b"));
        q.Remove("a");

        Assert.Equal(["b"], q.Items.Select(s => s.Id));
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Move_SwapsNeighboursAndFollowsActive()
    {
        Playlist q = new();
        q.Add(S("a"));
        q.Add(S("b"));
        q.Add(S("c"));
        q.SetActive("b");

        q.MoveUp("b");
        Assert.Equal(["b", "a", "c"], q.Items.Select(s => s.Id));
        Assert.Equal(0, q.ActiveIndex);

        q.MoveUp("b");   // already first → no-op
        q.MoveDown("c"); // already last → no-op
        Assert.Equal(["b", "a", "c"], q.Items.Select(s => s.Id));
    }

    [Fact]
    public void RemovingBeforeActive_ShiftsActiveIndex()
    {
        Playlist q = new();
        q.Add(S("a"));
        q.Add(S("b"));
        q.SetActive("b");

        q.Remove("a");

        Assert.Equal(0, q.ActiveIndex);
        Assert.Equal("b", q.ActiveSong!.Id);
    }

    [Fact]
    public void Next_IsSongAfterActive()
    {
        Playlist q = new();
        q.Add(S("a"));
        q.Add(S("b"));

        Assert.Equal("a", q.Next!.Id);
        q.SetActive("a");
        Assert.Equal("b", q.Next!.Id);
        q.SetActive("b");
        Assert.Null(q.Next);
    }
}
