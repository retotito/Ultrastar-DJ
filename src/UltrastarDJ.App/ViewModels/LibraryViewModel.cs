using System.Globalization;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Library;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>A library row: the song plus what the table shows about its source.</summary>
public sealed record LibraryRow(Song Song, string Source, bool IsAvailable, LoadFailure? Failure = null, SongMark? Mark = null)
{
    public bool IsFavourite => Mark?.Favourite == true;
    /// <summary>Two voices (UltraStar duet) — the icon before the title, the rating filter's "Duets".</summary>
    public bool IsDuet => Song.IsDuet;
    /// <summary>Marked broken by the DJ (bad notes / timing) — still playable, never offered on the songbook.</summary>
    public bool IsBroken => Mark?.Broken == true;
    public string? BrokenTip => IsBroken ? $"Marked as broken{(Mark!.Note is { } n ? $": {n}" : "")}" : null;
    public string FavouriteMenuText => IsFavourite ? "Remove from favourites" : "Favourite";
    public string BrokenMenuText => IsBroken ? "Not broken" : "Mark as broken";
    /// <summary>The last load failed for a reason of the song's own (<see cref="LoadFailureService"/>).</summary>
    public bool HasFailure => Failure is not null;
    public string? FailureTip => Failure is { } f
        ? $"Could not be loaded on {f.AtUtc.ToLocalTime():d MMM, HH:mm}: {f.Reason}\nThe mark goes away once it loads again or the song changes."
        : null;
    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public int? Year => Song.Year;
    public string? Language => Song.Language;
    public string? Genre => Song.Genre;
    public string? Edition => Song.Edition;
    public string? Creator => Song.Creator;
    /// <summary>The file's #BPM; USDB catalog entries without a txt yet have none.</summary>
    public string Bpm => Song.Bpm > 0 ? Song.Bpm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—";
    public bool IsUsdb => Song.UsdbId is not null;
    public bool HasLocalAudio => Song.HasLocalAudio;
    public bool HasLocalVideo => Song.HasLocalVideo;
    /// <summary>USDB songs always play from YouTube; the id is only known once the txt is fetched.</summary>
    public bool HasYouTube => Song.HasYouTube || IsUsdb;
    public double Opacity => IsAvailable ? 1.0 : 0.4;
    /// <summary>Why a greyed row's Preview / queue / load are disabled.</summary>
    public string? UnavailableTip => IsAvailable ? null
        : IsUsdb ? "Offline — USDB songs need the internet" : SourceAvailability.NotConnected(Source);
    /// <summary>USDB popularity; "—" for local songs, empty for USDB songs under 100 views.</summary>
    public string Stars => Song.Stars is { } n ? new string('★', n) : "—";
}

/// <summary>An entry of the rating filter: exactly <see cref="Stars"/> stars, the DJ's favourites or broken songs, or no filter.</summary>
public sealed record RatingOption(int? Stars, string Label, bool Favourites = false, bool Broken = false, bool Duets = false)
{
    /// <summary>The coloured icon of a special entry (favourites, duets, broken) — emoji could not take a colour.</summary>
    public string? Glyph => Favourites ? "favorite" : Duets ? "group" : Broken ? "report" : null;
    public string GlyphBrushKey => Favourites ? "BrushFavourite" : Duets ? "BrushDuet" : "BrushError";
    public bool HasGlyph => Glyph is not null;

    // An em space stands in for the icon where only the text is measured (FitWidestItem sizes the box by ToString).
    public override string ToString() => HasGlyph ? "\u2003 " + Label : Label;
}

/// <summary>An entry of the source filter. <see cref="Key"/> is a source id, or <see cref="AllKey"/>/<see cref="LocalKey"/>.</summary>
// IsAvailable false: an unplugged folder — greyed, still selectable (to see what is on the drive).
public sealed record SourceOption(string Key, string Label, bool IsAvailable = true)
{
    public double Opacity => IsAvailable ? 1.0 : 0.45;
    public const string AllKey = "*";
    public const string LocalKey = "*local";
    public override string ToString() => IsAvailable ? Label : $"{Label} (not connected)";
}

