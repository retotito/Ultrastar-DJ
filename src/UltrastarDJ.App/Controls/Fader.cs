using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Slider for live levels (volume, gain, gate, offsets). Only the knob changes the value: a stray click on the
/// track must not jump a fader during a party. Open hand on the knob, fist while dragging, double-click resets
/// to <see cref="ResetValue"/>. Seek bars stay plain <see cref="Slider"/>s (click-to-jump is expected there).
/// </summary>
public sealed class Fader : Slider
{
    public static readonly StyledProperty<double> ResetValueProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(ResetValue), 1.0);

    private Thumb? _thumb;

    /// <summary>Value restored by double-clicking the knob.</summary>
    public double ResetValue
    {
        get => GetValue(ResetValueProperty);
        set => SetValue(ResetValueProperty, value);
    }

    // Same look as Slider: reuse its control theme.
    protected override Type StyleKeyOverride => typeof(Slider);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_thumb is not null)
        {
            _thumb.DragStarted -= OnDragStarted;
            _thumb.DragCompleted -= OnDragCompleted;
            _thumb.DoubleTapped -= OnDoubleTapped;
        }

        base.OnApplyTemplate(e);

        // Slider moves to the click through these two track buttons; without hit-testing the track is inert.
        foreach (string part in new[] { "PART_DecreaseButton", "PART_IncreaseButton" })
        {
            if (e.NameScope.Find<Button>(part) is { } button)
            {
                button.IsHitTestVisible = false;
            }
        }

        _thumb = Track?.Thumb;
        if (_thumb is not null)
        {
            _thumb.Cursor = GrabCursors.Grab;
            _thumb.DragStarted += OnDragStarted;
            _thumb.DragCompleted += OnDragCompleted;
            _thumb.DoubleTapped += OnDoubleTapped;
        }
    }

    private void OnDragStarted(object? sender, VectorEventArgs e) => _thumb!.Cursor = GrabCursors.Grabbing;

    private void OnDragCompleted(object? sender, VectorEventArgs e) => _thumb!.Cursor = GrabCursors.Grab;

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Value = Math.Clamp(ResetValue, Minimum, Maximum);
}
