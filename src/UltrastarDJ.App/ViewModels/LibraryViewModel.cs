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
    /// <summary>USDB popularity; "—" for local songs, empty for USDB songs under 100 views.</summary>
    public string Stars => Song.Stars is { } n ? new string('★', n) : "—";
}

/// <summary>An entry of the rating filter: exactly <see cref="Stars"/> stars, or no filter when null.</summary>
public sealed record RatingOption(int? Stars, string Label)
{
    public override string ToString() => Label;
}

/// <summary>An entry of the source filter. <see cref="Key"/> is a source id, or <see cref="AllKey"/>/<see cref="LocalKey"/>.</summary>
public sealed record SourceOption(string Key, string Label)
{
    public const string AllKey = "*";
    public const string LocalKey = "*local";
    public override string ToString() => Label;
}

/// <summary>Centre panel: the song library with search, filters and sort. Row actions hand songs to preview, queue or game.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    // The first entry of each filter is "no filter" and is labelled with the filter's name.
    private const string AnyLanguage = "Language";
    private const string AnyGenre = "Genre";
    private static readonly RatingOption AnyRating = new(null, "Rating");
    private static readonly SourceOption AllSources = new(SourceOption.AllKey, "All sources");

    // Clearing Sources makes the ComboBox write null back into Source; that must not re-enter Refresh.
    private bool _rebuildingSources;

    private readonly LibraryService _library;
    private readonly PreviewViewModel _preview;
    private readonly QueueViewModel _queue;
    private readonly NowPlayingViewModel _nowPlaying;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _shownCount;
    [ObservableProperty] private LibraryRow? _selected;
    [ObservableProperty] private string _language = AnyLanguage;
    [ObservableProperty] private string _genre = AnyGenre;
    [ObservableProperty] private RatingOption? _rating = AnyRating;
    [ObservableProperty] private SongSort _sort = SongSort.Artist;
    /// <summary>Null while the ComboBox rebuilds its items; treated as "all sources".</summary>
    [ObservableProperty] private SourceOption? _source = AllSources;
    [ObservableProperty] private bool _descending;

    public LibraryViewModel(LibraryService library, PreviewViewModel preview, QueueViewModel queue, NowPlayingViewModel nowPlaying)
    {
        _library = library;
        _preview = preview;
        _queue = queue;
        _nowPlaying = nowPlaying;
        _nowPlaying.LoadAvailabilityChanged += () =>
        {
            LoadIntoGameCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(LoadSongTip));
        };
        Refresh();
        _library.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _library.AvailabilityChanged += () => Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>Replaced wholesale on every refresh: 27k USDB rows through ObservableCollection.Add would stall the UI.</summary>
    [ObservableProperty] private IReadOnlyList<LibraryRow> _rows = [];
    public ObservableCollection<string> Languages { get; } = [AnyLanguage];
    public ObservableCollection<string> Genres { get; } = [AnyGenre];
    /// <summary>Any of rating/language/genre/source set (search has its own clear button). Shows "Clear".</summary>
    public bool HasActiveFilters => Rating?.Stars is not null || Language != AnyLanguage || Genre != AnyGenre
        || Source?.Key is not (null or SourceOption.AllKey);

    [RelayCommand]
    private void ClearFilters()
    {
        Rating = AnyRating;
        Language = AnyLanguage;
        Genre = AnyGenre;
        Source = AllSources;
    }

    public IReadOnlyList<RatingOption> Ratings { get; } = [AnyRating, new(1, "★"), new(2, "★★"), new(3, "★★★"), new(4, "★★★★")];
    public ObservableCollection<SourceOption> Sources { get; } = [AllSources];

    public string SortIndicator(SongSort column) => Sort == column ? (Descending ? " ▼" : " ▲") : "";
    public string ArtistHeader => "ARTIST" + SortIndicator(SongSort.Artist);
    public string TitleHeader => "TITLE" + SortIndicator(SongSort.Title);
    public string YearHeader => "YEAR" + SortIndicator(SongSort.Year);
    public string LanguageHeader => "LANGUAGE" + SortIndicator(SongSort.Language);
    public string SourceHeader => "SOURCE" + SortIndicator(SongSort.Source);
    public string RatingHeader => "RATING" + SortIndicator(SongSort.Rating);

    [RelayCommand]
    private void SortBy(SongSort column)
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
        OnPropertyChanged(nameof(SourceHeader));
        OnPropertyChanged(nameof(RatingHeader));
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

    [RelayCommand(CanExecute = nameof(CanLoadIntoGame))]
    private Task LoadIntoGameAsync(LibraryRow? row) => row is null ? Task.CompletedTask : _nowPlaying.LoadCommand.ExecuteAsync(row.Song);

    private bool CanLoadIntoGame(LibraryRow? row) => _nowPlaying.CanLoadSong;

    public string LoadSongTip => _nowPlaying.LoadSongTip;

    public Task PreviewSelectedAsync() => PreviewAsync(Selected);

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnLanguageChanged(string value) => Refresh();
    partial void OnGenreChanged(string value) => Refresh();
    partial void OnRatingChanged(RatingOption? value) => Refresh();

    partial void OnSourceChanged(SourceOption? value)
    {
        if (!_rebuildingSources)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        IReadOnlyList<Song> all = _library.Songs;
        TotalCount = all.Count;
        RefreshFilterValues(all);

        SongQuery query = new()
        {
            Search = Search,
            Language = Language == AnyLanguage ? null : Language,
            Genre = Genre == AnyGenre ? null : Genre,
            Stars = Rating?.Stars,
            SourceIds = SelectedSourceIds(),
            SortBy = Sort,
            Descending = Descending,
        };
        Rows = query.Apply(all, _library.SourceLabel)
            .Select(s => new LibraryRow(s, _library.SourceLabel(s.SourceId), _library.IsAvailable(s.SourceId)))
            .ToList();
        ShownCount = Rows.Count;
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private HashSet<string>? SelectedSourceIds() => Source?.Key switch
    {
        null or SourceOption.AllKey => null,
        SourceOption.LocalKey => _library.Sources.Select(s => s.Id).ToHashSet(),
        string id => [id],
    };

    private void RefreshFilterValues(IReadOnlyList<Song> all)
    {
        Sync(Languages, AnyLanguage, all.Select(s => s.Language).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        Sync(Genres, AnyGenre, all.Select(s => s.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        List<SourceOption> sources = [AllSources];
        if (_library.Sources.Count > 1 || (_library.Sources.Count > 0 && _library.UsdbCount > 0))
        {
            sources.Add(new SourceOption(SourceOption.LocalKey, "All local folders"));
        }

        sources.AddRange(_library.Sources.Select(s => new SourceOption(s.Id, s.Label)));
        if (_library.UsdbCount > 0)
        {
            sources.Add(new SourceOption(UsdbCatalogEntry.SourceId, "USDB"));
        }

        if (!Sources.SequenceEqual(sources))
        {
            SourceOption? keep = Source;
            _rebuildingSources = true;
            Sources.Clear();
            foreach (SourceOption o in sources)
            {
                Sources.Add(o);
            }

            Source = keep is not null && sources.Contains(keep) ? keep : AllSources;
            _rebuildingSources = false;
        }

        if (!Languages.Contains(Language))
        {
            Language = AnyLanguage;
        }

        if (!Genres.Contains(Genre))
        {
            Genre = AnyGenre;
        }
    }

    private static void Sync(ObservableCollection<string> target, string any, IEnumerable<string> values)
    {
        List<string> wanted = [any, .. values];
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
