using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongQueryTests
{
    private static readonly Dictionary<string, string> Labels = new() { ["a"] = "Party Folder", ["b"] = "Classics", ["usdb"] = "USDB" };

    private static Song S(string title, string artist, string source, int? year = null, string? language = null, string? genre = null, int? views = null)
        => new() { Id = $"{source}::{title}", SourceId = source, Title = title, Artist = artist, Bpm = 300, Year = year, Language = language, Genre = genre, UsdbViews = views };

    private static readonly Song[] Songs =
    [
        S("Roxanne", "The Police", "a", 1978, "English", "Rock"),
        S("Lovefool", "The Cardigans", "b", 1996, "English", "Pop"),
        S("Beautiful Day", "U2", "usdb", 2000, "English", "Pop", views: 2500),
        S("99 Luftballons", "Nena", "usdb", 1983, "German", "Pop", views: 600),
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
    public void Apply_LanguageAndGenre_MatchOneEntryOfAList()
    {
        Song[] songs =
        [
            S("Duo", "X", "a", language: "English, French", genre: "Pop, Rock"),
            S("Solo", "Y", "a", language: "French", genre: "Chanson"),
            S("Other", "Z", "a", language: "German, Suisse German", genre: "Rock"),
        ];

        Assert.Equal(["Duo", "Solo"], new SongQuery { Language = "french" }.Apply(songs, id => Labels[id]).Select(s => s.Title));
        Assert.Equal(["Duo", "Other"], new SongQuery { Genre = "Rock" }.Apply(songs, id => Labels[id]).Select(s => s.Title));
    }

    [Fact]
    public void Apply_SortByGenreAndBpm()
    {
        Song[] songs = [S("B", "X", "a", genre: "Rock") with { Bpm = 120 }, S("A", "Y", "a", genre: "Pop") with { Bpm = 300 }, S("C", "Z", "a") with { Bpm = 90 }];

        Assert.Equal(["C", "A", "B"], new SongQuery { SortBy = SongSort.Genre }.Apply(songs, id => Labels[id]).Select(s => s.Title));
        Assert.Equal(["A", "B", "C"], new SongQuery { SortBy = SongSort.Bpm, Descending = true }.Apply(songs, id => Labels[id]).Select(s => s.Title));
    }

    [Fact]
    public void Apply_SortByYear_MissingYearFirst()
    {
        Song[] songs = [S("New", "X", "a", 2010), S("None", "Y", "a"), S("Old", "Z", "a", 1970)];

        Assert.Equal(["None", "Old", "New"], new SongQuery { SortBy = SongSort.Year }.Apply(songs, id => Labels[id]).Select(s => s.Title));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(499, 1)]
    [InlineData(500, 2)]
    [InlineData(1000, 3)]
    [InlineData(2000, 4)]
    public void Stars_FromUsdbViews(int? views, int? stars)
    {
        Assert.Equal(stars, S("T", "A", "usdb", views: views).Stars);
    }

    [Fact]
    public void Apply_Stars_KeepsExactlyThatRatingAndDropsUnrated()
    {
        Assert.Equal(["99 Luftballons"], Titles(new SongQuery { Stars = 2 }));
        Assert.Equal(["Beautiful Day"], Titles(new SongQuery { Stars = 4 }));
        Assert.Empty(Titles(new SongQuery { Stars = 1 }));
    }

    [Fact]
    public void Apply_SortByRating_OrdersByViewsWithUnratedFirst()
    {
        Assert.Equal(["Lovefool", "Roxanne", "99 Luftballons", "Beautiful Day"], Titles(new SongQuery { SortBy = SongSort.Rating }));
        Assert.Equal(["Beautiful Day", "99 Luftballons", "Lovefool", "Roxanne"], Titles(new SongQuery { SortBy = SongSort.Rating, Descending = true }));
    }

    [Fact]
    public void Apply_FavouritesOnly_KeepsTheFavourites()
    {
        SongQuery q = new() { FavouritesOnly = true, FavouriteIds = new HashSet<string> { "a::Roxanne", "usdb::99 Luftballons" } };
        Assert.Equal(["99 Luftballons", "Roxanne"], Titles(q));
    }

    [Fact]
    public void Apply_Hidden_LeavesThoseOut()
    {
        // Broken songs with "Show broken songs" off.
        SongQuery q = new() { Hidden = new HashSet<string> { "b::Lovefool" } };
        Assert.DoesNotContain("Lovefool", Titles(q));
        Assert.Equal(3, Titles(q).Count);
    }

    [Fact]
    public void Apply_BrokenOnly_KeepsTheBrokenSongs()
    {
        SongQuery q = new() { BrokenOnly = true, BrokenIds = new HashSet<string> { "b::Lovefool" } };
        Assert.Equal(["Lovefool"], Titles(q));
    }
}
