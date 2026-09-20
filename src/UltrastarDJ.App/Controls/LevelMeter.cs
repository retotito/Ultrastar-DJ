using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Horizontal level bar with an optional marker (e.g. the noise-gate position) on the same 0..1 scale.
/// Draws directly — it is fed at meter rate, which is too fast for templated controls.
/// </summary>
public sealed class LevelMeter : Control
{
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(Level));
    public static readonly StyledProperty<double> MarkProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(Mark), -1);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<LevelMeter, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<LevelMeter, IBrush?>(nameof(Track));
    public static readonly StyledProperty<IBrush?> MarkBrushProperty = AvaloniaProperty.Register<LevelMeter, IBrush?>(nameof(MarkBrush));

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(LevelProperty, MarkProperty, FillProperty, TrackProperty, MarkBrushProperty);
    }

    /// <summary>0..1.</summary>
    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    /// <summary>0..1, or negative for none.</summary>
    public double Mark { get => GetValue(MarkProperty); set => SetValue(MarkProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IBrush? MarkBrush { get => GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        Rect b = new(Bounds.Size);
        double r = b.Height / 2;
        context.DrawRectangle(Track ?? Brushes.Gray, null, new RoundedRect(b, r));

        double level = Math.Clamp(Level, 0, 1);
        if (level > 0)
        {
            context.DrawRectangle(Fill ?? Brushes.White, null, new RoundedRect(new Rect(0, 0, b.Width * level, b.Height), r));
        }

        double mark = Mark;
        if (mark >= 0 && mark <= 1)
        {
            double x = Math.Round(b.Width * mark);
            context.DrawRectangle(MarkBrush ?? Brushes.White, null, new Rect(x - 1, -2, 2, b.Height + 4));
        }
    }
}
