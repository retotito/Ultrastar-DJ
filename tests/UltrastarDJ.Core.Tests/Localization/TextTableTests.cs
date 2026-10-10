using UltrastarDJ.Core.Localization;

namespace UltrastarDJ.Core.Tests.Localization;

public class TextTableTests
{
    private static readonly Dictionary<string, string> English = new() { ["a"] = "Songs", ["b"] = "{0:N0} songs" };

    [Fact]
    public void Translated_Wins()
        => Assert.Equal("Chansons", new TextTable(English, new Dictionary<string, string> { ["a"] = "Chansons" }).Get("a"));

    [Fact]
    public void Missing_FallsBackToEnglish()
        => Assert.Equal("{0:N0} songs", new TextTable(English, new Dictionary<string, string>()).Get("b"));

    [Fact]
    public void EmptyTranslation_FallsBackToEnglish()
        => Assert.Equal("Songs", new TextTable(English, new Dictionary<string, string> { ["a"] = "  " }).Get("a"));

    [Fact]
    public void UnknownKey_IsShownSoItGetsNoticed()
        => Assert.Equal("⟦nope⟧", new TextTable(English, null).Get("nope"));

    [Fact]
    public void Pseudo_IsLongerAccentedAndBracketed_PlaceholdersKept()
    {
        string p = Pseudo.Of("{0:N0} songs found");
        Assert.StartsWith("[", p);
        Assert.EndsWith("]", p);
        Assert.Contains("{0:N0}", p);
        Assert.DoesNotContain("songs", p);
        Assert.True(p.Length >= "{0:N0} songs found".Length * 1.3);
    }

    [Fact]
    public void Placeholders_SameSet_Ok()
        => Assert.True(Placeholders.Match("{0} of {1:N0}", "{1:N0} sur {0}"));

    [Theory]
    [InlineData("{0} of {1}", "{0} sur")]
    [InlineData("{0} songs", "{0} {2} chansons")]
    public void Placeholders_Different_NotOk(string english, string translated) => Assert.False(Placeholders.Match(english, translated));
}
