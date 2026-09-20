using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// View state of one singer screen. Pure observer: later sprints feed <see cref="State"/> from the playback service.
/// </summary>
public sealed partial class BeamerViewModel : ViewModelBase
{
    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Idle;

    [ObservableProperty]
    private bool _hasVideo;

    public BeamerViewModel(DisplayId id, FrameBus gameFrames)
    {
        Id = id;
        GameFrames = gameFrames;
        HasVideo = gameFrames.HasSource;
        gameFrames.SourceChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => HasVideo = gameFrames.HasSource);
    }

    public DisplayId Id { get; }
    public string Label => $"Beamer {(int)Id}";

    /// <summary>The game channel's video, shared by every beamer and the DJ monitor.</summary>
    public FrameBus GameFrames { get; }
}
