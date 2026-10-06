using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    }

    private Task LoadAsync() => DataContext is LibraryViewModel vm ? vm.PreviewSelectedAsync() : Task.CompletedTask;
}
