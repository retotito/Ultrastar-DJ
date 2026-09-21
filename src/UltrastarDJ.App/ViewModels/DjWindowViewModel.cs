using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Playback;

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
}

public sealed partial class DjWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly PlaybackService _playback;

    [ObservableProperty]
    private SidebarPanel _activePanel = SidebarPanel.None;

    [ObservableProperty]
    private ViewModelBase? _panelContent;

    /// <summary>Audio/display configuration is locked while a song is active (prototype rule).</summary>
    [ObservableProperty]
    private bool _audioLocked;

    [ObservableProperty]
    private DialogMessage? _dialog;

    public DjWindowViewModel(IServiceProvider services, LibraryViewModel library, NowPlayingViewModel nowPlaying, PreviewViewModel preview, QueueViewModel queue,
        NotificationService notifications, PlaybackService playback)
    {
        _services = services;
        _playback = playback;
        Library = library;
        NowPlaying = nowPlaying;
        Preview = preview;
        Queue = queue;
        Notifications = notifications;
        notifications.DialogChanged += d => Dialog = d;
        playback.StateChanged += _ => UpdateLock();
        UpdateLock();
    }

    public LibraryViewModel Library { get; }
    public NowPlayingViewModel NowPlaying { get; }
    public PreviewViewModel Preview { get; }
    public QueueViewModel Queue { get; }
    public NotificationService Notifications { get; }

    private void UpdateLock()
    {
        AudioLocked = _playback.State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused;
        if (AudioLocked && ActivePanel is SidebarPanel.AudioInput or SidebarPanel.AudioOutput or SidebarPanel.Displays)
        {
            ClosePanel();
        }
    }

    [RelayCommand]
    private void DismissDialog() => Notifications.DismissDialog();

    [RelayCommand]
    private void ToggleNowPlaying() => NowPlaying.ToggleVisible();

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
            SidebarPanel.Sources => _services.GetRequiredService<SourcesPanelViewModel>(),
            SidebarPanel.AudioInput => _services.GetRequiredService<PlayersPanelViewModel>(),
            SidebarPanel.AudioOutput => _services.GetRequiredService<AudioOutputPanelViewModel>(),
            SidebarPanel.Settings => _services.GetRequiredService<SettingsPanelViewModel>(),
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
