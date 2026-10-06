using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Game;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.App.ViewModels;

public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly AppPaths _paths;
    private bool _loading;
    private string? _newestCrashReport;

    [ObservableProperty] private bool _lightTheme;
    [ObservableProperty] private bool _showTooltips;
    [ObservableProperty] private NoteBarStyle _noteBarStyle;
    [ObservableProperty] private Difficulty _difficulty;

    [ObservableProperty] private string _crashReportsText = "";
    [ObservableProperty] private string _filesStatus = "";

    public SettingsPanelViewModel(AppSettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
        _loading = true;
        LightTheme = settings.LightTheme;
        ShowTooltips = settings.ShowTooltips;
        NoteBarStyle = settings.NoteBarStyle;
        Difficulty = settings.Difficulty;
        _loading = false;
    }

    // ── Logs & data ──
    // macOS writes a report here when a process really crashes (e.g. in an audio thread) — our own log stops before.
    private static readonly string CrashReportFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");

    public bool HasCrashReports => _newestCrashReport is not null;

    /// <summary>The panel opened: look for new crash reports.</summary>
    public void RefreshFiles()
    {
        FilesStatus = "";
        List<FileInfo> reports = OperatingSystem.IsMacOS() && Directory.Exists(CrashReportFolder)
            ? [.. new DirectoryInfo(CrashReportFolder).EnumerateFiles($"{AppPaths.AppFolderName}*").Where(f => f.Extension is ".ips" or ".crash").OrderByDescending(f => f.LastWriteTimeUtc)]
            : [];
        _newestCrashReport = reports.Count > 0 ? reports[0].FullName : null;
        CrashReportsText = reports.Count == 1 ? "1 crash report" : $"{reports.Count} crash reports";
        OnPropertyChanged(nameof(HasCrashReports));
    }

    /// <summary>Today's log: the newest file (one per day, or …_001.log once a day's file reached 10 MB).</summary>
    private string? NewestLog() => new DirectoryInfo(_paths.Logs).EnumerateFiles("ultrastardj-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;

    [RelayCommand]
    private void ShowLogFolder() => Run(() =>
    {
        if (NewestLog() is { } log)
        {
            FileReveal.Reveal(log);
        }
        else
        {
            FileReveal.OpenFolder(_paths.Logs);
        }
    });

    [RelayCommand]
    private void OpenLog() => Run(() =>
    {
        if (NewestLog() is { } log)
        {
            FileReveal.OpenLog(log);
        }
        else
        {
            FilesStatus = "No log yet.";
        }
    });

    [RelayCommand]
    private void ShowCrashReports() => Run(() =>
    {
        if (_newestCrashReport is { } report)
        {
            FileReveal.Reveal(report);
        }
    });

    [RelayCommand]
    private void ShowDataFolder() => Run(() => FileReveal.OpenFolder(_paths.Data));

    private void Run(Action open)
    {
        try
        {
            FilesStatus = "";
            open();
        }
        catch (Win32Exception ex)
        {
            FilesStatus = $"Could not open it: {ex.Message}";
        }
    }

    public static string Version => typeof(SettingsPanelViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
    public IReadOnlyList<Difficulty> Difficulties { get; } = [Difficulty.Easy, Difficulty.Medium, Difficulty.Hard];
    public string DifficultyHint => Difficulty switch
    {
        Difficulty.Easy => "±2 semitones — party mode",
        Difficulty.Hard => "exact semitone — for show-offs",
        _ => "±1 semitone — the usual",
    };

    public IReadOnlyList<NoteBarStyle> NoteBarStyles { get; } = [NoteBarStyle.White, NoteBarStyle.Black];

    partial void OnNoteBarStyleChanged(NoteBarStyle value)
    {
        if (!_loading)
        {
            _settings.SetNoteBarStyle(value);
        }
    }

    partial void OnShowTooltipsChanged(bool value)
    {
        if (!_loading)
        {
            _settings.SetShowTooltips(value);
        }
    }

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
}
