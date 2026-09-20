using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Which sidebar panel is open. Exactly one (or none) at a time.</summary>
public enum SidebarPanel
{
    None,
    Layout,
    Sources,
    AudioInput,
    AudioOutput,
    Displays,
    Settings,
    MediaLab,
}

public sealed partial class DjWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private SidebarPanel _activePanel = SidebarPanel.None;

    [ObservableProperty]
    private ViewModelBase? _panelContent;

    public DjWindowViewModel(IServiceProvider services)
    {
        _services = services;
    }

    public static string Version => typeof(DjWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";

    [RelayCommand]
    private void TogglePanel(SidebarPanel panel)
    {
        if (ActivePanel == panel)
        {
            ClosePanel();
            return;
        }

        ActivePanel = panel;
        PanelContent = panel switch
        {
            SidebarPanel.Displays => _services.GetRequiredService<DisplaysPanelViewModel>(),
            SidebarPanel.MediaLab => _services.GetRequiredService<MediaLabPanelViewModel>(),
            _ => new PlaceholderPanelViewModel(panel.ToString()),
        };
    }

    [RelayCommand]
    private void ClosePanel()
    {
        ActivePanel = SidebarPanel.None;
        PanelContent = null;
    }
}