/// <summary>Centre panel: the song library with search, filters and sort. Row actions hand songs to preview, queue or game.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    // The first entry of each filter is "no filter" and is labelled with the filter's name.
    public const string AnyLanguage = "Language";
    public const string AnyGenre = "Genre";
    private static readonly RatingOption AnyRating = new(null, "Rating");
    private static readonly SourceOption AllSources = new(SourceOption.AllKey, "All sources");

    // Clearing Sources makes the ComboBox write null back into Source; that must not re-enter Refresh.
    private bool _rebuildingSources;
    // Same for Language / Genre: rebuilding their lists (a source switched on or off) makes the ComboBoxes write null.
    private bool _rebuildingLists;

    private readonly LibraryService _library;
    private readonly LoadFailureService _failures;
    private readonly SongMarksService _marks;
    private readonly AppSettingsService _settings;
    private bool _showBroken;
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
    /// <summary>Song count per filter entry under the search and the other filters (FacetConverters labels the entries).</summary>
    [ObservableProperty] private SongFacets? _facets;

    public LibraryViewModel(LibraryService library, PreviewViewModel preview, QueueViewModel queue, NowPlayingViewModel nowPlaying, AppSettingsService settings,
        LoadFailureService failures, SongMarksService marks, UsdbTextDownloader texts, UsdbTextStore textStore)
    {
        _texts = texts;
        _textStore = textStore;
        texts.Changed += () => Dispatcher.UIThread.Post(OnTextsChanged);
        textStore.Changed += () => Dispatcher.UIThread.Post(OnTextsChanged);
        _failures = failures;
        _marks = marks;
        _settings = settings;
        failures.Changed += () => Dispatcher.UIThread.Post(Refresh);
        marks.Changed += Refresh;
        // Layout → Show broken songs (the other app settings do not touch the rows; Refresh is cheap enough anyway).
        settings.Changed += OnSettingsChanged;
        _showBroken = settings.ShowBrokenSongs;
        Table = new LibraryTableLayout(settings);
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

    private readonly UsdbTextDownloader _texts;
    private readonly UsdbTextStore _textStore;

    // ── Footer: USDB song texts downloading for offline use (Song Sources → USDB) ──

    /// <summary>Shown while texts are still missing for the chosen mode (downloading, paused or waiting).</summary>
    public bool TextsVisible => _texts.Target > 0 && !_texts.IsComplete;
    public bool TextsActive => _texts.IsActive;
    public string TextsText => string.Create(CultureInfo.CurrentCulture,
        $"Song texts {_texts.PercentText} ({_texts.Done:N0} / {_texts.Target:N0})") + (_texts.IsActive ? "" : _textStore.Paused ? " · paused" : " · waiting");
    public string TextsTip => _texts.IsActive
        ? string.Create(CultureInfo.CurrentCulture, $"Downloading USDB song texts for offline use — about {Hours(_texts.Remaining)} left. Click for Song Sources.")
        : _textStore.Paused ? "Song text download paused — resume it under Song Sources"
        : "Song text download waits for USDB (offline, not logged in or syncing)";

    private static string Hours(TimeSpan t) => t.TotalHours >= 1 ? $"{t.TotalHours:0.#} h" : $"{Math.Max(1, Math.Ceiling(t.TotalMinutes)):0} min";

    private void OnTextsChanged()
    {
        OnPropertyChanged(nameof(TextsVisible));
        OnPropertyChanged(nameof(TextsActive));
        OnPropertyChanged(nameof(TextsText));
        OnPropertyChanged(nameof(TextsTip));
    }

    /// <summary>Replaced wholesale on every refresh: 27k USDB rows through ObservableCollection.Add would stall the UI.</summary>
    [ObservableProperty] private IReadOnlyList<LibraryRow> _rows = [];
    public ObservableCollection<string> Languages { get; } = [AnyLanguage];
    public ObservableCollection<string> Genres { get; } = [AnyGenre];
    /// <summary>A search or any of rating/language/genre/source set: Clear is active (it clears them all).</summary>
    public bool HasActiveFilters => Search.Length > 0 || Rating?.Stars is not null || Rating?.Favourites == true || Rating?.Broken == true || Rating?.Duets == true || Language != AnyLanguage || Genre != AnyGenre
        || Source?.Key is not (null or SourceOption.AllKey);

    [RelayCommand]
    private void ClearSearch() => Search = "";

    [RelayCommand(CanExecute = nameof(HasActiveFilters))]
    private void ClearFilters()
    {
        Search = "";
        Rating = AnyRating;
        Language = AnyLanguage;
        Genre = AnyGenre;
        Source = AllSources;
    }

    private static readonly RatingOption[] BaseRatings = [AnyRating, new(null, "Favourites", Favourites: true), new(null, "Duets", Duets: true), new(1, "★"), new(2, "★★"), new(3, "★★★"), new(4, "★★★★")];
    private static readonly RatingOption BrokenRating = new(null, "Broken", Broken: true);

    /// <summary>"Broken" only while broken songs are shown (Layout): hidden songs cannot be filtered on.</summary>
    public IReadOnlyList<RatingOption> Ratings => _showBroken ? [.. BaseRatings, BrokenRating] : BaseRatings;

    private void OnSettingsChanged()
    {
        if (_showBroken == _settings.ShowBrokenSongs)
        {
            return;
        }

        // Swapping the list makes the ComboBox drop its selection: keep it, unless it was "Broken" and that is gone.
        _showBroken = _settings.ShowBrokenSongs;
        RatingOption keep = Rating is { Broken: true } && !_showBroken ? AnyRating : Rating ?? AnyRating;
        OnPropertyChanged(nameof(Ratings));
        Rating = keep;
        OnPropertyChanged(nameof(Rating));
        Refresh();
    }
    public ObservableCollection<SourceOption> Sources { get; } = [AllSources];

    public string SortIndicator(SongSort column) => Sort == column ? (Descending ? " ▼" : " ▲") : "";
    public string ArtistHeader => "ARTIST" + SortIndicator(SongSort.Artist);
    public string TitleHeader => "TITLE" + SortIndicator(SongSort.Title);
    public string YearHeader => "YEAR" + SortIndicator(SongSort.Year);
    public string LanguageHeader => "LANGUAGE" + SortIndicator(SongSort.Language);
    public string SourceHeader => "SOURCE" + SortIndicator(SongSort.Source);
    public string RatingHeader => "RATING" + SortIndicator(SongSort.Rating);
    public string GenreHeader => "GENRE" + SortIndicator(SongSort.Genre);
    public string EditionHeader => "EDITION" + SortIndicator(SongSort.Edition);
    public string CreatorHeader => "CREATOR" + SortIndicator(SongSort.Creator);
    public string BpmHeader => "BPM" + SortIndicator(SongSort.Bpm);

    /// <summary>Column widths and visibility (Layout panel).</summary>
    public LibraryTableLayout Table { get; }

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
        OnPropertyChanged(nameof(GenreHeader));
        OnPropertyChanged(nameof(EditionHeader));
        OnPropertyChanged(nameof(CreatorHeader));
        OnPropertyChanged(nameof(BpmHeader));
        Refresh();
    }

    /// <summary>Double-click / Enter.</summary>
    [RelayCommand(CanExecute = nameof(IsPlayable))]
    private Task PreviewAsync(LibraryRow? row) => row is not { IsAvailable: true } ? Task.CompletedTask : _preview.LoadCommand.ExecuteAsync(row.Song);

    // Greyed rows (drive unplugged, USDB offline) can't be previewed, queued or loaded; Details stays.
    private static bool IsPlayable(LibraryRow? row) => row is { IsAvailable: true };

    /// <summary>The DJ window shows the Details popup (it owns the overlay).</summary>
    public event Action<LibraryRow>? DetailsRequested;

    [RelayCommand]
    private void ShowDetails(LibraryRow? row)
    {
        if (row is not null)
        {
            DetailsRequested?.Invoke(row);
        }
    }

    [RelayCommand]
    private void ToggleFavourite(LibraryRow? row)
    {
        if (row is not null)
        {
            _marks.SetFavourite(row.Song, !row.IsFavourite);
        }
    }

    [RelayCommand]
    private void ToggleBroken(LibraryRow? row)
    {
        if (row is not null)
        {
            _marks.SetBroken(row.Song, !row.IsBroken);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPlayable))]
    private void AddToQueue(LibraryRow? row)
    {
        if (row is not null)
        {
            _queue.Add(row.Song);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadIntoGame))]
    private Task LoadIntoGameAsync(LibraryRow? row) => row is null ? Task.CompletedTask : _nowPlaying.LoadCommand.ExecuteAsync(row.Song);

    private bool CanLoadIntoGame(LibraryRow? row) => _nowPlaying.CanLoadSong && IsPlayable(row);

    public string LoadSongTip => _nowPlaying.LoadSongTip;

    public Task PreviewSelectedAsync() => PreviewAsync(Selected);

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnLanguageChanged(string value)
    {
        if (!_rebuildingLists)
        {
            Refresh();
        }
    }

    partial void OnGenreChanged(string value)
    {
        if (!_rebuildingLists)
        {
            Refresh();
        }
    }
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
            FavouritesOnly = Rating?.Favourites == true,
            FavouriteIds = _marks.FavouriteIds,
            BrokenOnly = Rating?.Broken == true,
            DuetsOnly = Rating?.Duets == true,
            BrokenIds = _marks.BrokenIds,
            Hidden = _showBroken ? null : _marks.BrokenIds,
            SourceIds = SelectedSourceIds(),
            SortBy = Sort,
            Descending = Descending,
        };
        Facets = SongFacets.Of(all, query);
        Rows = query.Apply(all, _library.SourceLabel)
            .Select(s => new LibraryRow(s, _library.SourceLabel(s.SourceId), _library.IsAvailable(s.SourceId), _failures.For(s), _marks.For(s)))
            .ToList();
        ShownCount = Rows.Count;
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.NotifyCanExecuteChanged();
    }

    private HashSet<string>? SelectedSourceIds() => Source?.Key switch
    {
        null or SourceOption.AllKey => null,
        SourceOption.LocalKey => _library.Sources.Select(s => s.Id).ToHashSet(),
        string id => [id],
    };

    private void RefreshFilterValues(IReadOnlyList<Song> all)
    {
        // Each language / genre once, never "French, English" (ValueList, as usdb_syncer). Rebuilding a list makes its
        // ComboBox write null into the filter (shown as an empty selection): keep the choice, restore it afterwards —
        // or "no filter" if that value is gone with a switched-off source — and re-announce it so the box shows it.
        string keepLanguage = Language;
        string keepGenre = Genre;
        _rebuildingLists = true;
        bool rebuilt = Sync(Languages, AnyLanguage, ValueList.Distinct(all.Select(s => s.Language), ValueList.LanguageSeparators))
            | Sync(Genres, AnyGenre, ValueList.Distinct(all.Select(s => s.Genre), ValueList.GenreSeparators));
        if (rebuilt)
        {
            Language = keepLanguage is not null && Languages.Contains(keepLanguage) ? keepLanguage : AnyLanguage;
            Genre = keepGenre is not null && Genres.Contains(keepGenre) ? keepGenre : AnyGenre;
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(Genre));
        }

        _rebuildingLists = false;
        List<SourceOption> sources = [AllSources];
        int folders = _library.Sources.Count(s => s.Enabled);
        if (folders > 1 || (folders > 0 && _library.UsdbCount > 0))
        {
            sources.Add(new SourceOption(SourceOption.LocalKey, "All local folders"));
        }

        // Switched-off folders are not in the library, so not in the filter either.
        sources.AddRange(_library.Sources.Where(s => s.Enabled).Select(s => new SourceOption(s.Id, s.Label, _library.IsAvailable(s.Id))));
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

            // By key: a drive coming or going changes the option (greyed), not the DJ's choice.
            Source = sources.FirstOrDefault(o => o.Key == keep?.Key) ?? AllSources;
            _rebuildingSources = false;
        }

    }

    /// <returns>Whether the list changed (its ComboBox then lost its selection).</returns>
    private static bool Sync(ObservableCollection<string> target, string any, IEnumerable<string> values)
    {
        List<string> wanted = [any, .. values];
        if (target.SequenceEqual(wanted))
        {
            return false;
        }

        target.Clear();
        foreach (string v in wanted)
        {
            target.Add(v);
        }

        return true;
    }
}
