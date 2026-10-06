using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using UltrastarDJ.App.Services;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Beamer side of Test sync: a big disc that lights when the current click should be heard. Polled every display
/// frame (RequestAnimationFrame, like the game overlay), so it goes through the same render path — and the same
/// projector delay — as the lyrics it calibrates.
/// </summary>
public sealed class SyncFlash : Control
{
    public static readonly StyledProperty<SyncTestService?> SourceProperty = AvaloniaProperty.Register<SyncFlash, SyncTestService?>(nameof(Source));
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<SyncFlash, bool>(nameof(IsRunning));
    public static readonly StyledProperty<IBrush?> FlashBrushProperty = AvaloniaProperty.Register<SyncFlash, IBrush?>(nameof(FlashBrush));

    private bool _animating;
    private bool _attached;
    private bool _on;

    static SyncFlash()
    {
        AffectsRender<SyncFlash>(FlashBrushProperty);
        IsRunningProperty.Changed.AddClassHandler<SyncFlash>((c, _) => c.UpdateLoop());
    }

    public SyncTestService? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public IBrush? FlashBrush { get => GetValue(FlashBrushProperty); set => SetValue(FlashBrushProperty, value); }

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
        bool run = IsRunning && _attached && Source is not null;
        if (run && !_animating)
        {
            _animating = true;
            RequestFrame();
        }
        else if (!run)
        {
            _animating = false;
            _on = false;
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

            bool on = Source?.IsFlashOn() ?? false;
            if (on != _on)
            {
                _on = on;
                InvalidateVisual();
            }

            RequestFrame();
        });

    public override void Render(DrawingContext context)
    {
        if (_on)
        {
            double r = Math.Min(Bounds.Width, Bounds.Height) * 0.22;
            context.DrawEllipse(FlashBrush ?? Brushes.White, null, Bounds.Center - Bounds.Position, r, r);
        }
    }
}
