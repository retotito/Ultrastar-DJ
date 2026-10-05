using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// panels themselves; the empty sidebar area closes like anywhere else. The press is not consumed: clicking a song row closes the panel and selects the row.
    /// The Now Playing card is not a popover and stays open until its own close button.
    /// </summary>
    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not DjWindowViewModel { PanelContent: not null } vm || e.Source is not Visual source)
        {
            return;
        }

        List<Visual> path = source.GetSelfAndVisualAncestors().ToList();
        bool inPopover = path.Any(v => ReferenceEquals(v, Popover));
        bool onSidebarButton = path.OfType<Button>().Any() && path.Any(v => ReferenceEquals(v, Sidebar));
        if (!inPopover && !onSidebarButton)
        {
            vm.ClosePanelCommand.Execute(null);
        }
    }
}
