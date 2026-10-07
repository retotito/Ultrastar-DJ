using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;
using UltrastarDJ.Infrastructure.Usdb;

namespace UltrastarDJ.App.ViewModels;

/// <summary>One "label: value" line of the Details popup.</summary>
public sealed record DetailField(string Label, string Value);

/// <summary>A file the song refers to, and whether it is there.</summary>
public sealed record DetailFile(string Label, string Path, bool Exists)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Glyph => Exists ? "check_circle" : "cancel";
}

/// <summary>
/// The Details popup of a library song: its tags, a summary of its notes (length, duet, golden share, pitch range),
/// the files it refers to, problems with its file, and the .txt itself. Local songs read their file; USDB songs
/// fetch their text like Preview does (cached), so this needs the network only the first time.
/// </summary>
public sealed partial class SongDetailsViewModel : ViewModelBase
{
    private readonly LibraryViewModel _library;
    private readonly UsdbService _usdb;
    private readonly ThumbnailService _thumbnails;
    private readonly ConnectivityService _connectivity;
    private readonly SongMarksService _marks;
    private readonly Action _close;

    [ObservableProperty] private bool _loading = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private Bitmap? _picture;
    [ObservableProperty] private string _txt = "";
    [ObservableProperty] private IReadOnlyList<DetailField> _fields = [];
    [ObservableProperty] private IReadOnlyList<DetailField> _summary = [];
    [ObservableProperty] private IReadOnlyList<DetailFile> _files = [];
    [ObservableProperty] private IReadOnlyList<string> _problems = [];
    [ObservableProperty] private string? _youTubeUrl;
    // The DJ's marks (SongMarksService): saved as they change.
    [ObservableProperty] private bool _isFavourite;
    [ObservableProperty] private bool _isBroken;
    [ObservableProperty] private string _note = "";

    public SongDetailsViewModel(LibraryRow row, LibraryViewModel library, UsdbService usdb, ThumbnailService thumbnails,
        ConnectivityService connectivity, SongMarksService marks, Action close)
    {
        Row = row;
        _marks = marks;
        _isFavourite = row.IsFavourite;
        _isBroken = row.IsBroken;
        _note = row.Mark?.Note ?? "";
        _library = library;
        _usdb = usdb;
        _thumbnails = thumbnails;
        _connectivity = connectivity;
        _close = close;
        Fields = FieldsOf(row.Song);
        // "Load into game" follows the library's rule (not while a song is running).
        _library.LoadIntoGameCommand.CanExecuteChanged += (_, _) => LoadIntoGameCommand.NotifyCanExecuteChanged();
        _ = LoadAsync();
    }

    public LibraryRow Row { get; }
    /// <summary>The note field shows for a broken song, or when a note was kept after "broken" was switched off.</summary>
    public bool ShowNote => IsBroken || Note.Length > 0;

    partial void OnIsFavouriteChanged(bool value) => _marks.SetFavourite(Row.Song, value);

    partial void OnIsBrokenChanged(bool value)
    {
        _marks.SetBroken(Row.Song, value);
        OnPropertyChanged(nameof(ShowNote));
    }

    partial void OnNoteChanged(string value)
    {
        _marks.SetNote(Row.Song, value);
        OnPropertyChanged(nameof(ShowNote));
    }
    public string Title => Row.Title;
    public string Artist => Row.Artist;
    public string Source => Row.Source;
    public bool IsUsdb => Row.IsUsdb;
    public string? UsdbUrl => Row.Song.UsdbId is { } id ? $"{UsdbHtml.BaseUrl}/index.php?link=detail&id={id}" : null;
    /// <summary>The song's folder (local songs): Show in Finder reveals the .txt there.</summary>
    public string? TxtPath => Row.IsUsdb ? null : Row.Song.TxtPath;
    public string? Folder => TxtPath is { } p ? Path.GetDirectoryName(p) : null;
    public bool HasFiles => Files.Count > 0;
    public bool HasProblems => Problems.Count > 0;
    /// <summary>Checked and clean: say so, instead of leaving the reader to wonder whether it was checked.</summary>
    public bool NoProblems => !Loading && Problems.Count == 0 && (HasTxt || !IsUsdb);
    public bool HasTxt => Txt.Length > 0;
    public bool HasYouTube => YouTubeUrl is not null;
    public LibraryViewModel Library => _library;

