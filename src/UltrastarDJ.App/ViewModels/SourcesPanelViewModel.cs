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

/// <summary>Song Sources panel: local folders now; USDB arrives in Sprint 6.</summary>
public sealed partial class SourcesPanelViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly ILogger<SourcesPanelViewModel> _log;

    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";

    public SourcesPanelViewModel(LibraryService library, ILogger<SourcesPanelViewModel> log)
    {
        _library = library;
        _log = log;
        Refresh();
        _library.Changed += () => Dispatcher.UIThread.Post(Refresh);
    }

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
