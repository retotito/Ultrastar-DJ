using Avalonia;
using Avalonia.Controls;
using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.Views;

/// <summary>
/// Opens a window where it was when the app last closed (<see cref="WindowPlacement"/>): same screen, position, size
/// and mode — or centred, when that screen is not connected today. On Linux/Wayland the compositor may ignore the
/// position (it decides placement itself); size and mode still come back.
/// </summary>
public static class WindowMemory
{
    /// <summary>Before the window is shown.</summary>
    public static void Restore(Window window, WindowPlacement? saved)
    {
        if (saved is null)
        {
            return;
        }

        List<ScreenArea> screens = [.. window.Screens.All.Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling))];
        if (WindowPlacement.Fit(saved, screens, window.MinWidth, window.MinHeight) is not { } p)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(p.X, p.Y);
        window.Width = p.Width;
        window.Height = p.Height;
        if (p.Mode != WindowMode.Normal)
        {
            // Maximised / fullscreen once it is on screen (set before showing, macOS ignores it).
            window.Opened += (_, _) => window.WindowState = p.Mode == WindowMode.FullScreen ? WindowState.FullScreen : WindowState.Maximized;
        }
    }

    /// <summary>Keeps the window's normal position and size, and hands the placement over when it closes.</summary>
    public static void Track(Window window, Action<WindowPlacement> save)
    {
        PixelPoint pos = window.Position;
        Size size = new(window.Width, window.Height);
        void Remember()
        {
            // Only the normal bounds: maximised / fullscreen bounds are not what un-maximising should return to.
            if (window.WindowState == WindowState.Normal)
            {
                pos = window.Position;
                size = window.ClientSize;
            }
        }

        window.PositionChanged += (_, _) => Remember();
        window.Resized += (_, _) => Remember();
        window.Opened += (_, _) => Remember();
        window.Closing += (_, _) =>
        {
            Remember();
            WindowMode mode = window.WindowState switch
            {
                WindowState.FullScreen => WindowMode.FullScreen,
                WindowState.Maximized => WindowMode.Maximized,
                _ => WindowMode.Normal,
            };
            save(new WindowPlacement(pos.X, pos.Y, Math.Round(size.Width), Math.Round(size.Height), mode));
        };
    }
}
