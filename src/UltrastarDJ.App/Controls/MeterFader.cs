using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Volume fader with its level meter as the track (Tauri HorizontalFader): segments light up green → yellow → red,
/// the knob sits on top. Behaves like <see cref="Fader"/>: only the knob moves (a stray click on the track must not
/// jump a fader during a party), double-click resets to <see cref="ResetValue"/>, open hand / fist cursor; arrow keys
/// step. The meter's dB range is set per fader (<see cref="MinDb"/> … <see cref="ClipDb"/>).
/// With <see cref="ShowGate"/> it is a mic's Gain + Gate in one (Tauri PlayerCard): the range below
/// <see cref="GateDb"/> is shaded on the meter and a handle under the track drags the gate (double-click: <see cref="GateResetDb"/>).
/// Drawn directly and allocation-free: <see cref="Level"/> changes at meter rate.
/// </summary>
public sealed class MeterFader : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MeterFader, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ResetValueProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(ResetValue), 1);
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(Level));
    public static readonly StyledProperty<double> MinDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(MinDb), MeterScale.Music.MinDb);
    public static readonly StyledProperty<double> MaxDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(MaxDb), MeterScale.Music.MaxDb);
    public static readonly StyledProperty<double> WarnDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(WarnDb), MeterScale.Music.WarnDb);
    public static readonly StyledProperty<double> ClipDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(ClipDb), MeterScale.Music.ClipDb);
    public static readonly StyledProperty<int> SegmentsProperty = AvaloniaProperty.Register<MeterFader, int>(nameof(Segments), 30);
    public static readonly StyledProperty<Color> LowColorProperty = AvaloniaProperty.Register<MeterFader, Color>(nameof(LowColor), Colors.LimeGreen);
    public static readonly StyledProperty<Color> WarnColorProperty = AvaloniaProperty.Register<MeterFader, Color>(nameof(WarnColor), Colors.Gold);
    public static readonly StyledProperty<Color> ClipColorProperty = AvaloniaProperty.Register<MeterFader, Color>(nameof(ClipColor), Colors.Red);
    public static readonly StyledProperty<IBrush?> KnobProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(Knob));
    public static readonly StyledProperty<IBrush?> KnobBaseProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(KnobBase));
    public static readonly StyledProperty<IBrush?> KnobGripProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(KnobGrip));
    public static readonly StyledProperty<IBrush?> PeakBrushProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(PeakBrush));
    public static readonly StyledProperty<bool> ShowGateProperty = AvaloniaProperty.Register<MeterFader, bool>(nameof(ShowGate));
    public static readonly StyledProperty<double> GateDbProperty =
        AvaloniaProperty.Register<MeterFader, double>(nameof(GateDb), -50, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GateMinDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(GateMinDb), -70);
    public static readonly StyledProperty<double> GateMaxDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(GateMaxDb), -20);
    public static readonly StyledProperty<double> GateResetDbProperty = AvaloniaProperty.Register<MeterFader, double>(nameof(GateResetDb), -50);
    public static readonly StyledProperty<IBrush?> GateZoneProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(GateZone));
    public static readonly StyledProperty<IBrush?> GateHandleProperty = AvaloniaProperty.Register<MeterFader, IBrush?>(nameof(GateHandle));

    private const double KnobW = 12;
    private const double KnobH = 28;
    private const double BarH = 10;
    private const double Gap = 2;
    // The knob's strip; with a gate, the handle row below it.
    private const double StripH = KnobH + 4;
    private const double GateRowH = 14;
    private const double HandleD = 10;
    // Peak hold: stays 1 s, then falls back across the whole meter in 2 s.
    private const double PeakHoldSec = 1.0;
    private const double PeakFallPerSec = 0.5;

    private static readonly BoxShadows KnobShadow = new(new BoxShadow { OffsetY = 1, Blur = 4, Color = Color.FromArgb(0x80, 0, 0, 0) });
    private static readonly IPen HandleRim = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, 0.7), 2);

    private static readonly Cursor SizeWE = new(StandardCursorType.SizeWestEast);

    private enum Part { None, Knob, Gate }

    // Lit / unlit segment brushes, rebuilt only when a colour changes.
    private IImmutableBrush _low = null!, _warn = null!, _clip = null!, _lowDim = null!, _warnDim = null!, _clipDim = null!;
    private ImmutablePen _hoverRing = null!;

    private Part _drag;
    private Part _hover;
    private double _grabOffset;
    private double _peak01;
    private TimeSpan _peakAt;
    private TimeSpan _now;
    private TimeSpan _lastFrame;
    private bool _animating;
    private readonly Action<TimeSpan> _onFrame;

    static MeterFader()
    {
        AffectsRender<MeterFader>(ValueProperty, MinimumProperty, MaximumProperty, LevelProperty, MinDbProperty, MaxDbProperty,
            WarnDbProperty, ClipDbProperty, SegmentsProperty, KnobProperty, KnobBaseProperty, KnobGripProperty, PeakBrushProperty,
            ShowGateProperty, GateDbProperty, GateMinDbProperty, GateMaxDbProperty, GateZoneProperty, GateHandleProperty);
        AffectsMeasure<MeterFader>(ShowGateProperty);
        FocusableProperty.OverrideDefaultValue<MeterFader>(true);
    }

    public MeterFader()
    {
        _onFrame = OnFrame;
        MakeBrushes();
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Value restored by double-clicking the knob.</summary>
    public double ResetValue { get => GetValue(ResetValueProperty); set => SetValue(ResetValueProperty, value); }
    /// <summary>Linear amplitude (RMS, 1 = full scale) of what this fader lets through.</summary>
    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    /// <summary>Left end of the meter, dBFS.</summary>
    public double MinDb { get => GetValue(MinDbProperty); set => SetValue(MinDbProperty, value); }
    /// <summary>Right end of the meter, dBFS.</summary>
    public double MaxDb { get => GetValue(MaxDbProperty); set => SetValue(MaxDbProperty, value); }
    /// <summary>Segments from here on are yellow.</summary>
    public double WarnDb { get => GetValue(WarnDbProperty); set => SetValue(WarnDbProperty, value); }
    /// <summary>Segments from here on are red.</summary>
    public double ClipDb { get => GetValue(ClipDbProperty); set => SetValue(ClipDbProperty, value); }
    public int Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Color LowColor { get => GetValue(LowColorProperty); set => SetValue(LowColorProperty, value); }
    public Color WarnColor { get => GetValue(WarnColorProperty); set => SetValue(WarnColorProperty, value); }
    public Color ClipColor { get => GetValue(ClipColorProperty); set => SetValue(ClipColorProperty, value); }
    /// <summary>Knob colour, drawn over <see cref="KnobBase"/> (so a translucent accent looks as on the toggles).</summary>
    public IBrush? Knob { get => GetValue(KnobProperty); set => SetValue(KnobProperty, value); }
    public IBrush? KnobBase { get => GetValue(KnobBaseProperty); set => SetValue(KnobBaseProperty, value); }
    public IBrush? KnobGrip { get => GetValue(KnobGripProperty); set => SetValue(KnobGripProperty, value); }
    /// <summary>The peak-hold line.</summary>
    public IBrush? PeakBrush { get => GetValue(PeakBrushProperty); set => SetValue(PeakBrushProperty, value); }
    /// <summary>Gain + Gate mode: shaded range below the gate, drag handle under the track.</summary>
    public bool ShowGate { get => GetValue(ShowGateProperty); set => SetValue(ShowGateProperty, value); }
    /// <summary>Gate threshold in dBFS, on the meter's own scale.</summary>
    public double GateDb { get => GetValue(GateDbProperty); set => SetValue(GateDbProperty, value); }
    public double GateMinDb { get => GetValue(GateMinDbProperty); set => SetValue(GateMinDbProperty, value); }
    public double GateMaxDb { get => GetValue(GateMaxDbProperty); set => SetValue(GateMaxDbProperty, value); }
    /// <summary>Gate restored by double-clicking the handle.</summary>
    public double GateResetDb { get => GetValue(GateResetDbProperty); set => SetValue(GateResetDbProperty, value); }
    /// <summary>Shade over the meter below the gate (what counts as silence).</summary>
    public IBrush? GateZone { get => GetValue(GateZoneProperty); set => SetValue(GateZoneProperty, value); }
    public IBrush? GateHandle { get => GetValue(GateHandleProperty); set => SetValue(GateHandleProperty, value); }

    private MeterScale Scale => new(MinDb, MaxDb, WarnDb, ClipDb);

    protected override Size MeasureOverride(Size availableSize) => new(0, ShowGate ? StripH + GateRowH : StripH);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LowColorProperty || change.Property == WarnColorProperty || change.Property == ClipColorProperty || change.Property == KnobProperty)
        {
            MakeBrushes();
        }
        else if (change.Property == LevelProperty)
        {
            double f = Scale.Fraction(Level);
            if (f >= _peak01)
            {
                _peak01 = f;
                _peakAt = _now;
            }

            StartAnimating();
        }
    }

    private void MakeBrushes()
    {
        _low = new ImmutableSolidColorBrush(LowColor);
        _warn = new ImmutableSolidColorBrush(WarnColor);
        _clip = new ImmutableSolidColorBrush(ClipColor);
        // Unlit segments: the same colour, faint — the scale stays readable on light and dark surfaces.
        _lowDim = new ImmutableSolidColorBrush(LowColor, 0.2);
        _warnDim = new ImmutableSolidColorBrush(WarnColor, 0.2);
        _clipDim = new ImmutableSolidColorBrush(ClipColor, 0.2);
        Color ring = Knob is ISolidColorBrush k ? k.Color : Colors.DodgerBlue;
        _hoverRing = new ImmutablePen(new ImmutableSolidColorBrush(ring, 0.5), 2);
    }

    // The peak line falls back over time even when the level stops changing (pause, silence): a frame callback
    // only while it is above the level, none at rest.
    private void StartAnimating()
    {
        if (!_animating && TopLevel.GetTopLevel(this) is { } top)
        {
            _animating = true;
            top.RequestAnimationFrame(_onFrame);
        }
    }

    private void OnFrame(TimeSpan t)
    {
        double dt = _lastFrame == TimeSpan.Zero ? 0 : Math.Min((t - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = t;
        _now = t;
        _animating = false;
        double level = Scale.Fraction(Level);
        if ((t - _peakAt).TotalSeconds > PeakHoldSec)
        {
            _peak01 = Math.Max(level, _peak01 - PeakFallPerSec * dt);
        }

        InvalidateVisual();
        if (_peak01 > level + 0.001)
        {
            StartAnimating();
        }
        else
        {
            _lastFrame = TimeSpan.Zero;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= KnobW)
        {
            return;
        }

        // Transparent fill: the whole strip takes the pointer (hover, knob grab near the edges).
        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        MeterScale scale = Scale;
        double barY = (StripH - BarH) / 2;
        double left = KnobW / 2;
        double span = w - KnobW;

        // Meter: segments across the knob's travel, so the knob's centre points at the level it lets through.
        int n = Math.Max(4, Segments);
        double segW = (span - Gap * (n - 1)) / n;
        double lit = scale.Fraction(Level) * n;
        double warn = scale.WarnFraction * n;
        double clip = scale.ClipFraction * n;
        for (int i = 0; i < n; i++)
        {
            bool on = i < Math.Round(lit);
            IImmutableBrush brush = i >= Math.Round(clip) ? (on ? _clip : _clipDim)
                : i >= Math.Round(warn) ? (on ? _warn : _warnDim)
                : on ? _low : _lowDim;
            ctx.DrawRectangle(brush, null, new Rect(left + i * (segW + Gap), barY, segW, BarH));
        }

        if (_peak01 > 0.001)
        {
            double px = left + Math.Min(_peak01, 1) * span;
            ctx.DrawRectangle(PeakBrush ?? Brushes.White, null, new Rect(px - 1, barY - 2, 2, BarH + 4));
        }

        if (ShowGate)
        {
            // Below the gate = silence: shaded, and the handle on a thin line down from the meter.
            double gx = GateX();
            ctx.DrawRectangle(GateZone ?? Brushes.Transparent, null, new Rect(left, barY - 2, Math.Max(0, gx - left), BarH + 4));
            IBrush handle = GateHandle ?? Brushes.Red;
            ctx.DrawRectangle(handle, null, new Rect(gx - 0.5, barY + BarH, 1, StripH - barY - BarH + 1));
            double d = HandleD * (_drag == Part.Gate || _hover == Part.Gate ? 1.3 : 1);
            ctx.DrawEllipse(handle, HandleRim, new Point(gx, StripH + GateRowH / 2 - 1), d / 2, d / 2);
        }

        // Knob: base (opaque) + colour, so a translucent accent reads exactly as on the toggle switches.
        double range = Maximum - Minimum;
        double v = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        double grow = _drag == Part.Knob ? 1.1 : 1;
        double kw = KnobW * grow;
        double kh = KnobH * grow;
        RoundedRect knob = new(new Rect(left + v * span - kw / 2, (StripH - kh) / 2, kw, kh), 4);
        ctx.DrawRectangle(KnobBase ?? Brushes.White, null, knob, KnobShadow);
        ctx.DrawRectangle(Knob ?? Brushes.DodgerBlue, _hover == Part.Knob || _drag == Part.Knob ? _hoverRing : null, knob);
        ctx.DrawRectangle(KnobGrip ?? Brushes.Black, null, new Rect(knob.Rect.Center.X - 1, knob.Rect.Center.Y - 5, 2, 10));
    }

    private double KnobCentre()
    {
        double range = Maximum - Minimum;
        double v = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        return KnobW / 2 + v * (Bounds.Width - KnobW);
    }

    private double GateX() => KnobW / 2 + Scale.FractionOfDb(GateDb) * (Bounds.Width - KnobW);

    // The handle row is the gate's, the strip above is the knob's.
    private Part HitTest(Point p)
    {
        if (ShowGate && p.Y >= StripH - 2)
        {
            return Math.Abs(p.X - GateX()) <= HandleD ? Part.Gate : Part.None;
        }

        return Math.Abs(p.X - KnobCentre()) <= KnobW / 2 + 4 ? Part.Knob : Part.None;
    }

    private void SetHover(Part part)
    {
        if (part != _hover)
        {
            _hover = part;
            Cursor = part switch
            {
                Part.Knob => GrabCursors.Grab,
                Part.Gate => SizeWE,
                _ => Cursor.Default,
            };
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point p = e.GetPosition(this);
        Part part = HitTest(p);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || part == Part.None)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (part == Part.Gate)
            {
                GateDb = Math.Clamp(GateResetDb, GateMinDb, GateMaxDb);
            }
            else
            {
                Value = Math.Clamp(ResetValue, Minimum, Maximum);
            }

            e.Handled = true;
            return;
        }

        // Keep the grab point under the pointer: neither knob nor handle jumps to the click.
        _grabOffset = p.X - (part == Part.Gate ? GateX() : KnobCentre());
        _drag = part;
        if (part == Part.Knob)
        {
            Cursor = GrabCursors.Grabbing;
        }

        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);
        if (_drag != Part.None)
        {
            double span = Bounds.Width - KnobW;
            double f = span <= 0 ? 0 : Math.Clamp((p.X - _grabOffset - KnobW / 2) / span, 0, 1);
            if (_drag == Part.Gate)
            {
                double db = MinDb + f * (MaxDb - MinDb);
                GateDb = Math.Clamp(db, GateMinDb, GateMaxDb);
            }
            else
            {
                Value = Minimum + f * (Maximum - Minimum);
            }

            return;
        }

        SetHover(HitTest(p));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag != Part.None)
        {
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = Part.None;
        // Re-apply the hover cursor (the fist goes back to the open hand).
        Part hover = _hover;
        _hover = Part.None;
        SetHover(hover);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_drag == Part.None)
        {
            SetHover(Part.None);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = (Maximum - Minimum) / 50;
        double? to = e.Key switch
        {
            Key.Left or Key.Down => Value - step,
            Key.Right or Key.Up => Value + step,
            _ => null,
        };
        if (to is { } v)
        {
            Value = Math.Clamp(v, Minimum, Maximum);
            e.Handled = true;
        }
    }
}
