using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Song Sources panel: local folders and the USDB account.</summary>
public sealed partial class SourcesPanelViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryService _library;
    private readonly UsdbService _usdb;
    private readonly NotificationService _notifications;
    private readonly ILogger<SourcesPanelViewModel> _log;

    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _usdbUser = "";
    [ObservableProperty] private string _usdbPassword = "";
    [ObservableProperty] private bool _usdbConnected;
    [ObservableProperty] private bool _usdbSyncing;
    [ObservableProperty] private bool _usdbBusy;
    [ObservableProperty] private string _usdbStatus = "";
    [ObservableProperty] private int _usdbCount;
    /// <summary>The USDB switch (same idea as a folder's): off hides its songs, the login stays.</summary>
    [ObservableProperty] private bool _usdbEnabled;

    public SourcesPanelViewModel(LibraryService library, UsdbService usdb, UsdbTextStore texts, UsdbTextDownloader downloader,
        NotificationService notifications, ILogger<SourcesPanelViewModel> log)
    {
        _texts = texts;
        _downloader = downloader;
        _textsMode = texts.Mode;
        texts.Changed += OnTextsChanged;
        downloader.Changed += OnTextsChanged;
        _library = library;
        _usdb = usdb;
        _notifications = notifications;
        _log = log;
        _usdbUser = usdb.Username ?? "";
        _usdbEnabled = library.UsdbEnabled;
        Refresh();
        RefreshUsdb();
        _library.Changed += OnLibraryChanged;
        // A drive plugged in or pulled: the row's "Not connected" follows within the 5 s poll.
        _library.AvailabilityChanged += OnLibraryChanged;
        _usdb.Changed += OnUsdbChanged;
    }

    private readonly UsdbTextStore _texts;
    private readonly UsdbTextDownloader _downloader;

    // ── Song texts kept on this computer (UsdbTextStore / UsdbTextDownloader) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextsLoaded), nameof(TextsFavourites), nameof(TextsAll))]
    private UsdbTextsMode _textsMode;
    [ObservableProperty] private bool _textsMoving;

    public bool TextsLoaded { get => TextsMode == UsdbTextsMode.Loaded; set { if (value) TextsMode = UsdbTextsMode.Loaded; } }
    public bool TextsFavourites { get => TextsMode == UsdbTextsMode.Favourites; set { if (value) TextsMode = UsdbTextsMode.Favourites; } }
    public bool TextsAll { get => TextsMode == UsdbTextsMode.All; set { if (value) TextsMode = UsdbTextsMode.All; } }

    partial void OnTextsModeChanged(UsdbTextsMode value)
    {
        if (value != _texts.Mode)
        {
            _texts.SetMode(value);
        }
    }

    public string TextsFolder => _texts.Folder;
    public bool TextsPaused => _texts.Paused;
    public bool TextsCanPause => TextsMode != UsdbTextsMode.Loaded && !_downloader.IsComplete;
    public string TextsProgress
    {
        get
        {
            int onDisk = _texts.Count;
            if (TextsMode == UsdbTextsMode.Loaded || _downloader.Target == 0)
            {
                return $"{onDisk:N0} song texts on this computer";
            }

            if (_downloader.IsComplete)
            {
                return $"All {_downloader.Target:N0} {(TextsMode == UsdbTextsMode.All ? "song texts" : "favourites")} on this computer ✓";
            }

            string state = _downloader.IsActive ? $"about {Hours(_downloader.Remaining)} left" : _texts.Paused ? "paused" : "waiting for USDB";
            return $"{_downloader.Done:N0} of {_downloader.Target:N0} downloaded ({_downloader.PercentText}) · {state}";
        }
    }

    private static string Hours(TimeSpan t) => t.TotalHours >= 1 ? $"{t.TotalHours:0.#} h" : $"{Math.Max(1, Math.Ceiling(t.TotalMinutes)):0} min";

    private void OnTextsChanged() => Dispatcher.UIThread.Post(() =>
    {
        TextsMode = _texts.Mode;
        OnPropertyChanged(nameof(TextsFolder));
        OnPropertyChanged(nameof(TextsPaused));
        OnPropertyChanged(nameof(TextsCanPause));
        OnPropertyChanged(nameof(TextsProgress));
    });

    [RelayCommand] private void ToggleTextsPaused() => _texts.SetPaused(!_texts.Paused);

    [RelayCommand]
    private void ShowTextsFolder()
    {
        try
        {
            Directory.CreateDirectory(_texts.Folder);
            FileReveal.OpenFolder(_texts.Folder);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            UsdbStatus = $"Could not open it: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChangeTextsFolderAsync()
    {
        if (Owner is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> picked = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder for the USDB song texts", AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            await MoveTextsAsync(path);
        }
    }

    [RelayCommand] private Task DefaultTextsFolderAsync() => MoveTextsAsync(null);

    public bool TextsCustomFolder => !_texts.IsDefaultFolder;

    private async Task MoveTextsAsync(string? folder)
    {
        TextsMoving = true;
        try
        {
            int moved = await Task.Run(() => _texts.MoveTo(folder));
            _notifications.Success("Song texts moved", $"{moved:N0} files → {_texts.Folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Moving the song texts failed");
            _notifications.ShowError("Could not move the song texts", ex.Message);
        }
        finally
        {
            TextsMoving = false;
            OnPropertyChanged(nameof(TextsCustomFolder));
            OnTextsChanged();
        }
    }

    private void OnLibraryChanged() => Dispatcher.UIThread.Post(Refresh);
    private void OnUsdbChanged() => Dispatcher.UIThread.Post(RefreshUsdb);

    public ObservableCollection<SourceRowViewModel> Sources { get; } = [];

    /// <summary>Set by the view: the window used to open the native folder picker.</summary>
    public TopLevel? Owner { get; set; }

    private void Refresh()
    {
        Sources.Clear();
        foreach (SongSource s in _library.Sources)
        {
            Sources.Add(new SourceRowViewModel(s, _library.CountFor(s.Id), _library.IsAvailable(s.Id), this));
        }
    }

    private void RefreshUsdb()
    {
        UsdbConnected = _usdb.IsConnected;
        UsdbSyncing = _usdb.IsSyncing;
        UsdbStatus = _usdb.Status;
        UsdbCount = _usdb.CatalogCount;
        UsdbConnectCommand.NotifyCanExecuteChanged();
        UsdbSyncCommand.NotifyCanExecuteChanged();
    }

    public bool CanConnectUsdb => !UsdbBusy && !UsdbConnected && UsdbUser.Trim().Length > 0 && UsdbPassword.Length > 0;
    partial void OnUsdbUserChanged(string value) => UsdbConnectCommand.NotifyCanExecuteChanged();
    partial void OnUsdbPasswordChanged(string value) => UsdbConnectCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanConnectUsdb))]
    private async Task UsdbConnectAsync()
    {
        UsdbBusy = true;
        try
        {
            if (await _usdb.ConnectAsync(UsdbUser.Trim(), UsdbPassword))
            {
                UsdbPassword = "";
            }
        }
        catch (UsdbException ex)
        {
            _log.LogWarning("USDB connect failed: {Error}", ex.Message);
            UsdbStatus = ex.Message;
        }
        finally
        {
            UsdbBusy = false;
            RefreshUsdb();
        }
    }

    private bool CanSyncUsdb() => UsdbConnected && !UsdbSyncing;

    [RelayCommand(CanExecute = nameof(CanSyncUsdb))]
    private Task UsdbSyncAsync() => _usdb.SyncAsync(full: false);

    [RelayCommand(CanExecute = nameof(CanSyncUsdb))]
    private Task UsdbFullSyncAsync() => _usdb.SyncAsync(full: true);

    [RelayCommand] private void UsdbAbort() => _usdb.AbortSync();

    [RelayCommand]
    private void UsdbDisconnect()
    {
        _usdb.Disconnect();
        UsdbPassword = "";
        RefreshUsdb();
    }

    public void Dispose()
    {
        _library.Changed -= OnLibraryChanged;
        _library.AvailabilityChanged -= OnLibraryChanged;
        _usdb.Changed -= OnUsdbChanged;
        _texts.Changed -= OnTextsChanged;
        _downloader.Changed -= OnTextsChanged;
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (Owner is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> picked = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add song folder", AllowMultiple = false });
        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        await RunScanAsync(() => _library.AddLocalFolderAsync(path, Progress()));
    }

    public Task RescanAsync(string sourceId) => RunScanAsync(() => _library.RescanAsync(sourceId, Progress()));

    public void Remove(string sourceId) => _library.RemoveSource(sourceId);

    public void SetEnabled(string sourceId, bool enabled) => _library.SetEnabled(sourceId, enabled);

    partial void OnUsdbEnabledChanged(bool value) => _library.SetUsdbEnabled(value);

    private Progress<LocalFolderScanner.Progress> Progress()
        => new(p => Status = $"Scanning… {p.Parsed} songs ({p.Found} files)");

    private async Task RunScanAsync(Func<Task> scan)
    {
        Busy = true;
        try
        {
            await scan();
            Status = $"{_library.Songs.Count} songs in library";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Scan failed");
            Status = "";
            _notifications.ShowError("Folder scan failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}

public sealed partial class SourceRowViewModel(SongSource source, int count, bool available, SourcesPanelViewModel owner) : ObservableObject
{
    /// <summary>Switched off: its songs leave the library and the source filter (prototype's toggle).</summary>
    [ObservableProperty] private bool _enabled = source.Enabled;

    public string Label => source.Label;
    public string Path => source.Path ?? "";
    /// <summary>Folder unreachable (USB drive pulled). Only for enabled sources — a switched-off one is not checked.</summary>
    public bool IsMissing => source.Enabled && !available;
    public string CountText => IsMissing ? "Not connected" : $"{count} songs";
    public double Opacity => IsMissing || !source.Enabled ? 0.5 : 1.0;

    partial void OnEnabledChanged(bool value) => owner.SetEnabled(source.Id, value);

    [RelayCommand] private Task RescanAsync() => owner.RescanAsync(source.Id);
    [RelayCommand] private void Remove() => owner.Remove(source.Id);
}
