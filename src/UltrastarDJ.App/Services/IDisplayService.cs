using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.Services;

/// <summary>A monitor as seen by the display service. Stable enough to persist by <see cref="Name"/>.</summary>
public sealed record ScreenInfo(int Index, string Name, int WidthPx, int HeightPx, bool IsPrimary);

/// <summary>
/// Opens and closes the singer screens (beamer windows) and remembers which players belong to which screen.
/// Boundary between ViewModels and Avalonia windowing — ViewModels never touch <c>Window</c> directly.
/// </summary>
public interface IDisplayService
{
    IReadOnlyList<ScreenInfo> Screens { get; }
    void RefreshScreens();

    DisplayConfig GetConfig(DisplayId id);
    void SetPlayers(DisplayId id, IReadOnlyList<int> playerIds);

    bool IsOpen(DisplayId id);
    void Open(DisplayId id, ScreenInfo screen);
    void Close(DisplayId id);

    /// <summary>Raised on the UI thread when a display opens or closes.</summary>
    event Action<DisplayId, bool>? OpenStateChanged;
}
