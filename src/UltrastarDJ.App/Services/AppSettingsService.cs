using Avalonia;
using Avalonia.Styling;
using UltrastarDJ.App.Game;
using UltrastarDJ.App.Library;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Game;

namespace UltrastarDJ.App.Services;

/// <summary>General app settings (theme, tooltips, difficulty, lyrics offset), persisted and applied on change.</summary>
public sealed class AppSettingsService
{
    private const string SettingsName = "app";
    private readonly ISettingsStore _settings;
    private AppSettingsDocument _doc;

    public AppSettingsService(ISettingsStore settings)
    {
        _settings = settings;
        _doc = settings.Load(SettingsName, AppSettingsDocument.Default());
        // Before any window is built; the system's language until one was chosen (first start asks).
        Localization.Translations.Instance.SetLanguage(_doc.Language ?? Localization.Translations.SystemLanguage());
        Apply();
    }

    public event Action? Changed;

    public bool LightTheme => _doc.LightTheme;
    public bool ShowTooltips => !_doc.HideTooltips;
    public NoteBarStyle NoteBarStyle => _doc.NoteBarStyle;
    public Difficulty Difficulty => _doc.Difficulty;
    /// <summary>Positive = lyrics/notes appear later relative to the audio.</summary>
    public double LyricsOffsetMs => _doc.LyricsOffsetMs;
    public bool NowPlayingHidden => _doc.NowPlayingHidden;
    /// <summary>Top-left of the floating Now Playing card in window coordinates; null = default placement.</summary>
    public (double X, double Y)? NowPlayingPosition => _doc.NowPlayingX is { } x && _doc.NowPlayingY is { } y ? (x, y) : null;

    /// <summary>Library columns to show (Layout panel).</summary>
    public IReadOnlySet<LibraryColumn> VisibleColumns => _doc.VisibleColumns is { } v ? v.ToHashSet() : LibraryColumns.DefaultVisible;

    public void SetColumnVisible(LibraryColumn column, bool visible)
    {
        HashSet<LibraryColumn> set = [.. VisibleColumns];
        if (visible ? set.Add(column) : set.Remove(column))
        {
            Update(_doc with { VisibleColumns = [.. LibraryColumns.All.Where(set.Contains)] });
        }
    }

    /// <summary>Layout → Show broken songs: off leaves songs marked broken out of the library (filters and counts too).</summary>
    public bool ShowBrokenSongs => _doc.ShowBrokenSongs;
    public void SetShowBrokenSongs(bool show) => Update(_doc with { ShowBrokenSongs = show });

    /// <summary>Where the DJ window was when the app last closed (null: first start → centred).</summary>
    public WindowPlacement? DjWindowPlacement => _doc.DjWindow;
    public void SetDjWindowPlacement(WindowPlacement placement) => Update(_doc with { DjWindow = placement });

    /// <summary>The UI language's code ("de"); null until it was chosen on the first start.</summary>
    public string? Language => _doc.Language;

    public void SetLanguage(string code)
    {
        Localization.Translations.Instance.SetLanguage(code);
        Update(_doc with { Language = code });
    }

    public void SetLightTheme(bool light) => Update(_doc with { LightTheme = light });
    public void SetShowTooltips(bool show) => Update(_doc with { HideTooltips = !show });
    public void SetNoteBarStyle(NoteBarStyle style) => Update(_doc with { NoteBarStyle = style });
    /// <summary>Beamer: the stave lines behind the note bars.</summary>
    public bool ShowGridLines => !_doc.HideGridLines;
    public void SetShowGridLines(bool show) => Update(_doc with { HideGridLines = !show });
    public void SetDifficulty(Difficulty d) => Update(_doc with { Difficulty = d });
    /// <summary>Migrated into the game output's latency (OutputsService); kept only to read old settings files.</summary>
    public void ClearLyricsOffset() => Update(_doc with { LyricsOffsetMs = 0 });
    public void SetNowPlayingHidden(bool hidden) => Update(_doc with { NowPlayingHidden = hidden });
    public void SetNowPlayingPosition(double x, double y) => Update(_doc with { NowPlayingX = Math.Round(x), NowPlayingY = Math.Round(y) });

    private void Update(AppSettingsDocument doc)
    {
        _doc = doc;
        _settings.Save(SettingsName, _doc);
        Apply();
        Changed?.Invoke();
    }

    private void Apply()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _doc.LightTheme ? ThemeVariant.Light : ThemeVariant.Dark;
            // Every window binds ToolTip.ServiceEnabled (inherited) to this resource: one switch for all tooltips.
            app.Resources["ShowTooltips"] = ShowTooltips;
        }
    }

    public sealed record AppSettingsDocument(bool LightTheme, Difficulty Difficulty, double LyricsOffsetMs)
    {
        public bool NowPlayingHidden { get; init; }
        /// <summary>Stored inverted so settings files written before this option keep tooltips on.</summary>
        public bool HideTooltips { get; init; }
        public NoteBarStyle NoteBarStyle { get; init; }
        /// <summary>Stored inverted so settings files from before the switch keep the lines.</summary>
        public bool HideGridLines { get; init; }
        public double? NowPlayingX { get; init; }
        public double? NowPlayingY { get; init; }
        /// <summary>Null = <see cref="LibraryColumns.DefaultVisible"/> (files written before the Layout panel).</summary>
        public IReadOnlyList<LibraryColumn>? VisibleColumns { get; init; }
        public bool ShowBrokenSongs { get; init; }
        public WindowPlacement? DjWindow { get; init; }
        public string? Language { get; init; }

        /// <summary>A first start is light; a saved choice (light or dark) is kept.</summary>
        public static AppSettingsDocument Default() => new(true, Difficulty.Medium, 0);
    }
}
