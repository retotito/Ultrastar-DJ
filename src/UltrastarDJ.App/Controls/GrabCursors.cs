using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// CSS <c>grab</c>/<c>grabbing</c> for draggable areas. Avalonia has no standard closed-hand cursor:
/// <see cref="StandardCursorType.DragMove"/> is macOS's open hand, the closed hand is drawn here once.
/// </summary>
public static class GrabCursors
{
    // 24×24 fist seen from the back: four knuckles, thumb on the left.
    private const string FistPath =
        "M6,10 C6,7.5 9,7.5 9,10 C9,7 12.5,7 12.5,10 C12.5,7 16,7 16,10 C16,7.5 19.5,7.5 19.5,10.5 L19.5,15 " +
        "C19.5,19 17,21 13.5,21 L11,21 C8,21 6.5,19.5 5.5,17.5 L3.8,14.2 C3.2,13 4.3,11.8 5.4,12.4 L6,12.8 Z";
    private const string FingerGaps = "M9,10 L9,12.5 M12.5,10 L12.5,12.5 M16,10 L16,12.5";

    private static Cursor? _grabbing;

    public static Cursor Grab { get; } = new(StandardCursorType.DragMove);

    /// <summary>Created on first use: rendering needs the platform to be up.</summary>
    public static Cursor Grabbing => _grabbing ??= CreateGrabbing();

    private static Cursor CreateGrabbing()
    {
        RenderTargetBitmap bitmap = new(new PixelSize(32, 32));
        using (DrawingContext ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateTranslation(4, 4)))
        {
            // White with a black outline, like the system cursors: visible on light and dark backgrounds.
            ctx.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.5), Geometry.Parse(FistPath));
            ctx.DrawGeometry(null, new Pen(Brushes.Black, 1.2), Geometry.Parse(FingerGaps));
        }

        return new Cursor(bitmap, new PixelPoint(16, 16));
    }
}
