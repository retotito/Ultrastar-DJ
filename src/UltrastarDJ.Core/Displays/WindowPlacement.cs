namespace UltrastarDJ.Core.Displays;

public enum WindowMode { Normal, Maximized, FullScreen }

/// <summary>A screen's working area in physical pixels, and its scaling (pixels per DIP).</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height, double Scaling)
{
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// Where a window was when the app closed, so it opens there again: top-left in physical pixels (as the OS places
/// windows), size in DIPs (as Avalonia sizes them — the same on a Retina and a normal screen), and its mode. The size
/// is the window's normal size even when it was maximised, so un-maximising lands somewhere sensible.
/// </summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, WindowMode Mode)
{
    /// <summary>
    /// The placement adjusted to the screens connected now: on the screen that holds its top-left corner, shrunk to
    /// fit that screen (never below the minimum) and moved fully onto it. Null when that screen is gone (an external
    /// monitor not plugged in today) — the window is then centred as on a first start.
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement saved, IReadOnlyList<ScreenArea> screens, double minWidth, double minHeight)
    {
        // A little inside the corner: the title bar must be reachable to drag the window.
        ScreenArea? found = screens.Cast<ScreenArea?>().FirstOrDefault(s => s!.Value.Contains(saved.X + 20, saved.Y + 10));
        if (found is not { } screen)
        {
            return null;
        }

        double maxW = screen.Width / screen.Scaling;
        double maxH = screen.Height / screen.Scaling;
        double w = Math.Max(minWidth, Math.Min(saved.Width, maxW));
        double h = Math.Max(minHeight, Math.Min(saved.Height, maxH));
        int x = Math.Clamp(saved.X, screen.X, Math.Max(screen.X, screen.X + screen.Width - (int)Math.Round(w * screen.Scaling)));
        int y = Math.Clamp(saved.Y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - (int)Math.Round(h * screen.Scaling)));
        return saved with { X = x, Y = y, Width = w, Height = h };
    }
}
