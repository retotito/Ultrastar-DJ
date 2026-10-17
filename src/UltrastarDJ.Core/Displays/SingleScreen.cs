namespace UltrastarDJ.Core.Displays;

/// <summary>Game Displays → Single-screen mode (docs/04-ui.md "Single-screen mode").</summary>
public enum SingleScreenMode
{
    /// <summary>On while only one screen is connected.</summary>
    Automatic,
    On,
    Off,
}

/// <summary>
/// One screen for the DJ and the singers: only Display 1 is used — in front, fullscreen, during a song — and it shows
/// every player assigned to either display. The saved assignment is never changed: with a second screen, Display 2's
/// players are back there.
/// </summary>
public static class SingleScreen
{
    public static bool IsActive(SingleScreenMode mode, int screenCount) => mode switch
    {
        SingleScreenMode.On => true,
        SingleScreenMode.Off => false,
        _ => screenCount <= 1,
    };

    /// <summary>The players a display shows: as assigned, or (single-screen) all of them on Display 1, none on 2.</summary>
    public static IReadOnlyList<int> PlayersOn(DisplayId display, DisplayConfig first, DisplayConfig second, bool oneScreen)
    {
        if (!oneScreen)
        {
            return display == DisplayId.Beamer1 ? first.PlayerIds : second.PlayerIds;
        }

        return display == DisplayId.Beamer1 ? [.. first.PlayerIds.Concat(second.PlayerIds).Distinct().Order()] : [];
    }
}
