namespace UltrastarDJ.Core.Players;

/// <summary>
/// Which players' microphones are plugged in. Device ids are device names (stable across re-plugging), so a mic
/// that comes back is recognised and its players keep their assignment.
/// </summary>
public static class MicPresence
{
    /// <summary>Devices that appeared / disappeared between two enumerations.</summary>
    public sealed record Change(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
    {
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
    }

    /// <summary>Players with an assigned mic whose device is not present.</summary>
    public static IReadOnlyList<PlayerConfig> Missing(IEnumerable<PlayerConfig> players, IReadOnlySet<string> presentDeviceIds)
        => players.Where(p => p.Mic is { } m && !presentDeviceIds.Contains(m.DeviceId)).ToList();

    /// <summary>Players whose mic is on one of <paramref name="deviceIds"/>.</summary>
    public static IReadOnlyList<PlayerConfig> On(IEnumerable<PlayerConfig> players, IEnumerable<string> deviceIds)
    {
        HashSet<string> set = [.. deviceIds];
        return players.Where(p => p.Mic is { } m && set.Contains(m.DeviceId)).ToList();
    }

    public static Change Diff(IReadOnlySet<string> before, IReadOnlySet<string> after)
        => new(after.Except(before).Order(StringComparer.Ordinal).ToList(), before.Except(after).Order(StringComparer.Ordinal).ToList());

    /// <summary>"Player 2 (SingStar, right)" — for toasts and dialogs.</summary>
    public static string Describe(PlayerConfig p)
        => p.Mic is { } m ? $"{p.Name} ({m.DeviceId}, {m.Channel.ToString().ToLowerInvariant()})" : p.Name;
}
