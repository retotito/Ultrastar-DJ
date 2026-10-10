using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongFacetsTests
{
    private static Song S(string title, string source, string? language, string? genre, int? views = null)
        => new() { Id = $"{source}::{title}", SourceId = source, Title = title, Artist = "A", Bpm = 300, Language = language, Genre = genre, UsdbViews = views };

    private static readonly Song[] Songs =
    [
        S("One", "a", "English, French", "Pop, Rock", 2500),
        S("Two", "a", "French", "Chanson"),
        S("Three", "b", "German", "Rock", 600),
        S("Four", "usdb", "English", "Pop", 150),
    ];

    [Fact]
    public void NoFilters_CountsEveryEntryOfEveryList()
    {
        SongFacets f = SongFacets.Of(Songs, new SongQuery());

        Assert.Equal(2, f.Languages["English"]);
        Assert.Equal(2, f.Languages["french"]);       // ignoring case
        Assert.Equal(1, f.Languages["German"]);
        Assert.Equal(4, f.LanguageTotal);
        Assert.Equal(2, f.Genres["Rock"]);
        Assert.Equal((1, 1, 1), (f.Stars[4], f.Stars[2], f.Stars[1]));
        Assert.Equal(2, f.Sources["a"]);
    }

    [Fact]
    public void AFilter_DoesNotNarrowItsOwnCounts_ButTheOthers()
    {
        // Language = French: the language list still shows all languages (switching is possible),
        // genres and sources only count French songs.
        SongFacets f = SongFacets.Of(Songs, new SongQuery { Language = "French" });

        Assert.Equal(2, f.Languages["English"]);
        Assert.Equal(4, f.LanguageTotal);               // "Language" = no language filter: all 4
        Assert.Equal(1, f.Genres["Rock"]);              // "One" only — "Three" is German
        Assert.Equal(2, f.GenreTotal);
        Assert.Equal(2, f.Sources["a"]);
        Assert.False(f.Sources.ContainsKey("b"));
    }

    [Fact]
    public void Search_NarrowsEveryFacet()
    {
        SongFacets f = SongFacets.Of(Songs, new SongQuery { Search = "t" });   // Two, Three

        Assert.Equal(2, f.LanguageTotal);
        Assert.Equal(1, f.Languages["French"]);
        Assert.False(f.Languages.ContainsKey("English"));
    }

    [Fact]
    public void Counts_MatchWhatTheFilterWouldShow()
    {
        SongQuery q = new() { Genre = "Pop", Stars = 4 };
        SongFacets f = SongFacets.Of(Songs, q);

        foreach ((string language, int count) in f.Languages)
        {
            Assert.Equal(count, (q with { Language = language }).Apply(Songs, id => id).Count());
        }
    }

    [Fact]
    public void Favourites_AreCountedUnderTheOtherFilters()
    {
        SongQuery q = new() { Language = "English", FavouriteIds = new HashSet<string> { "a::One", "a::Two", "usdb::Four" } };
        SongFacets f = SongFacets.Of(Songs, q);

        Assert.Equal(2, f.Favourites);   // One and Four are English; Two is French only
    }

    [Fact]
    public void HiddenSongs_AreNotCounted()
    {
        SongFacets f = SongFacets.Of(Songs, new SongQuery { Hidden = new HashSet<string> { "a::One" } });
        Assert.Equal(1, f.Languages["English"]);
        Assert.Equal(3, f.LanguageTotal);
    }

    [Fact]
    public void Broken_AreCountedUnderTheOtherFilters()
    {
        SongQuery q = new() { Language = "French", BrokenIds = new HashSet<string> { "a::One", "a::Two", "b::Three" } };
        Assert.Equal(2, SongFacets.Of(Songs, q).Broken);   // One and Two list French
    }

    [Fact]
    public void Duets_AreCountedUnderTheOtherFilters()
    {
        Song[] songs = [.. Songs.Select(s => s.Title is "One" or "Three" ? s with { IsDuet = true } : s)];
        Assert.Equal(1, SongFacets.Of(songs, new SongQuery { Language = "German" }).Duets);   // Three is German
    }
}
