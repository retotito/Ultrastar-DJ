using UltrastarDJ.Core.Game;

namespace UltrastarDJ.Core.Tests.Game;

public class PhraseRatingTests
{
    [Theory]
    [InlineData(1.00, 8)]
    [InlineData(0.94, 8)]   // USDX: "perfect!" already from ~94 %
    [InlineData(0.93, 7)]
    [InlineData(0.82, 7)]
    [InlineData(0.80, 6)]
    [InlineData(0.69, 6)]
    [InlineData(0.68, 5)]
    [InlineData(0.00, 0)]
    [InlineData(1.20, 8)]   // clamped
    public void Rate_IsRoundedEighths(double perfection, int rating)
    {
        Assert.Equal(rating, PhraseRating.Rate(perfection));
    }

    [Theory]
    [InlineData(8, "PERFECT!")]
    [InlineData(7, "AWESOME!")]
    [InlineData(6, "GREAT!")]
    [InlineData(5, null)]
    [InlineData(0, null)]
    public void Text_OnlyTheTopThree(int rating, string? text)
    {
        Assert.Equal(text, PhraseRating.Text(rating));
    }
}
