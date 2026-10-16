using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// CSS <c>grab</c>/<c>grabbing</c> for draggable areas. Avalonia has no standard hand cursors for this:
/// <see cref="StandardCursorType.DragMove"/> is macOS's open hand but Windows' drag-and-drop arrow with a dashed box,
/// so the open hand (elsewhere) and the closed hand (everywhere) are drawn here once — on Windows in the pointer
/// colour and size the user chose (Settings → Accessibility → Mouse pointer), so they match the system's own pointers.
/// </summary>
public static partial class GrabCursors
{
    // 24×24 open hand seen from the back: four straight fingers, thumb out on the left.
    private const string HandPath =
        "M8,21 C6.5,19.5 5,17 3.8,14.5 C3.2,13.2 4.4,12 5.5,12.8 L7,14.2 L7,5.5 C7,4.2 9.4,4.2 9.4,5.5 L9.4,11 " +
        "L9.6,4.2 C9.6,2.9 12,2.9 12,4.2 L12,11 L12.2,4.7 C12.2,3.4 14.6,3.4 14.6,4.7 L14.6,11.5 " +
        "L14.8,6.7 C14.8,5.4 17.2,5.4 17.2,6.7 L17.2,15 C17.2,18.5 15.5,21 12.5,21 Z";

    // 24×24 fist seen from the back: four knuckles, thumb on the left.
    private const string FistPath =
        "M6,10 C6,7.5 9,7.5 9,10 C9,7 12.5,7 12.5,10 C12.5,7 16,7 16,10 C16,7.5 19.5,7.5 19.5,10.5 L19.5,15 " +
        "C19.5,19 17,21 13.5,21 L11,21 C8,21 6.5,19.5 5.5,17.5 L3.8,14.2 C3.2,13 4.3,11.8 5.4,12.4 L6,12.8 Z";
    private const string FingerGaps = "M9,10 L9,12.5 M12.5,10 L12.5,12.5 M16,10 L16,12.5";

    // The drawings are made for a 32 px cursor, the standard size at 100 % scaling.
    private const int DesignPx = 32;

    private static Cursor? _grab;
    private static Cursor? _grabbing;

    /// <summary>Created on first use: rendering needs the platform to be up.</summary>
    public static Cursor Grab => _grab ??= OperatingSystem.IsMacOS() ? new Cursor(StandardCursorType.DragMove) : Draw(HandPath, null);

    /// <summary>Created on first use: rendering needs the platform to be up.</summary>
    public static Cursor Grabbing => _grabbing ??= Draw(FistPath, FingerGaps);

    private static Cursor Draw(string outline, string? lines)
    {
        (bool dark, int px) = OperatingSystem.IsWindows() ? WindowsPointer() : (false, DesignPx);
        // White with a black outline like the standard pointers, or the other way round for Windows' black ones:
        // visible on light and dark backgrounds either way.
        IBrush fill = dark ? Brushes.Black : Brushes.White;
        IBrush edge = dark ? Brushes.White : Brushes.Black;
        double scale = (double)px / DesignPx;

        RenderTargetBitmap bitmap = new(new PixelSize(px, px));
        using (DrawingContext ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateTranslation(4, 4) * Matrix.CreateScale(scale, scale)))
        {
            ctx.DrawGeometry(fill, new Pen(edge, 1.5), Geometry.Parse(outline));
            if (lines is not null)
            {
                ctx.DrawGeometry(null, new Pen(edge, 1.2), Geometry.Parse(lines));
            }
        }

        return new Cursor(bitmap, new PixelPoint(px / 2, px / 2));
    }

    /// <summary>
    /// The user's pointer style and size: a black scheme ("Windows Black", or inverted, which is black on light
    /// backgrounds), and the base size (32 standard, 48 at size 2 …) times the display scaling, as Windows sizes its own.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (bool Dark, int Px) WindowsPointer()
    {
        try
        {
            using RegistryKey? cursors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            string scheme = cursors?.GetValue(null) as string ?? "";
            bool dark = scheme.Contains("Black", StringComparison.OrdinalIgnoreCase) || scheme.Contains("Inverted", StringComparison.OrdinalIgnoreCase);
            int basePx = cursors?.GetValue("CursorBaseSize") is int b and >= DesignPx ? b : DesignPx;
            int px = (int)Math.Round(basePx * GetDpiForSystem() / 96.0);
            return (dark, Math.Clamp(px, DesignPx, 256));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return (false, DesignPx);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
