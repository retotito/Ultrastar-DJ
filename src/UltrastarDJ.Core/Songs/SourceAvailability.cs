namespace UltrastarDJ.Core.Songs;

/// <summary>
/// Song folders on USB drives come and go. Between two checks: which sources disappeared (toast, songs greyed) and
/// which came back (toast, rescan). Only enabled sources count — a switched-off source is nobody's concern.
/// </summary>
public static class SourceAvailability
{
    public static (IReadOnlyList<string> WentAway, IReadOnlyList<string> CameBack) Diff(IReadOnlySet<string> unavailableBefore, IReadOnlySet<string> unavailableNow)
        => ([.. unavailableNow.Where(id => !unavailableBefore.Contains(id)).Order(StringComparer.Ordinal)],
            [.. unavailableBefore.Where(id => !unavailableNow.Contains(id)).Order(StringComparer.Ordinal)]);

    /// <summary>The DJ-facing reason a song from an unreachable folder cannot be loaded.</summary>
    public static string NotConnected(string sourceLabel) => $"{sourceLabel} is not connected — plug in the drive.";
}
