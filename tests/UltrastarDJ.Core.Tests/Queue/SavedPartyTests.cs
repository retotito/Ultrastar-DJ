using UltrastarDJ.Core.Queue;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Queue;

public class SavedPartyTests
{
    private static Song S(string id) => new() { Id = id, SourceId = "s", Title = id, Artist = "a", Bpm = 120 };

    private static readonly Dictionary<string, Song> Library = new[] { "a", "b", "c" }.ToDictionary(id => id, S);
    private static Song? Find(string id) => Library.GetValueOrDefault(id);
    private static readonly DateTime At = new(2026, 11, 22, 21, 0, 0);

    [Fact]
    public void Queue_ComesBackInOrder_WithTheGuestNames()
    {
        SavedParty saved = new([new("b", "Anna"), new("a", null), new("c", " ")], []);

        var (queue, _) = saved.Restore(Find);

        Assert.Equal([("b", "Anna"), ("a", null), ("c", null)], queue.Select(q => (q.Song.Id, q.Guest)));
    }

    [Fact]
    public void SongsGoneFromTheLibrary_AreSkipped()
    {
        SavedParty saved = new([new("gone", "Anna"), new("a", null)], [new("gone2", "Ben", At), new("b", "Ben", At)]);

        var (queue, requests) = saved.Restore(Find);

        Assert.Equal(["a"], queue.Select(q => q.Song.Id));
        Assert.Equal([("b", "Ben")], requests.Select(r => (r.Song.Id, r.Request.Guest)));
    }

    [Fact]
    public void Requests_AlreadyQueuedOrTwice_AreLeftOut()
    {
        SavedParty saved = new([new("a", null)], [new("a", "Anna", At), new("b", "Ben", At), new("b", "Cleo", At)]);

        var (_, requests) = saved.Restore(Find);

        Assert.Equal([("b", "Ben")], requests.Select(r => (r.Song.Id, r.Request.Guest)));
    }

    [Fact]
    public void AnOldOrDamagedFile_RestoresNothing()
    {
        // The settings store returns nulls for lists a file does not have.
        SavedParty saved = new(null!, null!);

        var (queue, requests) = saved.Restore(Find);

        Assert.Empty(queue);
        Assert.Empty(requests);
    }
}
