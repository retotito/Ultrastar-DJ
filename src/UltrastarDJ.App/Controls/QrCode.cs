using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// A QR code for <see cref="Text"/>, drawn as squares (no image). QRCoder computes the modules. Error correction L:
/// a screen does not get scratched or stained, and fewer, bigger modules are what a phone at a distance needs most —
/// the public link (https://….trycloudflare.com) is long. Every module is a whole number of device pixels: on a
/// projector at scaling 1, fractional modules blurred their edges and phones could not read the code. Includes the
/// white quiet zone scanners need around the code.
/// </summary>
public sealed class QrCode : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<QrCode, string?>(nameof(Text));

    private static readonly IBrush Dark = Brushes.Black;
    private static readonly IBrush Light = Brushes.White;
    private bool[,]? _modules;

    static QrCode()
    {
        AffectsRender<QrCode>(TextProperty);
        TextProperty.Changed.AddClassHandler<QrCode>((c, _) => c.Build());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private void Build()
    {
        if (string.IsNullOrEmpty(Text))
        {
            _modules = null;
            return;
        }

        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(Text, QRCodeGenerator.ECCLevel.L);
        // ModuleMatrix already includes a 4-module quiet zone on every side.
        int n = data.ModuleMatrix.Count;
        bool[,] m = new bool[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                m[x, y] = data.ModuleMatrix[y][x];
            }
        }

        _modules = m;
    }

    public override void Render(DrawingContext context)
    {
        if (_modules is not { } m)
        {
            return;
        }

        int n = m.GetLength(0);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        // Whole device pixels per module, centred on a whole pixel.
        double cell = Math.Max(1, Math.Floor(size * scale / n)) / scale;
        double origin = Math.Floor((size - cell * n) / 2 * scale) / scale;
        context.FillRectangle(Light, new Rect(0, 0, size, size));
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (m[x, y])
                {
                    context.FillRectangle(Dark, new Rect(origin + x * cell, origin + y * cell, cell, cell));
                }
            }
        }
    }
}
