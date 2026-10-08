using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.App.ViewModels;

/// <summary>A player offered for a duet voice.</summary>
public sealed record SingerOption(int PlayerId, string Name)
{
    public string ColorKey => $"BrushPlayer{PlayerId}";
    public override string ToString() => Name;
}

/// <summary>
/// The popup when a duet is loaded (2+ players set up): who sings voice 1 and who voice 2. Starts with the current pick
/// (the first two set up); the same player can't take both voices — picking them for the other voice swaps.
/// </summary>
public sealed partial class DuetSingersViewModel : ObservableObject
{
    private readonly PlaybackService _playback;
    private readonly Action _close;
    private bool _swapping;

    [ObservableProperty] private SingerOption? _voice1;
    [ObservableProperty] private SingerOption? _voice2;

    public DuetSingersViewModel(PlaybackService playback, Action close)
    {
        _playback = playback;
        _close = close;
        Options = [.. playback.ActivePlayers().Select(p => new SingerOption(p.Id, p.Name))];
        IReadOnlyList<(int PlayerId, int Voice)> now = playback.Singers();
        _voice1 = Options.FirstOrDefault(o => now.Any(s => s.PlayerId == o.PlayerId && s.Voice == 0));
        _voice2 = Options.FirstOrDefault(o => now.Any(s => s.PlayerId == o.PlayerId && s.Voice == 1));
        Title = playback.Song?.Title ?? "";
        Voice1Label = playback.Song?.Voice1 is { Length: > 0 } a ? $"Voice 1 — {a}" : "Voice 1";
        Voice2Label = playback.Song?.Voice2 is { Length: > 0 } b ? $"Voice 2 — {b}" : "Voice 2";
        SittingOut = Options.Count > 2 ? "The other players sit out this song." : "";
    }

    public string Title { get; }
    public string Voice1Label { get; }
    public string Voice2Label { get; }
    public IReadOnlyList<SingerOption> Options { get; }
    public string SittingOut { get; }

    // Picking the other voice's singer swaps the two: one player can't sing both.
    partial void OnVoice1Changed(SingerOption? oldValue, SingerOption? newValue)
    {
        if (!_swapping && newValue is not null && newValue == Voice2)
        {
            _swapping = true;
            Voice2 = oldValue;
            _swapping = false;
        }
    }

    partial void OnVoice2Changed(SingerOption? oldValue, SingerOption? newValue)
    {
        if (!_swapping && newValue is not null && newValue == Voice1)
        {
            _swapping = true;
            Voice1 = oldValue;
            _swapping = false;
        }
    }

    [RelayCommand]
    private void Swap()
    {
        _swapping = true;
        (Voice1, Voice2) = (Voice2, Voice1);
        _swapping = false;
    }

    [RelayCommand]
    private void Ok()
    {
        if (Voice1 is { } a && Voice2 is { } b && a != b)
        {
            _playback.SetDuetChoice(new DuetChoice(a.PlayerId, b.PlayerId));
        }

        _close();
    }

    [RelayCommand]
    private void Close() => _close();
}
