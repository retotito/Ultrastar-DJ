using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.App.ViewModels;

public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private bool _loading;

    [ObservableProperty] private bool _lightTheme;
    [ObservableProperty] private Difficulty _difficulty;
    [ObservableProperty] private double _lyricsOffsetMs;

    public SettingsPanelViewModel(AppSettingsService settings)
    {
        _settings = settings;
        _loading = true;
        LightTheme = settings.LightTheme;
        Difficulty = settings.Difficulty;
        LyricsOffsetMs = settings.LyricsOffsetMs;
        _loading = false;
    }

    public static string Version => typeof(SettingsPanelViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
    public IReadOnlyList<Difficulty> Difficulties { get; } = [Difficulty.Easy, Difficulty.Medium, Difficulty.Hard];
    public string DifficultyHint => Difficulty switch
    {
        Difficulty.Easy => "±2 semitones — party mode",
        Difficulty.Hard => "exact semitone — for show-offs",
        _ => "±1 semitone — the usual",
    };

    partial void OnLightThemeChanged(bool value)
    {
        if (!_loading)
        {
            _settings.SetLightTheme(value);
        }
    }

    partial void OnDifficultyChanged(Difficulty value)
    {
        OnPropertyChanged(nameof(DifficultyHint));
        if (!_loading)
        {
            _settings.SetDifficulty(value);
        }
    }

    partial void OnLyricsOffsetMsChanged(double value)
    {
        if (!_loading)
        {
            _settings.SetLyricsOffsetMs(value);
        }
    }
}