    partial void OnFilesChanged(IReadOnlyList<DetailFile> value) => OnPropertyChanged(nameof(HasFiles));
    partial void OnProblemsChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(NoProblems));
    }

    partial void OnLoadingChanged(bool value) => OnPropertyChanged(nameof(NoProblems));
    partial void OnTxtChanged(string value) => OnPropertyChanged(nameof(HasTxt));
    partial void OnYouTubeUrlChanged(string? value) => OnPropertyChanged(nameof(HasYouTube));

    [RelayCommand]
    private void Close() => _close();

    // Greyed song (drive unplugged, USDB offline): Details only, like in the library's menus.
    private bool IsPlayable() => Row.IsAvailable;

    [RelayCommand(CanExecute = nameof(IsPlayable))]
    private async Task PreviewAsync()
    {
        _close();
        await _library.PreviewCommand.ExecuteAsync(Row);
    }

    [RelayCommand(CanExecute = nameof(IsPlayable))]
    private void AddToQueue()
    {
        _close();
        _library.AddToQueueCommand.Execute(Row);
    }

    public string LoadTip => Row.UnavailableTip ?? _library.LoadSongTip;
    public string? UnavailableTip => Row.UnavailableTip;

    [RelayCommand(CanExecute = nameof(CanLoadIntoGame))]
    private async Task LoadIntoGameAsync()
    {
        _close();
        await _library.LoadIntoGameCommand.ExecuteAsync(Row);
    }

    private bool CanLoadIntoGame() => _library.LoadIntoGameCommand.CanExecute(Row);

    private async Task LoadAsync()
    {
        Song song = Row.Song;
        string? text = null;
        try
        {
            if (song.UsdbId is { } id)
            {
                if (_connectivity.IsOnline == false)
                {
                    Status = "Offline — the song text comes from USDB and could not be loaded.";
                }
                else
                {
                    Status = "Loading the song text from USDB…";
                    text = await _usdb.GetSongTxtAsync(id, song.UsdbMtime);
                    song = WithHeader(song, UltraStarParser.ParseHeader(text));
                }
            }
            else if (song.TxtPath is { } path)
            {
                text = await File.ReadAllTextAsync(path);
            }
        }
        catch (Exception ex) when (ex is UsdbException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Status = $"The song text could not be loaded: {ex.Message}";
        }

        IReadOnlyList<NoteTrack>? tracks = text is null ? null : UltraStarParser.ParseNotes(text);
        FileSystemExistence fs = new();
        Fields = FieldsOf(song);
        Summary = tracks is { Count: > 0 } ? SummaryOf(song, tracks, text!) : [];
        Files = Row.IsUsdb ? [] : FilesOf(song, fs);
        // A USDB song's problems are about its text only; it has no local files to miss.
        List<string> problems = text is null && Row.IsUsdb ? [] : [.. SongCheck.Problems(song, tracks, fs)];
        if (Row.Failure is { } failed)
        {
            problems.Insert(0, $"Could not be loaded on {failed.AtUtc.ToLocalTime():d MMM, HH:mm}: {failed.Reason}");
        }

        Problems = problems;
        Txt = text ?? "";
        YouTubeUrl = song.YouTubeId is { } yt ? $"https://www.youtube.com/watch?v={yt}" : null;
        if (text is not null)
        {
            Status = "";
        }

        Loading = false;
        Picture = await SongImages.PictureAsync(song, _thumbnails);
    }

    private static Song WithHeader(Song song, SongHeader h) => song with
    {
        Bpm = h.Bpm ?? song.Bpm,
        GapMs = h.GapMs ?? song.GapMs,
        YouTubeId = h.YouTubeId ?? song.YouTubeId,
        VideoGapSec = h.VideoGapSec,
        StartSec = h.StartSec,
        EndMs = h.EndMs,
        Year = h.Year ?? song.Year,
        Language = h.Language ?? song.Language,
        Genre = h.Genre ?? song.Genre,
        Edition = h.Edition ?? song.Edition,
        Creator = h.Creator ?? song.Creator,
        Comment = h.Comment ?? song.Comment,
    };

    private static IReadOnlyList<DetailField> FieldsOf(Song s)
    {
        List<DetailField> f = [];
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                f.Add(new DetailField(label, value));
            }
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        Add("Year", s.Year?.ToString(inv));
        Add("Language", s.Language);
        Add("Genre", s.Genre);
        Add("Edition", s.Edition);
        Add("Creator", s.Creator);
        Add("Comment", s.Comment);
        Add("BPM", s.Bpm > 0 ? s.Bpm.ToString("0.##", inv) : null);
        Add("GAP", s.GapMs != 0 ? $"{s.GapMs.ToString("0", inv)} ms" : null);
        Add("VIDEOGAP", s.VideoGapSec is { } vg and not 0 ? $"{vg.ToString("0.###", inv)} s" : null);
        Add("START", s.StartSec is { } st ? $"{st.ToString("0.###", inv)} s" : null);
        Add("END", s.EndMs is { } end ? $"{end.ToString("0", inv)} ms" : null);
        Add("USDB id", s.UsdbId?.ToString(inv));
        Add("USDB views", s.UsdbViews is { } v ? $"{v.ToString("N0", inv)}{(s.Stars is { } n and > 0 ? "  " + new string('★', n) : "")}" : null);
        return f;
    }

    private static IReadOnlyList<DetailField> SummaryOf(Song song, IReadOnlyList<NoteTrack> tracks, string text)
    {
        SongSummary s = SongSummary.Of(song, tracks);
        List<DetailField> f = [new("Singing ends at", TimeSpan.FromSeconds(s.SingingEndsSec).ToString(s.SingingEndsSec >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture))];
        f.Add(new("Voices", s.IsDuet ? DuetText(text) : "Solo"));
        f.Add(new("Phrases / notes", $"{s.Phrases} / {s.Notes}"));
        f.Add(new("Golden", s.GoldenShare.ToString("P0", CultureInfo.InvariantCulture)));
        if (s.RapNotes > 0)
        {
            f.Add(new("Rap notes", s.RapNotes.ToString(CultureInfo.InvariantCulture)));
        }

        if (s.FreestyleNotes > 0)
        {
            f.Add(new("Freestyle notes", s.FreestyleNotes.ToString(CultureInfo.InvariantCulture)));
        }

        if (s.LowestNote is { } low && s.HighestNote is { } high)
        {
            f.Add(new("Pitch range", $"{low} – {high}"));
        }

        return f;
    }

    // "#P1:" / "#P2:" (newer files) or "#DUETSINGERP1:" (older) name the two voices.
    private static string DuetText(string text)
    {
        string? p1 = null, p2 = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('#'))
            {
                if (line.Length > 0)
                {
                    break;
                }

                continue;
            }

            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string tag = line[1..colon].ToUpperInvariant();
            string value = line[(colon + 1)..].Trim();
            if (tag is "P1" or "DUETSINGERP1")
            {
                p1 = value;
            }
            else if (tag is "P2" or "DUETSINGERP2")
            {
                p2 = value;
            }
        }

        return p1 is not null && p2 is not null ? $"Duet — {p1} & {p2}" : "Duet";
    }

    private static IReadOnlyList<DetailFile> FilesOf(Song s, IFileExistence fs)
    {
        List<DetailFile> files = [];
        void Add(string label, string? path)
        {
            if (!string.IsNullOrEmpty(path) && !path.Contains("://", StringComparison.Ordinal))
            {
                files.Add(new DetailFile(label, path, fs.Exists(path)));
            }
        }

        Add(".txt", s.TxtPath);
        Add("Audio", s.AudioPath);
        Add("Video", s.VideoPath);
        Add("Cover", s.CoverPath);
        Add("Background", s.BackgroundPath);
        return files;
    }
}
