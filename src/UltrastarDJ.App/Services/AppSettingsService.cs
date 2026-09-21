using Avalonia;
using Avalonia.Styling;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.App.Services;

/// <summary>General app settings (theme, difficulty, lyrics offset), persisted and applied on change.</summary>
public sealed class AppSettingsService
{
    private const string SettingsName = "app";
    private readonly ISettingsStore _settings;
    private AppSettingsDocument _doc;

    public AppSettingsService(ISettingsStore settings)
    {
        _settings = settings;
        _doc = settings.Load(SettingsName, AppSettingsDocument.Default());
        ApplyTheme();
    }

    public event Action? Changed;

    public bool LightTheme => _doc.LightTheme;
    public Difficulty Difficulty => _doc.Difficulty;
    /// <summary>Positive = lyrics/notes appear later relative to the audio.</summary>
    public double LyricsOffsetMs => _doc.LyricsOffsetMs;
    public bool NowPlayingHidden => _doc.NowPlayingHidden;
    /// <summary>Top-left of the floating Now Playing card in window coordinates; null = default placement.</summary>
    public (double X, double Y)? NowPlayingPosition => _doc.NowPlayingX is { } x && _doc.NowPlayingY is { } y ? (x, y) : null;

    public void SetLightTheme(bool light) => Update(_doc with { LightTheme = light });
    public void SetDifficulty(Difficulty d) => Update(_doc with { Difficulty = d });
    public void SetLyricsOffsetMs(double ms) => Update(_doc with { LyricsOffsetMs = Math.Clamp(Math.Round(ms), -500, 500) });
    public void SetNowPlayingHidden(bool hidden) => Update(_doc with { NowPlayingHidden = hidden });
    public void SetNowPlayingPosition(double x, double y) => Update(_doc with { NowPlayingX = Math.Round(x), NowPlayingY = Math.Round(y) });

    private void Update(AppSettingsDocument doc)
    {
        _doc = doc;
        _settings.Save(SettingsName, _doc);
        ApplyTheme();
        Changed?.Invoke();
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _doc.LightTheme ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }

    public sealed record AppSettingsDocument(bool LightTheme, Difficulty Difficulty, double LyricsOffsetMs)
    {
        public bool NowPlayingHidden { get; init; }
        public double? NowPlayingX { get; init; }
        public double? NowPlayingY { get; init; }

        public static AppSettingsDocument Default() => new(false, Difficulty.Medium, 0);
    }
}
