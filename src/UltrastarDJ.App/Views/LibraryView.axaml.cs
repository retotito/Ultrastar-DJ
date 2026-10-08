using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

public sealed partial class LibraryView : UserControl
{
    private LibraryViewModel? _vm;

    public LibraryView()
    {
        InitializeComponent();
        SongList.DoubleTapped += async (_, _) => await LoadAsync();
        SongList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await LoadAsync();
            }
        };
        TableScroll.SizeChanged += (_, _) => FitTable();
        // Tunnel: the song list's own ScrollViewer would take every wheel / trackpad event (it scrolls vertically)
        // and the table's horizontal scrolling would never see a sideways swipe.
        TableScroll.AddHandler(PointerWheelChangedEvent, OnTableWheel, RoutingStrategies.Tunnel);
    }

    // Pixels per wheel step, as Avalonia's ScrollViewer uses for a line.
    private const double WheelStepPx = 50;

    /// <summary>Sideways swipes and Shift + wheel scroll the table horizontally; up / down stays with the list.</summary>
    private void OnTableWheel(object? sender, PointerWheelEventArgs e)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double dx = shift && e.Delta.X == 0 ? e.Delta.Y : e.Delta.X;
        if (dx == 0 || (!shift && Math.Abs(e.Delta.X) < Math.Abs(e.Delta.Y)))
        {
            return;
        }

        double max = Math.Max(0, TableScroll.Extent.Width - TableScroll.Viewport.Width);
        if (max <= 0)
        {
            return;
        }

        TableScroll.Offset = TableScroll.Offset.WithX(Math.Clamp(TableScroll.Offset.X - dx * WheelStepPx, 0, max));
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Table.Changed -= FitTable;
        }

        _vm = DataContext as LibraryViewModel;
        if (_vm is not null)
        {
            _vm.Table.Changed += FitTable;
        }

        FitTable();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_vm is not null)
        {
            _vm.Table.Changed -= FitTable;
            _vm.Table.Changed += FitTable;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
        {
            _vm.Table.Changed -= FitTable;
        }
    }

    // A horizontally scrolling ScrollViewer measures its content with infinite width, so star columns would size
    // to whatever rows happen to be realised. The table gets an explicit width: the window, or its minimum when
    // the visible columns need more (then it scrolls).
    private void FitTable()
    {
        if (_vm is null)
        {
            return;
        }

        TableGrid.Width = Math.Max(TableScroll.Bounds.Width, _vm.Table.MinWidth);
        _vm.Table.Fit(TableGrid.Width);
    }

    private Task LoadAsync() => DataContext is LibraryViewModel vm ? vm.PreviewSelectedAsync() : Task.CompletedTask;
}
