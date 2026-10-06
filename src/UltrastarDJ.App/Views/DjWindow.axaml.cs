using Avalonia;
using Avalonia.Controls;
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
        // Drop-down lists and menus opened from a panel are drawn outside it, but stay its logical descendants.
        // Template parts have no logical parent, so walk the logical chain from every element on the visual path.
        bool inPopover = path.Any(v => ReferenceEquals(v, Popover))
            || path.OfType<ILogical>().Any(l => l.GetSelfAndLogicalAncestors().Any(a => ReferenceEquals(a, Popover)));
        bool onSidebarButton = path.OfType<Button>().Any() && path.Any(v => ReferenceEquals(v, Sidebar));
        if (!inPopover && !onSidebarButton)
        {
            vm.ClosePanelCommand.Execute(null);
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
