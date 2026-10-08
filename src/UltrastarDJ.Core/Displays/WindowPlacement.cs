namespace UltrastarDJ.Core.Displays;

public enum WindowMode { Normal, Maximized, FullScreen }

/// <summary>A screen's working area in physical pixels, its scaling (pixels per DIP) and the name the OS gives it.</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height, double Scaling, string? Name = null)
{
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// Where a window was when the app closed, so it opens there again: top-left in physical pixels (as the OS places
/// windows), size in DIPs (as Avalonia sizes them — the same on a Retina and a normal screen), and its mode. The size
/// is the window's normal size even when it was maximised, so un-maximising lands somewhere sensible.
/// <see cref="Screen"/> (name) and <see cref="ScreenX"/>/<see cref="ScreenY"/> (its origin then) find a projector again
/// after the monitor arrangement changed.
/// </summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, WindowMode Mode,
    string? Screen = null, int ScreenX = 0, int ScreenY = 0)
{
    /// <summary>
    /// The placement adjusted to the screens connected now: on the screen that holds its top-left corner, shrunk to
    /// fit that screen (never below the minimum) and moved fully onto it. Null when that screen is gone (an external
    /// monitor not plugged in today) — the window is then centred as on a first start.
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement saved, IReadOnlyList<ScreenArea> screens, double minWidth, double minHeight)
    {
        // A little inside the corner: the title bar must be reachable to drag the window.
        bool AtSpot(ScreenArea s) => s.Contains(saved.X + 20, saved.Y + 10);
        bool Named(ScreenArea s) => saved.Screen is not null && s.Name == saved.Screen;

        // 1. The named screen where it was; 2. the named screen moved elsewhere (arrangement changed): same offset on
        // it; 3. whatever screen is at the spot (no name saved, or another monitor plugged in there); else gone.
        ScreenArea[] list = [.. screens];
        if (list.Where(s => Named(s) && AtSpot(s)).Cast<ScreenArea?>().FirstOrDefault() is not { } screen)
        {
            if (list.Where(Named).Cast<ScreenArea?>().FirstOrDefault() is { } moved)
            {
                screen = moved;
                saved = saved with { X = saved.X - saved.ScreenX + moved.X, Y = saved.Y - saved.ScreenY + moved.Y };
            }
            else if (list.Where(AtSpot).Cast<ScreenArea?>().FirstOrDefault() is { } atSpot)
            {
                screen = atSpot;
            }
            else
            {
                return null;
            }
        }

        double maxW = screen.Width / screen.Scaling;
        double maxH = screen.Height / screen.Scaling;
        double w = Math.Max(minWidth, Math.Min(saved.Width, maxW));
        double h = Math.Max(minHeight, Math.Min(saved.Height, maxH));
        int x = Math.Clamp(saved.X, screen.X, Math.Max(screen.X, screen.X + screen.Width - (int)Math.Round(w * screen.Scaling)));
        int y = Math.Clamp(saved.Y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - (int)Math.Round(h * screen.Scaling)));
        return saved with { X = x, Y = y, Width = w, Height = h, Screen = screen.Name ?? saved.Screen, ScreenX = screen.X, ScreenY = screen.Y };
    }
}
