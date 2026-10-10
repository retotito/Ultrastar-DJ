namespace UltrastarDJ.Core.Game;

/// <summary>
/// Gold, silver and bronze on the score screen. Places as in sports: equal scores share a place and the next one is
/// skipped (1, 1, 3). No points, no trophy.
/// </summary>
public static class Podium
{
    /// <summary>Bronze is the last trophy.</summary>
    public const int LastPlace = 3;

    /// <summary>Per score (same order): 1–3 for a trophy, 0 for none.</summary>
    public static int[] Places(IReadOnlyList<int> scores)
    {
        int[] places = new int[scores.Count];
        for (int i = 0; i < scores.Count; i++)
        {
            int place = 1 + scores.Count(s => s > scores[i]);
            places[i] = scores[i] > 0 && place <= LastPlace ? place : 0;
        }

        return places;
    }

    /// <summary>The places to reveal one after another, bronze first so gold comes last.</summary>
    public static int[] RevealOrder(IReadOnlyList<int> places) => [.. places.Where(p => p > 0).Distinct().OrderDescending()];
}
