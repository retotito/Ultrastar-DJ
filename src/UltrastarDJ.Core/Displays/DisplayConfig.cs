namespace UltrastarDJ.Core.Displays;

/// <summary>
/// Persisted configuration of one singer screen. The open/closed state is runtime-only and lives in the display service.
/// </summary>
/// <param name="Id">Which beamer.</param>
/// <param name="PlayerIds">Players (1–4) rendered on this screen. A player appears on at most one display.</param>
public sealed record DisplayConfig(DisplayId Id, IReadOnlyList<int> PlayerIds)
{
    /// <summary>Where its window was when last closed — "Open" puts it there again if that screen is connected.</summary>
    public WindowPlacement? Placement { get; init; }

    public static DisplayConfig Default(DisplayId id) => new(id, []);
}
