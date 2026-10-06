using Avalonia;
using Avalonia.Media;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.App.Game;

/// <summary>
/// The beamer's scoring effects after UltraStar Deluxe (UGraphicClasses / USingScores), drawn by
/// <see cref="Controls.GameOverlayControl"/>: twinkling stars on perfect notes, sparkles on sung golden notes,
/// the phrase rating popup (great / awesome / perfect) and the star burst for a 100 % phrase.
/// Every frame, render thread: nothing here allocates — stars are one shared geometry, particles a fixed table.
/// </summary>
public sealed class ScoreEffects
{
    // USDX particles live 16 frames at its ~30 Hz effect rate: one twinkle ≈ 0.53 s.
    private const double TwinkleSec = 0.53;

    // USDX popup phases (USingScores): pop up 350 ms, rise 550 ms, fade 200 ms.
    private const double PopSec = 0.35;
    private const double RiseSec = 0.55;
    private const double FadeSec = 0.2;
    public const double PopupSec = PopSec + RiseSec + FadeSec;

    private const int BurstCount = 48;
    private const double BurstSec = 0.7;

    private static readonly Color StarWhite = Color.FromRgb(255, 255, 242);
    public static readonly IBrush StarCore = new SolidColorBrush(StarWhite);
    private static readonly IBrush StarHalo = new SolidColorBrush(StarWhite, 0.35);
    private static readonly IBrush GoldHalo = new SolidColorBrush(Color.FromRgb(255, 180, 25), 0.55);
    private static readonly IBrush GoldCore = new SolidColorBrush(Color.FromRgb(255, 255, 160));
    private static readonly IBrush TipCore = new SolidColorBrush(Color.FromRgb(230, 240, 255));
    private static readonly IBrush PerfectGlow = new SolidColorBrush(Color.FromRgb(255, 215, 0), 0.3);
    private static readonly IBrush WhiteGlow = new SolidColorBrush(Colors.White, 0.2);
    private static readonly (double, double)[] GlowOffsets = [(-3, 0), (3, 0), (0, -3), (0, 3), (-2, -2), (2, 2), (-2, 2), (2, -2)];

    // A four-pointed sparkle of radius 1 around (0, 0); scaled and moved per star.
    private static readonly Geometry Star = BuildStar();

    // Star burst particles: start (0..1 of the lane), velocity (px/s), delay, life, size — fixed so a burst looks
    // lively without a Random per frame.
    private static readonly (double X, double Y, double Vx, double Vy, double Delay, double Life, double Size)[] Burst = BuildBurst();

    // Perfect-note stars, around the bar's top-right corner: offset from the corner (px) and phase.
    private static readonly (double Dx, double Dy, double Scale, double Phase)[] NoteStars =
        [(-4, -2, 1.0, 0.0), (-15, 3, 0.7, 0.35), (2, 7, 0.55, 0.7)];

