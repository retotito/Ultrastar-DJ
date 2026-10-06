namespace UltrastarDJ.Core.Game;

/// <summary>
/// The word a player gets at the end of each phrase, after UltraStar Deluxe (<c>UScreenSingController.OnSentenceEnd</c>:
/// <c>Rating := Round(LinePerfection * 8)</c>; texts from <c>UThemes</c> LineBonusText). USDX shows all nine levels
/// (awful … perfect); we show only the top three so the beamer praises and never mocks.
/// </summary>
public static class PhraseRating
{
    public const int MaxRating = 8;

    /// <summary>0..8 from the share of the phrase's points sung (0..1). Banker's rounding like Pascal's Round.</summary>
    public static int Rate(double perfection) => (int)Math.Round(Math.Clamp(perfection, 0, 1) * MaxRating, MidpointRounding.ToEven);

    /// <summary>The popup text, or null for ratings that show nothing (below "great").</summary>
    public static string? Text(int rating) => rating switch
    {
        8 => "PERFECT!",
        7 => "AWESOME!",
        6 => "GREAT!",
        _ => null,
    };
}
