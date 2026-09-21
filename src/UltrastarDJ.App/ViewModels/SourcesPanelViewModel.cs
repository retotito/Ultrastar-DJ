using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Song Sources panel: local folders and the USDB account.</summary>
public sealed partial class SourcesPanelViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryService _library;
    private readonly UsdbService _usdb;
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

    public SourcesPanelViewModel(LibraryService library, UsdbService usdb, ILogger<SourcesPanelViewModel> log)
    {
        _library = library;
        _usdb = usdb;
        _log = log;
        _usdbUser = usdb.Username ?? "";
        Refresh();
        RefreshUsdb();
        _library.Changed += OnLibraryChanged;
        _usdb.Changed += OnUsdbChanged;
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
            Sources.Add(new SourceRowViewModel(s, _library.CountFor(s.Id), this));
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
        _usdb.Changed -= OnUsdbChanged;
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
            Status = "Scan failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

public sealed partial class SourceRowViewModel(SongSource source, int count, SourcesPanelViewModel owner) : ObservableObject
{
    public string Label => source.Label;
    public string Path => source.Path ?? "";
    public string CountText => $"{count} songs";

    [RelayCommand] private Task RescanAsync() => owner.RescanAsync(source.Id);
    [RelayCommand] private void Remove() => owner.Remove(source.Id);
}
