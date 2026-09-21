using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>A library row: the song plus what the table shows about its source.</summary>
public sealed record LibraryRow(Song Song, string Source, bool IsAvailable)
{
    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public int? Year => Song.Year;
    public string? Language => Song.Language;
    public string? Genre => Song.Genre;
    public bool IsUsdb => Song.UsdbId is not null;
    public bool HasLocalAudio => Song.HasLocalAudio;
    public bool HasLocalVideo => Song.HasLocalVideo;
    /// <summary>USDB songs always play from YouTube; the id is only known once the txt is fetched.</summary>
    public bool HasYouTube => Song.HasYouTube || IsUsdb;
    public double Opacity => IsAvailable ? 1.0 : 0.4;
}

public enum LibrarySort
{
    Artist,
    Title,
    Year,
    Language,
}

/// <summary>Centre panel: the song library with search, filters and sort. Row actions hand songs to preview, queue or game.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private const string All = "All";

    private readonly LibraryService _library;
    private readonly PreviewViewModel _preview;
    private readonly QueueViewModel _queue;
    private readonly NowPlayingViewModel _nowPlaying;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _shownCount;
    [ObservableProperty] private LibraryRow? _selected;
    [ObservableProperty] private string _language = All;
    [ObservableProperty] private string _genre = All;
    [ObservableProperty] private LibrarySort _sort = LibrarySort.Artist;
    [ObservableProperty] private bool _descending;

    public LibraryViewModel(LibraryService library, PreviewViewModel preview, QueueViewModel queue, NowPlayingViewModel nowPlaying)
    {
        _library = library;
        _preview = preview;
        _queue = queue;
        _nowPlaying = nowPlaying;
        Refresh();
        _library.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _library.AvailabilityChanged += () => Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>Replaced wholesale on every refresh: 27k USDB rows through ObservableCollection.Add would stall the UI.</summary>
    [ObservableProperty] private IReadOnlyList<LibraryRow> _rows = [];
    public ObservableCollection<string> Languages { get; } = [All];
    public ObservableCollection<string> Genres { get; } = [All];

    public string SortIndicator(LibrarySort column) => Sort == column ? (Descending ? " ▼" : " ▲") : "";
    public string ArtistHeader => "ARTIST" + SortIndicator(LibrarySort.Artist);
    public string TitleHeader => "TITLE" + SortIndicator(LibrarySort.Title);
    public string YearHeader => "YEAR" + SortIndicator(LibrarySort.Year);
    public string LanguageHeader => "LANGUAGE" + SortIndicator(LibrarySort.Language);

    [RelayCommand]
    private void SortBy(LibrarySort column)
    {
        if (Sort == column)
        {
            Descending = !Descending;
        }
        else
        {
            Sort = column;
            Descending = false;
        }

        OnPropertyChanged(nameof(ArtistHeader));
        OnPropertyChanged(nameof(TitleHeader));
        OnPropertyChanged(nameof(YearHeader));
        OnPropertyChanged(nameof(LanguageHeader));
        Refresh();
    }

    /// <summary>Double-click / Enter.</summary>
    [RelayCommand]
    private Task PreviewAsync(LibraryRow? row) => row is null ? Task.CompletedTask : _preview.LoadCommand.ExecuteAsync(row.Song);

    [RelayCommand]
    private void AddToQueue(LibraryRow? row)
    {
        if (row is not null)
        {
            _queue.Add(row.Song);
        }
    }

    [RelayCommand]
    private Task LoadIntoGameAsync(LibraryRow? row) => row is null ? Task.CompletedTask : _nowPlaying.LoadCommand.ExecuteAsync(row.Song);

    public Task PreviewSelectedAsync() => PreviewAsync(Selected);

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnLanguageChanged(string value) => Refresh();
    partial void OnGenreChanged(string value) => Refresh();

    private void Refresh()
    {
        IReadOnlyList<Song> all = _library.Songs;
        TotalCount = all.Count;
        RefreshFilterValues(all);

        string q = Search.Trim();
        IEnumerable<Song> filtered = all;
        if (q.Length > 0)
        {
            filtered = filtered.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (Language != All)
        {
            filtered = filtered.Where(s => string.Equals(s.Language, Language, StringComparison.OrdinalIgnoreCase));
        }

        if (Genre != All)
        {
            filtered = filtered.Where(s => string.Equals(s.Genre, Genre, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<Song> sorted = Sort switch
        {
            LibrarySort.Title => Order(filtered, s => s.Title),
            LibrarySort.Year => Order(filtered, s => s.Year ?? 0),
            LibrarySort.Language => Order(filtered, s => s.Language ?? ""),
            _ => Order(filtered, s => s.Artist),
        };
        sorted = Sort == LibrarySort.Artist ? sorted.ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase) : sorted.ThenBy(s => s.Artist, StringComparer.OrdinalIgnoreCase);

        Rows = sorted.Select(s => new LibraryRow(s, _library.SourceLabel(s.SourceId), _library.IsAvailable(s.SourceId))).ToList();
        ShownCount = Rows.Count;
    }

    private IOrderedEnumerable<Song> Order<TKey>(IEnumerable<Song> songs, Func<Song, TKey> key)
    {
        IComparer<TKey>? cmp = typeof(TKey) == typeof(string) ? (IComparer<TKey>)StringComparer.OrdinalIgnoreCase : null;
        return Descending ? songs.OrderByDescending(key, cmp) : songs.OrderBy(key, cmp);
    }

    private void RefreshFilterValues(IReadOnlyList<Song> all)
    {
        Sync(Languages, all.Select(s => s.Language).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        Sync(Genres, all.Select(s => s.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        if (!Languages.Contains(Language))
        {
            Language = All;
        }

        if (!Genres.Contains(Genre))
        {
            Genre = All;
        }
    }

    private static void Sync(ObservableCollection<string> target, IEnumerable<string> values)
    {
        List<string> wanted = [All, .. values];
        if (target.SequenceEqual(wanted))
        {
            return;
        }

        target.Clear();
        foreach (string v in wanted)
        {
            target.Add(v);
        }
    }
}
