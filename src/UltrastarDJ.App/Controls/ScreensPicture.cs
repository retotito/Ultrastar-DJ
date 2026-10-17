using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// The pictures in the single-screen explanation (Game Displays → ⓘ), drawn flat after the prototype's mockup:
/// <see cref="Single"/> false — the DJ laptop between two displays showing the game; true — one laptop showing the
/// game. Scales to its width (3 : 1).
/// </summary>
public sealed class ScreensPicture : Control
{
    public static readonly StyledProperty<bool> SingleProperty = AvaloniaProperty.Register<ScreensPicture, bool>(nameof(Single));

    // Device and screen colours: the same on light and dark themes (they are things, not UI).
    private static readonly IBrush Shell = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1));
    private static readonly IBrush ShellDark = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC4));
    private static readonly IBrush Stage = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x28));
    private static readonly IBrush Lyrics = new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static readonly IBrush LyricsNext = new SolidColorBrush(Colors.White, 0.55);
    private static readonly IBrush Lane = new SolidColorBrush(Colors.White, 0.08);
    private static readonly IBrush DjBg = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
    private static readonly IBrush DjSide = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC));
    private static readonly IBrush DjRow = new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xE0));
    private static readonly IBrush DjVideo = new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x30));
    private static readonly IBrush DjGo = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E));
    private static readonly IBrush[] Players =
    [
        new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)), new SolidColorBrush(Color.FromRgb(0xEA, 0x43, 0x35)),
        new SolidColorBrush(Color.FromRgb(0x34, 0xA8, 0x53)), new SolidColorBrush(Color.FromRgb(0xFB, 0xBC, 0x04)),
    ];

    static ScreensPicture() => AffectsRender<ScreensPicture>(SingleProperty);

    public bool Single { get => GetValue(SingleProperty); set => SetValue(SingleProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        return new Size(w, w / 3);
    }

    public override void Render(DrawingContext ctx)
    {
        // Drawn on a 600 × 200 canvas, scaled to the control.
        double k = Bounds.Width / 600;
        using (ctx.PushTransform(Matrix.CreateScale(k, k)))
        {
            if (Single)
            {
                Laptop(ctx, new Rect(170, 18, 260, 150), game: true, [0, 1, 2, 3]);
            }
            else
            {
                Monitor(ctx, new Rect(8, 10, 196, 118), [0, 1]);
                Monitor(ctx, new Rect(396, 10, 196, 118), [2, 3]);
                Laptop(ctx, new Rect(205, 54, 190, 110), game: false, []);
            }
        }
    }

    private static void Monitor(DrawingContext ctx, Rect screen, int[] players)
    {
        Rect bezel = screen.Inflate(6);
        ctx.DrawRectangle(Shell, null, new RoundedRect(bezel, 7));
        Game(ctx, screen, players);
        // Stand and foot.
        double cx = screen.Center.X;
        ctx.DrawRectangle(ShellDark, null, new Rect(cx - 14, bezel.Bottom, 28, 26));
        ctx.DrawRectangle(Shell, null, new RoundedRect(new Rect(cx - 42, bezel.Bottom + 26, 84, 7), 3));
    }

    private static void Laptop(DrawingContext ctx, Rect screen, bool game, int[] players)
    {
        Rect lid = screen.Inflate(6);
        ctx.DrawRectangle(Shell, null, new RoundedRect(lid, 7));
        if (game)
        {
            Game(ctx, screen, players);
        }
        else
        {
            Dj(ctx, screen);
        }

        // Base: a little wider than the lid, a lighter edge in front.
        double extra = screen.Width * 0.12;
        StreamGeometry deck = new();
        using (StreamGeometryContext g = deck.Open())
        {
            g.BeginFigure(new Point(lid.Left - extra * 0.3, lid.Bottom), true);
            g.LineTo(new Point(lid.Right + extra * 0.3, lid.Bottom));
            g.LineTo(new Point(lid.Right + extra, lid.Bottom + 10));
            g.LineTo(new Point(lid.Left - extra, lid.Bottom + 10));
            g.EndFigure(true);
        }

        ctx.DrawGeometry(ShellDark, null, deck);
        ctx.DrawRectangle(Shell, null, new RoundedRect(new Rect(lid.Left - extra, lid.Bottom + 10, lid.Width + 2 * extra, 4), 2));
    }

    // The singers' view: lanes with note bars in the players' colours, lyrics at the bottom.
    private static void Game(DrawingContext ctx, Rect r, int[] players)
    {
        ctx.DrawRectangle(Stage, null, new RoundedRect(r, 3));
        double lanesH = r.Height * 0.74;
        int n = Math.Max(1, players.Length);
        double laneH = lanesH / n;
        for (int i = 0; i < players.Length; i++)
        {
            double y = r.Top + i * laneH;
            ctx.DrawRectangle(Lane, null, new Rect(r.Left + 4, y + laneH - 1, r.Width - 8, 0.8));
            IBrush c = Players[players[i] % Players.Length];
            // A short phrase: bars at different heights and lengths.
            (double x, double w, double row)[] bars = [(0.06, 0.16, 0.6), (0.25, 0.22, 0.35), (0.50, 0.14, 0.5), (0.68, 0.24, 0.25)];
            double barH = Math.Max(3, laneH * 0.16);
            foreach ((double x, double w, double row) in bars)
            {
                Rect bar = new(r.Left + r.Width * x, y + laneH * row, r.Width * w, barH);
                ctx.DrawRectangle(c, null, new RoundedRect(bar, barH / 2));
            }
        }

        double ly = r.Top + lanesH + (r.Height - lanesH) * 0.25;
        ctx.DrawRectangle(Lyrics, null, new RoundedRect(new Rect(r.Center.X - r.Width * 0.3, ly, r.Width * 0.6, 4), 2));
        ctx.DrawRectangle(LyricsNext, null, new RoundedRect(new Rect(r.Center.X - r.Width * 0.2, ly + 9, r.Width * 0.4, 3), 1.5));
    }

    // The DJ window: sidebar, search row, song rows, preview player on the right.
    private static void Dj(DrawingContext ctx, Rect r)
    {
        ctx.DrawRectangle(DjBg, null, new RoundedRect(r, 3));
        ctx.DrawRectangle(DjSide, null, new Rect(r.Left, r.Top, r.Width * 0.06, r.Height));
        double right = r.Width * 0.27;
        Rect list = new(r.Left + r.Width * 0.08, r.Top + 5, r.Width - r.Width * 0.08 - right - 6, r.Height - 10);
        ctx.DrawRectangle(DjRow, null, new RoundedRect(new Rect(list.Left, list.Top, list.Width * 0.45, 6), 2));
        for (double y = list.Top + 13; y < list.Bottom - 3; y += 7)
        {
            ctx.DrawRectangle(DjRow, null, new Rect(list.Left, y, list.Width, 2));
        }

        Rect panel = new(r.Right - right, r.Top + 5, right - 5, r.Height - 10);
        ctx.DrawRectangle(DjVideo, null, new RoundedRect(new Rect(panel.Left, panel.Top, panel.Width, panel.Width * 0.6), 2));
        ctx.DrawEllipse(DjGo, null, new Point(panel.Center.X, panel.Top + panel.Width * 0.6 + 12), 6, 6);
        for (double y = panel.Top + panel.Width * 0.6 + 24; y < panel.Bottom - 2; y += 7)
        {
            ctx.DrawRectangle(DjRow, null, new Rect(panel.Left, y, panel.Width, 2));
        }
    }
}
