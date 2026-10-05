using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using UltrastarDJ.App.Controls;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

/// <summary>Floating card; dragging the header moves it within the window. Position lives in the view model.</summary>
public sealed partial class NowPlayingView : UserControl
{
    private const double EdgeMargin = 16;
    private bool _dragging;
    private Point _pointerStart;
    private double _cardStartX;
    private double _cardStartY;

    public NowPlayingView()
    {
        InitializeComponent();
        DragHandle.Cursor = GrabCursors.Grab;
        DragHandle.PointerPressed += OnHandlePressed;
        DragHandle.PointerMoved += OnHandleMoved;
        DragHandle.PointerReleased += OnHandleReleased;
        DragHandle.PointerCaptureLost += (_, _) => EndDrag();
        // The card can grow (mix rows appear), and a saved position may come from a bigger window.
        SizeChanged += (_, _) =>
        {
            PlaceIfUnset();
            KeepInsideWindow();
        };
    }

    // The window shrinking must not push the card out of reach: pull it back in on every resize.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.SizeChanged += OnWindowSizeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.SizeChanged -= OnWindowSizeChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => KeepInsideWindow();

    /// <summary>Clamps the card into the window. Not persisted: the saved position is only what the DJ dragged to.</summary>
    private void KeepInsideWindow()
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this) is { } top && Bounds.Width > 0)
        {
            (vm.CardX, vm.CardY) = Clamp(vm.CardX, vm.CardY, top);
        }
    }

    private (double X, double Y) Clamp(double x, double y, TopLevel top) => (
        Math.Clamp(x, 0, Math.Max(0, top.ClientSize.Width - Bounds.Width)),
        Math.Clamp(y, 0, Math.Max(0, top.ClientSize.Height - Bounds.Height)));

    private NowPlayingViewModel? Vm => DataContext as NowPlayingViewModel;

    private void PlaceIfUnset()
    {
        if (Vm is { HasSavedPosition: false } vm && TopLevel.GetTopLevel(this) is { } top && Bounds.Width > 0)
        {
            // Default: top-right of the centre area, next to the preview/queue panel.
            double rightPanel = this.FindResource("RightPanelWidth") is double w ? w : 0;
            vm.CardX = Math.Max(0, top.ClientSize.Width - rightPanel - Bounds.Width - EdgeMargin);
            vm.CardY = EdgeMargin;
        }
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _dragging = true;
        _pointerStart = e.GetPosition(top);
        _cardStartX = vm.CardX;
        _cardStartY = vm.CardY;
        e.Pointer.Capture(DragHandle);
        DragHandle.Cursor = GrabCursors.Grabbing;
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || Vm is not { } vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        Point p = e.GetPosition(top);
        (vm.CardX, vm.CardY) = Clamp(_cardStartX + p.X - _pointerStart.X, _cardStartY + p.Y - _pointerStart.Y, top);
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            e.Pointer.Capture(null);
            EndDrag();
        }
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        DragHandle.Cursor = GrabCursors.Grab;
        Vm?.SavePosition();
    }
}
