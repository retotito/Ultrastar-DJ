namespace UltrastarDJ.Core.Playback;

/// <summary>
/// Whether a channel's chosen output is plugged in, and what replaces one that is gone: the speaker the system plays
/// on now, chosen for good. Not "System default": the OS makes a returning Bluetooth speaker its default again, and the
/// game went back to it by itself. When it is back the DJ is told and chooses it again.
/// </summary>
public static class OutputPresence
{
    /// <summary>mpv's id of the system default output (always there).</summary>
    public const string SystemDefault = "auto";

    public static bool IsMissing(string chosen, IReadOnlySet<string> present) => chosen != SystemDefault && !present.Contains(chosen);

    /// <param name="lost">The output that is gone.</param>
    /// <param name="present">Outputs plugged in now.</param>
    /// <param name="systemDefaults">The system's default output as different sources report it, best first (null = unknown).</param>
    public static string Replacement(string lost, IReadOnlySet<string> present, IEnumerable<string?> systemDefaults)
        => systemDefaults.FirstOrDefault(id => id is not null && id != lost && present.Contains(id)) ?? SystemDefault;
}
