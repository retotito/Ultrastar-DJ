using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Centre panel: the song library. Sprint 2 shows a plain virtualized list; search/filter/sort follow in Sprint 5.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _library;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private Song? _selected;

    public LibraryViewModel(LibraryService library)
    {
        _library = library;
        Refresh();
        _library.Changed += () => Dispatcher.UIThread.Post(Refresh);
    }

    public ObservableCollection<Song> Songs { get; } = [];

    partial void OnSearchChanged(string value) => Refresh();

    private void Refresh()
    {
        IReadOnlyList<Song> all = _library.Songs;
        TotalCount = all.Count;
        string q = Search.Trim();
        IEnumerable<Song> filtered = q.Length == 0
            ? all
            : all.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase));

        Songs.Clear();
        foreach (Song s in filtered)
        {
            Songs.Add(s);
        }
    }
}
