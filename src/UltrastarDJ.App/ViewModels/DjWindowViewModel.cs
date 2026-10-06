using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UltrastarDJ.App.Services;
using UltrastarDJ.Infrastructure;
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
    Songbook,
    Settings,
}

public sealed partial class DjWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly PlaybackService _playback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelTitle), nameof(PanelGlyph), nameof(PanelAtBottom))]
    private SidebarPanel _activePanel = SidebarPanel.None;

    /// <summary>Shown in the popover header, next to its close button.</summary>
    public string PanelTitle => ActivePanel switch
    {
        SidebarPanel.Sources => "Song Sources",
        SidebarPanel.AudioInput => "Audio Input",
        SidebarPanel.AudioOutput => "Audio Output",
        _ => ActivePanel.ToString(),
    };

    /// <summary>Panels opened from the bottom of the sidebar (Settings) anchor to the window's bottom edge.</summary>
    public bool PanelAtBottom => ActivePanel == SidebarPanel.Settings;

    public string PanelGlyph => ActivePanel switch
    {
        SidebarPanel.Layout => "table_chart",
        SidebarPanel.Sources => "library_music",
        SidebarPanel.AudioInput => "mic_external_on",
        SidebarPanel.AudioOutput => "speaker",
        SidebarPanel.Displays => "tv",
        SidebarPanel.Songbook => "phone_iphone",
        SidebarPanel.Settings => "settings",
        _ => "",
    };

    [ObservableProperty]
    private ViewModelBase? _panelContent;

    // However the Audio Input panel closes, its mic tests end with it.
    partial void OnPanelContentChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        if (oldValue is PlayersPanelViewModel players && !ReferenceEquals(oldValue, newValue))
        {
            players.StopTests();
        }

        if (newValue is SettingsPanelViewModel settings && !ReferenceEquals(oldValue, newValue))
        {
            settings.RefreshFiles();
        }

        if (oldValue is AudioOutputPanelViewModel outputs && !ReferenceEquals(oldValue, newValue))
        {
            outputs.StopSyncTest();
        }
    }

    /// <summary>Audio/display configuration is locked while a song is active (prototype rule).</summary>
    [ObservableProperty]
    private bool _audioLocked;

    [ObservableProperty]
    private DialogMessage? _dialog;

    /// <summary>Library → Details: the song popup in the middle of the window; null = closed.</summary>
    [ObservableProperty]
    private SongDetailsViewModel? _songDetails;

    /// <summary>"Show details" in the dialog: the raw message / stack trace.</summary>
    [ObservableProperty]
    private bool _dialogDetailsShown;

    public DjWindowViewModel(IServiceProvider services, LibraryViewModel library, NowPlayingViewModel nowPlaying, PreviewViewModel preview, QueueViewModel queue,
        NotificationService notifications, PlaybackService playback, AppPaths paths)
    {
        LogsFolder = paths.Logs;
        _services = services;
        _playback = playback;
        Library = library;
        library.DetailsRequested += row => SongDetails = new SongDetailsViewModel(row, library,
            services.GetRequiredService<UsdbService>(), services.GetRequiredService<ThumbnailService>(),
            services.GetRequiredService<ConnectivityService>(), () => SongDetails = null);
        NowPlaying = nowPlaying;
        Preview = preview;
        Queue = queue;
        Notifications = notifications;
        notifications.DialogChanged += d =>
        {
            Dialog = d;
            DialogDetailsShown = false;
        };
        playback.StateChanged += _ => UpdateLock();
        nowPlaying.DisplaysRequested += () =>
        {
            if (ActivePanel != SidebarPanel.Displays)
            {
                TogglePanel(SidebarPanel.Displays);
            }
        };
        UpdateLock();
    }

    public LibraryViewModel Library { get; }
    public NowPlayingViewModel NowPlaying { get; }
    public PreviewViewModel Preview { get; }
    public QueueViewModel Queue { get; }
    public NotificationService Notifications { get; }

    /// <summary>Opened by the bug dialog's "Open log folder".</summary>
    public string LogsFolder { get; }

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
    private void ToggleDialogDetails() => DialogDetailsShown = !DialogDetailsShown;

    [RelayCommand]
    private void DismissToast(Toast toast) => Notifications.Dismiss(toast);

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
            SidebarPanel.Songbook => _services.GetRequiredService<SongbookPanelViewModel>(),
            SidebarPanel.Settings => _services.GetRequiredService<SettingsPanelViewModel>(),
            SidebarPanel.Layout => _services.GetRequiredService<LayoutPanelViewModel>(),
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
