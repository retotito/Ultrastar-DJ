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

    public void SetLightTheme(bool light) => Update(_doc with { LightTheme = light });
    public void SetDifficulty(Difficulty d) => Update(_doc with { Difficulty = d });
    public void SetLyricsOffsetMs(double ms) => Update(_doc with { LyricsOffsetMs = Math.Clamp(Math.Round(ms), -500, 500) });

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
        public static AppSettingsDocument Default() => new(false, Difficulty.Medium, 0);
    }
}
