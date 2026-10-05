using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongQueryTests
{
    private static readonly Dictionary<string, string> Labels = new() { ["a"] = "Party Folder", ["b"] = "Classics", ["usdb"] = "USDB" };

    private static Song S(string title, string artist, string source, int? year = null, string? language = null, string? genre = null)
        => new() { Id = $"{source}::{title}", SourceId = source, Title = title, Artist = artist, Bpm = 300, Year = year, Language = language, Genre = genre };

    private static readonly Song[] Songs =
    [
        S("Roxanne", "The Police", "a", 1978, "English", "Rock"),
        S("Lovefool", "The Cardigans", "b", 1996, "English", "Pop"),
        S("Beautiful Day", "U2", "usdb", 2000, "English", "Pop"),
        S("99 Luftballons", "Nena", "usdb", 1983, "German", "Pop"),
    ];

    private static List<string> Titles(SongQuery q) => q.Apply(Songs, id => Labels[id]).Select(s => s.Title).ToList();

    [Fact]
    public void Apply_NoFilter_ReturnsAllSortedByArtist()
    {
        Assert.Equal(["99 Luftballons", "Lovefool", "Roxanne", "Beautiful Day"], Titles(new SongQuery()));
    }

    [Fact]
    public void Apply_SourceIds_KeepsOnlyThoseSources()
    {
        Assert.Equal(["99 Luftballons", "Beautiful Day"], Titles(new SongQuery { SourceIds = new HashSet<string> { "usdb" } }));
    }

    [Fact]
    public void Apply_SeveralSourceIds_KeepsAllOfThem()
    {
        Assert.Equal(["Lovefool", "Roxanne"], Titles(new SongQuery { SourceIds = new HashSet<string> { "a", "b" } }));
    }

    [Fact]
    public void Apply_SortBySource_OrdersByLabelThenArtist()
    {
        // Classics (b) < Party Folder (a) < USDB; within USDB: Nena before U2.
        Assert.Equal(["Lovefool", "Roxanne", "99 Luftballons", "Beautiful Day"], Titles(new SongQuery { SortBy = SongSort.Source }));
    }

    [Fact]
    public void Apply_SortBySourceDescending_ReversesSourcesButKeepsArtistAscending()
    {
        Assert.Equal(["99 Luftballons", "Beautiful Day", "Roxanne", "Lovefool"], Titles(new SongQuery { SortBy = SongSort.Source, Descending = true }));
    }

    [Fact]
    public void Apply_SearchLanguageGenreAndSource_Combine()
    {
        SongQuery q = new() { Search = "a",Language = "english", Genre = "Pop", SourceIds = new HashSet<string> { "usdb", "b" } };

        Assert.Equal(["Lovefool", "Beautiful Day"], Titles(q));
    }

    [Fact]
    public void Apply_SortByYear_MissingYearFirst()
    {
        Song[] songs = [S("New", "X", "a", 2010), S("None", "Y", "a"), S("Old", "Z", "a", 1970)];

        Assert.Equal(["None", "Old", "New"], new SongQuery { SortBy = SongSort.Year }.Apply(songs, id => Labels[id]).Select(s => s.Title));
    }
}
