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

namespace UltrastarDJ.App.ViewModels;

public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly AppPaths _paths;
    private readonly PlaybackService _playback;
    private readonly YtDlpService _ytDlp;

    // ── YouTube (yt-dlp): version, newer one, update ──
    public string YtDlpVersionText => _ytDlp.CurrentVersion is { } v ? $"yt-dlp {v}" : "yt-dlp not found";
    public string YtDlpStatusText => _ytDlp.Status is { Length: > 0 } s ? s
        : _ytDlp.UpdateAvailable ? $"Newer version {_ytDlp.LatestVersion} available"
        : _ytDlp.LatestVersion is not null ? "Up to date" : "";
    public string YtDlpButtonText => _ytDlp.UpdateAvailable ? $"Update to {_ytDlp.LatestVersion}" : "Check for update";
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

    public SettingsPanelViewModel(AppSettingsService settings, AppPaths paths, PlaybackService playback, YtDlpService ytDlp)
    {
        _ytDlp = ytDlp;
        ytDlp.Changed += OnYtDlpChanged;
        _settings = settings;
        _paths = paths;
        _playback = playback;
        playback.StateChanged += _ => OnPropertyChanged(nameof(DifficultyLocked));
        _loading = true;
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
    private async Task BackUpAsync()
    {
        if (Owner is null)
        {
            return;
        }

        IStorageFile? file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Back up Ultrastar DJ",
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
            BackupStatus = $"Saved {n} settings files to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupStatus = $"Could not save the backup: {ex.Message}";
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
            Title = "Restore a backup",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Ultrastar DJ backup") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            SettingsBackup.Stage(path, _paths.Settings);
            RestorePending = true;
            BackupStatus = "Restored when Ultrastar DJ starts again — quit now and start it again.";
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
