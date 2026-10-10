namespace UltrastarDJ.Core.Playback;

/// <summary>
/// Which output a channel really plays on while devices come and go: the one the DJ chose, or — while that is
/// unplugged — the system default. The choice itself stays, so the channel goes back once the device returns.
/// </summary>
public static class OutputPresence
{
    /// <summary>mpv's id of the system default output (always there).</summary>
    public const string SystemDefault = "auto";

    public static bool IsMissing(string chosen, IReadOnlySet<string> present) => chosen != SystemDefault && !present.Contains(chosen);

    public static string Effective(string chosen, IReadOnlySet<string> present) => IsMissing(chosen, present) ? SystemDefault : chosen;
}
