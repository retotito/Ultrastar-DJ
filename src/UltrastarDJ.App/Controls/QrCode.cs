using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// A QR code for <see cref="Text"/>, drawn as squares (sharp at any size, no image). QRCoder computes the modules;
/// error correction M survives a projector's blur and a phone camera at a distance. Includes the white quiet zone
/// scanners need around the code.
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
        using QRCodeData data = generator.CreateQrCode(Text, QRCodeGenerator.ECCLevel.M);
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
        double cell = Math.Floor(size / n * 4) / 4; // quarter pixels: crisp edges without visible seams
        double origin = (size - cell * n) / 2;
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
