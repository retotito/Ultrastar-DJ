using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// The progress line at the bottom of a player picture (as on the beamer), seekable like a video player's: thin at
/// rest, thicker with a knob under the pointer; click or drag to jump. A drag only previews — the jump happens on
/// release, so dragging across a YouTube stream does not fire a seek per pixel.
/// </summary>
public sealed class SeekStrip : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SeekStrip, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<SeekStrip, IBrush?>(nameof(Track));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<SeekStrip, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> KnobProperty = AvaloniaProperty.Register<SeekStrip, IBrush?>(nameof(Knob));

    private const double LineH = 4;
    private const double HoverLineH = 8;
    private const double KnobD = 14;
    private const double StripH = 16;

    private bool _hover;
    private bool _dragging;
    private double _drag;

    static SeekStrip()
    {
        AffectsRender<SeekStrip>(ValueProperty, TrackProperty, FillProperty, KnobProperty);
        CursorProperty.OverrideDefaultValue<SeekStrip>(new Cursor(StandardCursorType.Hand));
    }

    /// <summary>Position 0..1.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Knob { get => GetValue(KnobProperty); set => SetValue(KnobProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(0, StripH);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        // Transparent fill: the whole strip (taller than the line) takes the pointer.
        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        bool big = _hover || _dragging;
        double lineH = big ? HoverLineH : LineH;
        double y = h - lineH;
        double v = Math.Clamp(_dragging ? _drag : Value, 0, 1);
        ctx.DrawRectangle(Track ?? Brushes.Gray, null, new Rect(0, y, w, lineH));
        ctx.DrawRectangle(Fill ?? Brushes.DodgerBlue, null, new Rect(0, y, w * v, lineH));
        if (big)
        {
            double x = Math.Clamp(w * v, KnobD / 2, w - KnobD / 2);
            ctx.DrawEllipse(Knob ?? Fill ?? Brushes.White, null, new Point(x, y + lineH / 2), KnobD / 2, KnobD / 2);
        }
    }

    private double At(PointerEventArgs e) => Bounds.Width <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _drag = At(e);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            _drag = At(e);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _drag = At(e);
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            Value = _drag;   // one seek, where the pointer was let go
            InvalidateVisual();
        }
    }
}
