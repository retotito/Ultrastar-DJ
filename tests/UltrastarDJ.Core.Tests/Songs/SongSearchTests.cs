using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongSearchTests
{
    private static Song S(string title, string artist) => new() { Id = $"t::{title}", SourceId = "t", Title = title, Artist = artist, Bpm = 300 };

    private static readonly Song Halo = S("Halo", "Beyoncé");
    private static readonly Song Ace = S("Ace of Spades", "Motörhead");
    private static readonly Song DontStop = S("Don't Stop Me Now", "Queen");
    private static readonly Song Bohemian = S("Bohemian Rhapsody", "Queen");
    private static readonly Song Thunder = S("Thunderstruck", "AC/DC");
    private static readonly Song Simon = S("The Boxer", "Simon & Garfunkel");
    private static readonly Song Strasse = S("Straße der Sehnsucht", "Anna");
    private static readonly Song Live = S("Live Forever", "Oasis");

    [Theory]
    [InlineData("beyonce", "Halo")]          // accents
    [InlineData("BEYONCÉ", "Halo")]
    [InlineData("motorhead", "Ace of Spades")]
    [InlineData("strasse", "Straße der Sehnsucht")]
    [InlineData("dont stop", "Don't Stop Me Now")]   // punctuation
    [InlineData("acdc", "Thunderstruck")]
    [InlineData("ac dc", "Thunderstruck")]
    [InlineData("simon and garfunkel", "The Boxer")] // & = and
    [InlineData("queen bohemian", "Bohemian Rhapsody")] // words in any order, across artist and title
    [InlineData("rhapsody queen", "Bohemian Rhapsody")]
    [InlineData("queen bohem", "Bohemian Rhapsody")]  // the last word while still typing
    public void Matches_ForgivesAccentsPunctuationAndOrder(string query, string title)
    {
        Song song = new[] { Halo, Ace, DontStop, Bohemian, Thunder, Simon, Strasse }.Single(s => s.Title == title);
        Assert.True(SongSearch.For(query).Matches(song));
    }

    [Fact]
    public void Matches_EveryWordMustAppear()
    {
        Assert.False(SongSearch.For("queen thunder").Matches(Bohemian));
        Assert.True(SongSearch.For("").Matches(Bohemian));
    }

    [Theory]
    [InlineData("bohemain")]          // swapped letters
    [InlineData("rhapsodie")]         // one wrong
    [InlineData("rapsody")]           // one missing
    [InlineData("bohemian rhapsodyy")] // one extra
    [InlineData("bohemain rapsody")]  // a typo in each word
    public void MatchesSimilar_ForgivesTypos(string query)
    {
        Assert.False(SongSearch.For(query).Matches(Bohemian));
        Assert.True(SongSearch.For(query).MatchesSimilar(Bohemian));
    }

    [Fact]
    public void MatchesSimilar_ShortWordsMustBeExact()
    {
        // "love" is not "live": up to 3 letters no typo, 4 letters one — "lov" stays exact.
        Assert.False(SongSearch.For("lvie").MatchesSimilar(S("Ice", "X")));
        Assert.False(SongSearch.For("liv").MatchesSimilar(S("Love", "X")));
        Assert.Equal(0, SongSearch.AllowedTypos(3));
        Assert.Equal(1, SongSearch.AllowedTypos(4));
        Assert.Equal(2, SongSearch.AllowedTypos(8));
    }

    [Fact]
    public void Query_TypoMatchesOnlyWhenFewFound()
    {
        Song[] library = [Bohemian, DontStop, Live];
        // "bohemain" finds nothing plainly → the typo match is added.
        Assert.Equal(["Bohemian Rhapsody"], new SongQuery { Search = "bohemain" }.Apply(library, _ => "").Select(s => s.Title));

        // Plenty of plain matches → no typo matches mixed in.
        Song[] many = [.. Enumerable.Range(1, SongQuery.SimilarBelow).Select(i => S($"Live {i}", "Band")), S("Lime", "Band")];
        Assert.DoesNotContain("Lime", new SongQuery { Search = "live" }.Apply(many, _ => "").Select(s => s.Title));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("bohemain", "bohemian", 1)]
    [InlineData("abc", "abc", 0)]
    public void Distance_CountsTypingSteps(string a, string b, int expected) => Assert.Equal(expected, SongSearch.Distance(a, b, 3));
}