    // One text per (word, size, colour), built once. Never SetForegroundBrush while drawing: it makes Avalonia lay the
    // text out again, and with a weight the font lacks it built a new synthetic font face each time that HarfBuzz
    // keeps alive — 55 per second, until macOS killed the app at 23 GB.
    private readonly Dictionary<(string, double, IBrush), FormattedText> _texts = [];
    // Inter has Bold (Black does not exist and was synthesised from the system font).
    private static readonly Typeface RatingFace = new(FontFamily.Parse("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold);

    /// <summary>
    /// USDX perfect note: three off-white stars twinkle at the note's top-right corner (left of an R badge) from the
    /// moment it was sung to the end until the phrase leaves the screen.
    /// </summary>
    public static void DrawNoteStars(DrawingContext ctx, Rect bar, double pos, int seed, bool hasBadge)
    {
        double size = Math.Clamp(bar.Height * 0.35, 6, 14);
        double right = bar.Right - (hasBadge ? 20 : 0);
        foreach ((double dx, double dy, double scale, double phase) in NoteStars)
        {
            double a = Twinkle(pos, phase + seed * 0.13);
            DrawStar(ctx, new Point(right + dx, bar.Top + dy), size * scale, a, StarHalo, StarCore);
        }
    }

    /// <summary>
    /// USDX golden sparkles: on the sung part of a golden note, about one every 12 px (+1), twinkling in gold;
    /// <paramref name="tipX"/> (the fill edge while it is being hit) gets a brighter white-blue twinkle.
    /// </summary>
    public static void DrawGoldenSparkles(DrawingContext ctx, Rect bar, double fromX, double toX, double pos, int seed, double? tipX)
    {
        double width = toX - fromX;
        int n = (int)(width / 12) + 1;
        double size = Math.Clamp(bar.Height * 0.22, 4, 10);
        for (int k = 0; k < n; k++)
        {
            double h = Hash(seed * 31 + k);
            double x = fromX + (k + 0.5) * width / n;
            double y = bar.Top + bar.Height * (0.15 + 0.7 * Hash(seed * 17 + k * 7 + 3));
            DrawStar(ctx, new Point(x, y), size * (0.6 + 0.4 * h), Twinkle(pos, h), GoldHalo, GoldCore);
        }

        if (tipX is { } tip)
        {
            // Fast flicker at the fill edge (USDX GoldenNoteTwinkle: short-lived, white with a blue tint).
            double a = 0.6 + 0.4 * Math.Abs(Math.Sin(pos * 23));
            DrawStar(ctx, new Point(tip, bar.Center.Y), size * 1.5, a, StarHalo, TipCore);
        }
    }

    /// <summary>
    /// Phrase rating, centred in the lane (USDX popup timing): pops up from nothing, rises with ease-in while fading
    /// to 70 %, then fades out. Gold glow for PERFECT!, the player's colour for AWESOME!, white for GREAT!.
    /// </summary>
    public void DrawRating(DrawingContext ctx, Rect lane, PhraseResult r, double pos, IBrush playerGlow)
    {
        double t = pos - r.AtSec;
        if (t < 0 || t >= PopupSec || PhraseRating.Text(r.Rating) is not { } text)
        {
            return;
        }

        double rise = lane.Height * 0.12;
        (double Scale, double Up, double Alpha) phase = t < PopSec
            ? (Math.Sin(t / PopSec * Math.PI / 2), 0.0, 1.0)
            : t < PopSec + RiseSec
                ? (1.0, Sqr((t - PopSec) / RiseSec), 1 - 0.3 * (t - PopSec) / RiseSec)
                : (1.0, 1.0, 0.7 - 0.7 * (t - PopSec - RiseSec) / FadeSec);
        (double scale, double up, double alpha) = phase;

        double size = Math.Max(20, lane.Height * 0.11);
        FormattedText ft = Text(text, size, Brushes.White);
        Point center = new(lane.Center.X, lane.Center.Y + rise / 2 - rise * up);
        Point at = new(center.X - ft.Width / 2, center.Y - ft.Height / 2);
        IBrush glow = r.Rating == PhraseRating.MaxRating ? PerfectGlow : r.Rating == 7 ? playerGlow : WhiteGlow;
        FormattedText glowText = Text(text, size, glow);
        using (ctx.PushOpacity(Math.Clamp(alpha, 0, 1)))
        using (ctx.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
        {
            foreach ((double dx, double dy) in GlowOffsets)
            {
                ctx.DrawText(glowText, at + new Point(dx, dy));
            }

            ctx.DrawText(ft, at);
        }
    }

    /// <summary>USDX PerfectLineTwinkle: stars in the player's colour burst over the lane and drift apart.</summary>
    public static void DrawBurst(DrawingContext ctx, Rect lane, PhraseResult r, double pos, IBrush playerHalo)
    {
        double t0 = pos - r.AtSec;
        if (!r.Full || t0 < 0 || t0 >= BurstSec)
        {
            return;
        }

        foreach ((double x, double y, double vx, double vy, double delay, double life, double size) in Burst)
        {
            double t = t0 - delay;
            if (t < 0 || t >= life)
            {
                continue;
            }

            Point p = new(lane.X + x * lane.Width + vx * t, lane.Y + y * lane.Height + vy * t);
            DrawStar(ctx, p, size, 1 - t / life, playerHalo, StarCore);
        }
    }

    private FormattedText Text(string text, double size, IBrush brush)
    {
        if (!_texts.TryGetValue((text, size, brush), out FormattedText? ft))
        {
            ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, RatingFace, size, brush);
            _texts[(text, size, brush)] = ft;
        }

        return ft;
    }

    /// <summary>A star with a soft halo: halo at double size, core at full size.</summary>
    public static void DrawStar(DrawingContext ctx, Point at, double radius, double alpha, IBrush halo, IBrush core)
    {
        if (alpha <= 0.02)
        {
            return;
        }

        using (ctx.PushOpacity(alpha))
        {
            using (ctx.PushTransform(Matrix.CreateScale(radius * 1.9, radius * 1.9) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                ctx.DrawGeometry(halo, null, Star);
            }

            using (ctx.PushTransform(Matrix.CreateScale(radius, radius) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                ctx.DrawGeometry(core, null, Star);
            }
        }
    }

    /// <summary>USDX alpha curve (−cos + 1) / 2 over one twinkle, repeating; <paramref name="phase"/> in twinkles.</summary>
    public static double Twinkle(double pos, double phase) => (1 - Math.Cos(2 * Math.PI * (pos / TwinkleSec + phase))) / 2;

    private static double Sqr(double v) => v * v;

    /// <summary>Stable pseudo-random 0..1 per integer (positions that must not jump between frames).</summary>
    public static double Hash(int i)
    {
        uint h = (uint)i * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFF) / 65535.0;
    }

    private static Geometry BuildStar()
    {
        StreamGeometry g = new();
        using (StreamGeometryContext c = g.Open())
        {
            // Long points up/down/left/right, pinched waist (0.22) — reads as a sparkle, not a cross.
            const double w = 0.22;
            c.BeginFigure(new Point(0, -1), true);
            c.LineTo(new Point(w, -w));
            c.LineTo(new Point(1, 0));
            c.LineTo(new Point(w, w));
            c.LineTo(new Point(0, 1));
            c.LineTo(new Point(-w, w));
            c.LineTo(new Point(-1, 0));
            c.LineTo(new Point(-w, -w));
            c.EndFigure(true);
        }

        return g;
    }

    private static (double, double, double, double, double, double, double)[] BuildBurst()
    {
        Random rnd = new(11);
        (double, double, double, double, double, double, double)[] list = new (double, double, double, double, double, double, double)[BurstCount];
        for (int i = 0; i < BurstCount; i++)
        {
            double angle = rnd.NextDouble() * 2 * Math.PI;
            double speed = 60 + rnd.NextDouble() * 150;
            list[i] = (0.05 + rnd.NextDouble() * 0.9, 0.15 + rnd.NextDouble() * 0.7, Math.Cos(angle) * speed, Math.Sin(angle) * speed,
                rnd.NextDouble() * 0.15, 0.27 + rnd.NextDouble() * 0.27, 5 + rnd.NextDouble() * 6);
        }

        return list;
    }
}
