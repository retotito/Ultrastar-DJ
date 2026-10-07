using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Loading indicator in the middle of a player picture: a three-quarter ring turning once per 0.9 s.
/// Drawn and turned here (one frame callback per frame, only while visible) — a style animation on
/// RenderTransform has no animator in Avalonia and threw at startup.
/// </summary>
public sealed class Spinner : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Spinner, IBrush?>(nameof(Stroke));

    private const double TurnSec = 0.9;
    private const double Thickness = 4;

    private readonly Action<TimeSpan> _onFrame;
    private StreamGeometry? _ring;
    private IPen? _pen;
    private Size _ringFor;
    private double _angle;
    private bool _animating;

    static Spinner() => AffectsRender<Spinner>(StrokeProperty);

    public Spinner() => _onFrame = OnFrame;

    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StrokeProperty)
        {
            _pen = null;
        }
        else if (change.Property == IsVisibleProperty && IsVisible)
        {
            Animate();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    private void Animate()
    {
        if (!_animating && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is { } top)
        {
            _animating = true;
            top.RequestAnimationFrame(_onFrame);
        }
    }

    private void OnFrame(TimeSpan t)
    {
        _animating = false;
        if (!IsEffectivelyVisible)
        {
            return;   // stops here; becoming visible starts it again
        }

        _angle = t.TotalSeconds / TurnSec % 1 * 360;
        InvalidateVisual();
        Animate();
    }

    public override void Render(DrawingContext ctx)
    {
        Size size = Bounds.Size;
        double r = Math.Min(size.Width, size.Height) / 2 - Thickness;
        if (r <= 0)
        {
            return;
        }

        if (_ring is null || _ringFor != size)
        {
            // Built once per size: the frame only turns it.
            _ring = new StreamGeometry();
            using (StreamGeometryContext g = _ring.Open())
            {
                g.BeginFigure(new Point(r, 0), false);
                g.ArcTo(new Point(0, -r), new Size(r, r), 0, true, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            _ringFor = size;
        }

        _pen ??= new Pen(Stroke ?? Brushes.White, Thickness, lineCap: PenLineCap.Round);
        Matrix turn = Matrix.CreateRotation(_angle * Math.PI / 180) * Matrix.CreateTranslation(size.Width / 2, size.Height / 2);
        using (ctx.PushTransform(turn))
        {
            ctx.DrawGeometry(null, _pen, _ring);
        }
    }
}
