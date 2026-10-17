using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Opens and closes the singer screens (beamer windows) and remembers which players belong to which screen.
/// Boundary between ViewModels and Avalonia windowing — ViewModels never touch <c>Window</c> directly.
/// </summary>
public interface IDisplayService
{
    DisplayConfig GetConfig(DisplayId id);

    /// <summary>The players a display shows now — as assigned, or in single-screen mode all of them on Display 1.</summary>
    IReadOnlyList<int> PlayersOn(DisplayId id);

    SingleScreenMode SingleScreenMode { get; }
    void SetSingleScreenMode(SingleScreenMode mode);

    /// <summary>
    /// Single-screen mode in effect: no display windows — the game fills the DJ window during a song
    /// (<see cref="GameLayer"/>). A change (mode, screens plugged in or out) waits until no song is on.
    /// </summary>
    bool IsSingleScreen { get; }

    /// <summary>Single-screen mode, from "Get ready" until back to the DJ view: what the DJ window shows over itself.</summary>
    ViewModels.BeamerViewModel? GameLayer { get; }

    /// <summary>Raised on the UI thread when <see cref="GameLayer"/> appears or goes.</summary>
    event Action? GameLayerChanged;

    int ScreenCount { get; }

    /// <summary>Raised on the UI thread when <see cref="IsSingleScreen"/> or the mode changed.</summary>
    event Action? SingleScreenChanged;

    /// <summary>Single-screen mode: the game layer goes, the DJ view is back.</summary>
    void ShowDjWindow();
    void SetPlayers(DisplayId id, IReadOnlyList<int> playerIds);

    bool IsOpen(DisplayId id);
    /// <summary>Opens a normal window on the DJ window's screen; the DJ drags it to the projector.</summary>
    void Open(DisplayId id);
    void Close(DisplayId id);

    bool IsFullScreen(DisplayId id);
    /// <summary>Fullscreen on whatever screen the window is on now.</summary>
    void ToggleFullScreen(DisplayId id);

    /// <summary>
    /// Raised on the UI thread when an open display's screen was unplugged (with the screen's name). The display is
    /// parked next to the DJ window and returns by itself when the screen is back.
    /// </summary>
    event Action<DisplayId, string>? ScreenLost;

    /// <summary>Raised on the UI thread when a display opens or closes (also when the DJ closes it from its title bar).</summary>
    event Action<DisplayId, bool>? OpenStateChanged;

    /// <summary>Raised on the UI thread when a display enters or leaves fullscreen, whoever triggered it.</summary>
    event Action<DisplayId, bool>? FullScreenChanged;

    /// <summary>Raised on the UI thread after <see cref="SetPlayers"/> (both displays may have changed).</summary>
    event Action? PlayersChanged;

    /// <summary>
    /// Which screen an open display's window is on, for the panel: "LG TV · 1920×1080", plus a hint while it still
    /// shares the DJ window's screen. Null while closed.
    /// </summary>
    string? ScreenText(DisplayId id);

    /// <summary>Raised on the UI thread when an open display moved (it may be on another screen now).</summary>
    event Action<DisplayId>? PlacementChanged;
}
