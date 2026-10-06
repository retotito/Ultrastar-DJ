using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class ValueListTests
{
    [Theory]
    [InlineData("French, English", new[] { "French", "English" })]
    [InlineData("Chinese (romanized), French", new[] { "Chinese (romanized)", "French" })]
    [InlineData("English;German / Dutch|Danish", new[] { "English", "German", "Dutch", "Danish" })]
    [InlineData(" English ,, ", new[] { "English" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Languages_SplitOnUsdbSyncerSeparators(string? text, string[] expected)
    {
        Assert.Equal(expected, ValueList.Split(text, ValueList.LanguageSeparators));
    }

    [Fact]
    public void Genres_SplitOnCommaOnly()
    {
        // "R&B/Soul" is one genre name; usdb_syncer splits genres on commas only.
        Assert.Equal(["R&B/Soul", "Pop"], ValueList.Split("R&B/Soul, Pop", ValueList.GenreSeparators));
    }

    [Theory]
    [InlineData("French", true)]
    [InlineData("French, English", true)]
    [InlineData("English, French", true)]
    [InlineData("Chinese (romanized), French", true)]
    [InlineData("english, FRENCH", true)]
    [InlineData("German, Suisse German", false)]
    [InlineData("Frenchy", false)]
    [InlineData(null, false)]
    public void Contains_MatchesWholeEntriesIgnoringCase(string? text, bool expected)
    {
        Assert.Equal(expected, ValueList.Contains(text, "French", ValueList.LanguageSeparators));
    }

    [Fact]
    public void Contains_RomanizedIsItsOwnEntry()
    {
        Assert.False(ValueList.Contains("Chinese (romanized)", "Chinese", ValueList.LanguageSeparators));
        Assert.True(ValueList.Contains("Chinese (romanized), English", "Chinese (romanized)", ValueList.LanguageSeparators));
    }

    [Fact]
    public void Distinct_OneEntryPerValue_SortedIgnoringCase()
    {
        string?[] texts = ["French, English", "English, French", "english", "Chinese (romanized), French", null, ""];

        Assert.Equal(["Chinese (romanized)", "English", "French"], ValueList.Distinct(texts, ValueList.LanguageSeparators));
    }
}
