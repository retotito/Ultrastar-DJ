using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Game;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.Infrastructure.Settings;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly AppPaths _paths;
    private readonly PlaybackService _playback;
    private readonly YtDlpService _ytDlp;

    // ── YouTube (yt-dlp): version, newer one, update ──
    public string YtDlpVersionText => _ytDlp.CurrentVersion is { } v ? $"yt-dlp {v}" : L.T("settings.ytdlp_not_found");
    public string YtDlpStatusText => _ytDlp.Status is { Length: > 0 } s ? s
        : _ytDlp.UpdateAvailable ? L.F("settings.ytdlp_newer", _ytDlp.LatestVersion)
        : _ytDlp.LatestVersion is not null ? L.T("settings.ytdlp_up_to_date") : "";
    public string YtDlpButtonText => _ytDlp.UpdateAvailable ? L.F("settings.ytdlp_update_to", _ytDlp.LatestVersion) : L.T("settings.ytdlp_check");
    public bool YtDlpIdle => !_ytDlp.Busy;

    [RelayCommand]
    private async Task UpdateYtDlpAsync()
    {
        // A check that finds a newer one only turns the button into "Update to …": the DJ decides when.
        if (_ytDlp.UpdateAvailable)
        {
            await _ytDlp.UpdateAsync();
        }
        else
        {
            await _ytDlp.CheckAsync();
        }
    }

    private void OnYtDlpChanged()
    {
        OnPropertyChanged(nameof(YtDlpVersionText));
        OnPropertyChanged(nameof(YtDlpStatusText));
        OnPropertyChanged(nameof(YtDlpButtonText));
        OnPropertyChanged(nameof(YtDlpIdle));
    }

    /// <summary>
    /// Each player's scorer takes the tolerance when the song starts; a change mid-song would only apply to the next
    /// one, so the drop-down is locked meanwhile (like Audio Input / Output and Displays).
    /// </summary>
    public bool DifficultyLocked => _playback.State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused;
    private bool _loading;
    private string? _newestCrashReport;

    [ObservableProperty] private string _theme = LightName;
    [ObservableProperty] private bool _showTooltips;
    [ObservableProperty] private NoteBarStyle _noteBarStyle;
    [ObservableProperty] private bool _showGridLines;
    [ObservableProperty] private Difficulty _difficulty;

    [ObservableProperty] private string _crashReportsText = "";
    [ObservableProperty] private string _filesStatus = "";
    [ObservableProperty] private string _backupStatus = "";
    /// <summary>A restore is staged: it takes effect when the app starts again.</summary>
    [ObservableProperty] private bool _restorePending;

    /// <summary>The file pickers need the owning window (set by the view).</summary>
    public TopLevel? Owner { get; set; }

    public IReadOnlyList<UiLanguage> UiLanguages => Translations.Choices;

    /// <summary>Settings → Language: switches the whole app at once (no restart).</summary>
    [ObservableProperty] private UiLanguage? _uiLanguage;

    partial void OnUiLanguageChanged(UiLanguage? value)
    {
        if (!_loading && value is not null && value.Code != Translations.Instance.Code)
        {
            _settings.SetLanguage(value.Code);
        }
    }

    public SettingsPanelViewModel(AppSettingsService settings, AppPaths paths, PlaybackService playback, YtDlpService ytDlp)
    {
        _ytDlp = ytDlp;
        ytDlp.Changed += OnYtDlpChanged;
        _settings = settings;
        _paths = paths;
        _playback = playback;
        playback.StateChanged += _ => OnPropertyChanged(nameof(DifficultyLocked));
        _loading = true;
        UiLanguage = Translations.Instance.Current;
        Theme = settings.LightTheme ? LightName : DarkName;
        ShowTooltips = settings.ShowTooltips;
        NoteBarStyle = settings.NoteBarStyle;
        ShowGridLines = settings.ShowGridLines;
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
        CrashReportsText = reports.Count == 1 ? L.T("settings.crash_report_one") : L.F("settings.crash_reports", reports.Count);
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
            FilesStatus = L.T("settings.no_log");
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
    private async Task BackUpAsync()
    {
        if (Owner is null)
        {
            return;
        }

        IStorageFile? file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("settings.backup_title"),
            SuggestedFileName = $"UltrastarDJ-backup-{DateTime.Now:yyyy-MM-dd}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            int n = SettingsBackup.Create(_paths.Settings, path);
            BackupStatus = L.F("settings.backup_saved", n, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupStatus = L.F("settings.backup_failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (Owner is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("settings.restore_title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L.T("settings.restore_file_type")) { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            SettingsBackup.Stage(path, _paths.Settings);
            RestorePending = true;
            BackupStatus = L.T("settings.restore_pending");
        }
        catch (InvalidDataException ex)
        {
            BackupStatus = ex.Message;
        }
    }

    /// <summary>After a restore: quit, so the next start applies it.</summary>
    [RelayCommand]
    private static void Quit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

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
            FilesStatus = L.F("settings.open_failed", ex.Message);
        }
    }

    public static string Version => typeof(SettingsPanelViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
    public IReadOnlyList<Difficulty> Difficulties { get; } = [Difficulty.Easy, Difficulty.Medium, Difficulty.Hard];
    public string DifficultyHint => Difficulty switch
    {
        Difficulty.Easy => L.T("settings.difficulty_easy_hint"),
        Difficulty.Hard => L.T("settings.difficulty_hard_hint"),
        _ => L.T("settings.difficulty_medium_hint"),
    };

    public IReadOnlyList<NoteBarStyle> NoteBarStyles { get; } = [NoteBarStyle.White, NoteBarStyle.Black];

    partial void OnShowGridLinesChanged(bool value)
    {
        if (!_loading)
        {
            _settings.SetShowGridLines(value);
        }
    }

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

    private const string LightName = "Light";
    private const string DarkName = "Dark";
    public IReadOnlyList<string> Themes { get; } = [LightName, DarkName];

    partial void OnThemeChanged(string value)
    {
        if (!_loading && value is not null)
        {
            _settings.SetLightTheme(value == LightName);
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
