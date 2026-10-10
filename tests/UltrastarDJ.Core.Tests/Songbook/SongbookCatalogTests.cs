using UltrastarDJ.Core.Songbook;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songbook;

public class SongbookCatalogTests
{
    private static Song S(string title, string artist, string? language, string? genre, int? views = null, string? yt = null, int? usdb = null)
        => new() { Id = $"x::{title}", SourceId = "x", Title = title, Artist = artist, Bpm = 300, Language = language, Genre = genre, UsdbViews = views, YouTubeId = yt, UsdbId = usdb };

    [Fact]
    public void ListsEachLanguageAndGenreOnce_AndReferencesThemByIndex()
    {
        SongbookCatalog c = SongbookCatalog.Build(
        [
            S("B", "Zed", "english, French", "Pop, Rock"),
            S("A", "Abba", "English", "Pop"),
        ]);

        Assert.Equal(["English", "French"], c.Languages);
        Assert.Equal(["Pop", "Rock"], c.Genres);
        SongbookEntry zed = c.Songs.Single(e => e.Artist == "Zed");
        Assert.Equal([0, 1], zed.Languages);
        Assert.Equal([0, 1], zed.Genres);
        Assert.Equal([0], c.Songs.Single(e => e.Artist == "Abba").Languages);   // "english" and "English": one entry
    }

    [Fact]
    public void SortedByArtistThenTitle()
    {
        SongbookCatalog c = SongbookCatalog.Build([S("Z", "beta", null, null), S("B", "Alpha", null, null), S("A", "alpha", null, null)]);

        Assert.Equal(["A", "B", "Z"], c.Songs.Select(e => e.Title));
    }

    [Fact]
    public void CarriesStarsUsdbAndKnownYouTubeIds()
    {
        SongbookCatalog c = SongbookCatalog.Build([S("A", "X", null, null, views: 2500, usdb: 7), S("B", "X", null, null, yt: "dQw4w9WgXcQ")]);

        SongbookEntry usdb = c.Songs[0], local = c.Songs[1];
        Assert.Equal((4, true, (string?)null), (usdb.Stars, usdb.Usdb, usdb.YouTubeId));
        Assert.Equal((0, false, "dQw4w9WgXcQ"), (local.Stars, local.Usdb, local.YouTubeId));
    }

    [Fact]
    public void NotOffered_SongsAreLeftOut_AndTheirLanguagesWithThem()
    {
        // Broken songs and songs that could not be loaded: guests must not request what will not play well.
        SongbookCatalog c = SongbookCatalog.Build(
            [S("Good", "A", "English", "Pop"), S("Broken", "B", "French", "Chanson")],
            notOffered: new HashSet<string> { "x::Broken" });

        Assert.Equal(["Good"], c.Songs.Select(e => e.Title));
        Assert.Equal(["English"], c.Languages);
    }

    [Fact]
    public void Duets_AreMarked()
    {
        SongbookCatalog c = SongbookCatalog.Build([S("Solo", "A", null, null), S("Shallow [DUET]", "B", null, null) with { IsDuet = true }]);
        Assert.Equal([false, true], c.Songs.OrderBy(e => e.Title).Select(e => e.Duet).Reverse());
    }
}
