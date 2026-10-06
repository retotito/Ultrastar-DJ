using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using UltrastarDJ.App.Game;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Score screen celebration: stars in the winner's colour twinkle all over the screen and drift slowly upwards,
/// fading in once the count-up has finished and out again after <see cref="DurationSec"/>. Redrawn per display frame
/// (RequestAnimationFrame) only while visible.
/// </summary>
public sealed class StarShower : Control
{
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<StarShower, bool>(nameof(IsRunning));
    public static readonly StyledProperty<IBrush?> StarBrushProperty = AvaloniaProperty.Register<StarShower, IBrush?>(nameof(StarBrush));

    private const int Count = 60;
    private const double FadeInSec = 0.8;
    private const double FadeOutSec = 1.0;
    public const double DurationSec = 5.0;
    private const double DriftPxPerSec = 18;

    private readonly Stopwatch _clock = new();
    private bool _animating;
    private bool _attached;

    static StarShower()
    {
        AffectsRender<StarShower>(StarBrushProperty);
        IsRunningProperty.Changed.AddClassHandler<StarShower>((c, _) => c.UpdateLoop());
    }

    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }

    /// <summary>The winner's colour: each star's halo; the core is white.</summary>
    public IBrush? StarBrush { get => GetValue(StarBrushProperty); set => SetValue(StarBrushProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdateLoop();
    }

    private void UpdateLoop()
    {
        bool run = IsRunning && _attached;
        if (run && !_animating)
        {
            _animating = true;
            _clock.Restart();
            RequestFrame();
        }
        else if (!run)
        {
            _animating = false;
            InvalidateVisual();
        }
    }

    private void RequestFrame()
        => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
        {
            if (!_animating)
            {
                return;
            }

            InvalidateVisual();
            if (_clock.Elapsed.TotalSeconds < DurationSec)
            {
                RequestFrame();
            }
            else
            {
                // Done: the last frame (drawn now) is empty; no more redraws until the next score screen.
                _animating = false;
            }
        });

    public override void Render(DrawingContext context)
    {
        if (!_animating || StarBrush is not { } halo || Bounds.Width <= 0)
        {
            return;
        }

        double t = _clock.Elapsed.TotalSeconds;
        if (t >= DurationSec)
        {
            return;
        }

        double fade = Math.Min(1, Math.Min(t / FadeInSec, (DurationSec - t) / FadeOutSec));
        double w = Bounds.Width;
        double h = Bounds.Height;
        double baseSize = Math.Clamp(h * 0.012, 6, 16);
        for (int i = 0; i < Count; i++)
        {
            // Stable per-star position, size, speed and phase; the drift wraps from the top back to the bottom.
            double x = ScoreEffects.Hash(i * 3 + 1) * w;
            double speed = DriftPxPerSec * (0.5 + ScoreEffects.Hash(i * 5 + 2));
            double y = ((ScoreEffects.Hash(i * 7 + 3) * h - speed * t) % h + h) % h;
            double size = baseSize * (0.5 + ScoreEffects.Hash(i * 11 + 4));
            double alpha = fade * ScoreEffects.Twinkle(t * (0.6 + 0.6 * ScoreEffects.Hash(i * 13 + 5)), ScoreEffects.Hash(i * 17 + 6));
            ScoreEffects.DrawStar(context, new Point(x, y), size, alpha, halo, ScoreEffects.StarCore);
        }
    }
}
