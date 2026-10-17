using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

public sealed partial class DjWindow : Window
{
    public DjWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDownAnywhere, RoutingStrategies.Tunnel);
        PointerPressed += OnTitleAreaPressed;
    }

    // ── Title bar: the content reaches the window's top edge (ExtendClientAreaToDecorationsHint) ──
    // macOS draws its three buttons over the sidebar's top (its icons start below the top row, SidebarPad). Windows
    // puts its buttons top right over the preview header; Linux desktops that cannot do it keep their own title bar.

    /// <summary>The strip along the top that moves the window: the search and filter row (TopBarHeight).</summary>
    private const double DragStripHeight = 57;
    // A press on an empty spot of the top row (the bars marked "titlebar", the sidebar's top) drags the window, a
    // double-click maximises or restores it — as the title bar did. Buttons, the search field and drop-downs handle
    // their presses first, so they never get here; popovers and cards reaching up there are not part of the row.
    private void OnTitleAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExtendedIntoWindowDecorations || e.GetPosition(this).Y > DragStripHeight || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || !InTitleRow(e.Source as Visual))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    private bool InTitleRow(Visual? source)
        => source?.GetSelfAndVisualAncestors().Any(v => ReferenceEquals(v, Sidebar) || (v is StyledElement { Classes: var c } && c.Contains("titlebar"))) == true;

    // Esc closes the Details popup before anything else sees the key.
    private void OnKeyDownAnywhere(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DjWindowViewModel { DuetSingers: not null } duet)
        {
            duet.CloseDuetSingers(ok: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is DjWindowViewModel { SongDetails: not null } vm)
        {
            vm.SongDetails = null;
            e.Handled = true;
        }
    }

    // The duet pick: a press on the dimmed area keeps the current pick (the first two set up, or the last choice).
    private void OnDuetBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, DuetLayer) && DataContext is DjWindowViewModel vm)
        {
            vm.CloseDuetSingers(ok: false);
        }
    }

    // Only a press on the dimmed backdrop itself closes; presses inside the popup bubble up with another source.
    private void OnDetailsBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, DetailsLayer) && DataContext is DjWindowViewModel vm)
        {
            vm.SongDetails = null;
        }
    }

    /// <summary>
    /// Light dismiss for the sidebar popover. Sidebar buttons are excluded so they keep toggling/switching
    /// panels themselves; the empty sidebar area closes like anywhere else. Presses in a drop-down or menu opened from
    /// the panel count as inside. The press is not consumed: clicking a song row closes the panel and selects the row.
    /// The Now Playing card is not a popover and stays open until its own close button.
    /// </summary>
    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DjWindowViewModel { PanelContent: not null } vm || e.Source is not Visual source)
        {
            return;
        }

        List<Visual> path = source.GetSelfAndVisualAncestors().ToList();
        // A press in a drop-down list or menu is never "outside": it is a pick (or a scroll) in what the panel opened.
        // Such lists live in a popup (their own window, or the overlay layer) — no need to trace them back to the panel.
        if (path.Any(v => v is PopupRoot or OverlayPopupHost) || !ReferenceEquals(TopLevel.GetTopLevel(source), this))
        {
            return;
        }

        // While a drop-down list is open, Avalonia lays an invisible layer over the whole window: the next press lands
        // there (not on what is under the pointer) and only closes the list. It must not close the panel too — the
        // pointer may well be over the panel (log: "LightDismissOverlayLayer < VisualLayerManager < Panel < DjWindow").
        // (The layer type is internal to Avalonia: matched by name.)
        if (path.Any(v => v.GetType().Name == "LightDismissOverlayLayer"))
        {
            return;
        }

        // Template parts have no logical parent, so walk the logical chain from every element on the visual path.
        bool inPopover = path.Any(v => ReferenceEquals(v, Popover))
            || path.OfType<ILogical>().Any(l => l.GetSelfAndLogicalAncestors().Any(a => ReferenceEquals(a, Popover)));
        bool onSidebarButton = path.OfType<Button>().Any() && path.Any(v => ReferenceEquals(v, Sidebar));
        if (!inPopover && !onSidebarButton)
        {
            vm.CloseOnOutsidePress(string.Join(" < ", path.Take(4).Select(v => v.GetType().Name)));
        }
    }

    // Clipboard and file manager are view concerns: the bug dialog's two extra buttons live here.
    private async void OnCopyDialogDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DjWindowViewModel { Dialog.Details: { } details } && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(details);
        }
    }

    private async void OnOpenLogFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DjWindowViewModel vm)
        {
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(vm.LogsFolder));
        }
    }
}
