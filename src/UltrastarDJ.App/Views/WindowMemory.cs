using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.Views;

/// <summary>
/// Opens a window where it was when the app last closed (<see cref="WindowPlacement"/>): same screen, position, size
/// and mode — or centred, when that screen is not connected today. On Linux/Wayland the compositor may ignore the
/// position (it decides placement itself); size and mode still come back.
/// </summary>
public static class WindowMemory
{
    /// <summary>Before the window is shown. False when there is nothing saved or its screen is gone (the caller places it).</summary>
    public static bool Restore(Window window, WindowPlacement? saved)
    {
        if (saved is null)
        {
            return false;
        }

        List<ScreenArea> screens = [.. window.Screens.All.Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling, s.DisplayName))];
        if (WindowPlacement.Fit(saved, screens, window.MinWidth, window.MinHeight) is not { } p)
        {
            return false;
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

        return true;
    }

    /// <summary>
    /// First start (or its screen is gone): centred on the main screen when the designed size fits, else maximised —
    /// with the minimum size lowered to the screen, so nothing is cut off on small or zoomed screens (a 1366 × 768
    /// laptop at 125 % has only about 1090 × 580 to give).
    /// </summary>
    public static void FitToScreen(Window window)
    {
        if ((window.Screens.Primary ?? window.Screens.All.FirstOrDefault()) is not { } screen)
        {
            return;
        }

        double availableWidth = screen.WorkingArea.Width / screen.Scaling;
        double availableHeight = screen.WorkingArea.Height / screen.Scaling;
        window.MinWidth = Math.Min(window.MinWidth, availableWidth);
        window.MinHeight = Math.Min(window.MinHeight, availableHeight);
        // Room for the title bar the working area does not include.
        const double TitleBarHeight = 40;
        if (window.Width <= availableWidth && window.Height + TitleBarHeight <= availableHeight)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.Width = Math.Min(window.Width, availableWidth);
        window.Height = Math.Min(window.Height, availableHeight - TitleBarHeight);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Opened += (_, _) => window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Keeps the window's normal position and size and its mode as they change, and hands them over when it closes.
    /// Read while the window lives, never at closing: when the app quits, a closing window already reports position
    /// (0, 0) — every display was saved on the main screen.
    /// </summary>
    public static void Track(Window window, Action<WindowPlacement> save)
    {
        PixelPoint pos = window.Position;
        Size size = new(window.Width, window.Height);
        WindowMode mode = WindowMode.Normal;
        bool closing = false;
        void Remember()
        {
            if (closing || !window.IsVisible)
            {
                return;
            }

            mode = window.WindowState switch
            {
                WindowState.FullScreen => WindowMode.FullScreen,
                WindowState.Maximized => WindowMode.Maximized,
                _ => WindowMode.Normal,
            };
            // Only the normal bounds: maximised / fullscreen bounds are not what un-maximising should return to.
            // (The screen comes from this position too: maximised or fullscreen stay on the screen they were dragged to.)
            if (window.WindowState == WindowState.Normal)
            {
                pos = window.Position;
                size = window.ClientSize;
            }
        }

        window.PositionChanged += (_, _) => Remember();
        window.Resized += (_, _) => Remember();
        window.Opened += (_, _) => Remember();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                Remember();
            }
        };
        window.Closing += (_, _) =>
        {
            closing = true;
            // The screen it is on, by name and origin: found again after the monitor arrangement changed.
            Screen? screen = window.Screens.ScreenFromPoint(new PixelPoint(pos.X + 20, pos.Y + 10));
            save(new WindowPlacement(pos.X, pos.Y, Math.Round(size.Width), Math.Round(size.Height), mode,
                screen?.DisplayName, screen?.WorkingArea.X ?? 0, screen?.WorkingArea.Y ?? 0));
        };
    }
}
