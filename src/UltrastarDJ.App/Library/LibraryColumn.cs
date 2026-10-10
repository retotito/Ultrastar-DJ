using UltrastarDJ.App.Localization;
using Avalonia.Controls;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Library;

/// <summary>
/// The library table's optional columns (Title is always shown), toggled in the Layout panel and persisted in
/// the app settings. Widths are fixed; Title and Artist share what is left, and when the visible columns need
/// more than the window, the table scrolls horizontally instead of squeezing them.
/// </summary>
public enum LibraryColumn
{
    Artist,
    Year,
    Language,
    Genre,
    Edition,
    Creator,
    Bpm,
    Rating,
    Source,
    Media,
}

public static class LibraryColumns
{
    public static readonly IReadOnlyList<LibraryColumn> All = Enum.GetValues<LibraryColumn>();

    /// <summary>Shown until the DJ changes it: the columns the table always had, plus Genre.</summary>
    public static readonly IReadOnlySet<LibraryColumn> DefaultVisible = new HashSet<LibraryColumn>
    {
        LibraryColumn.Artist, LibraryColumn.Year, LibraryColumn.Language, LibraryColumn.Genre,
        LibraryColumn.Rating, LibraryColumn.Source, LibraryColumn.Media,
    };

    public const double TitleMinWidth = 180;
    public const double ArtistMinWidth = 140;
    public const double ActionsWidth = 36;

    /// <summary>Fixed width of a column; Artist is a star column (share of the rest) with <see cref="ArtistMinWidth"/>.</summary>
    public static GridLength Width(LibraryColumn c) => c switch
    {
        LibraryColumn.Artist => new GridLength(2, GridUnitType.Star),
        LibraryColumn.Year => new GridLength(60),
        // Room for "Japanese (romanized)" — the longest common value.
        LibraryColumn.Language => new GridLength(170),
        LibraryColumn.Genre => new GridLength(180),
        LibraryColumn.Edition => new GridLength(160),
        LibraryColumn.Creator => new GridLength(130),
        LibraryColumn.Bpm => new GridLength(60),
        LibraryColumn.Rating => new GridLength(70),
        LibraryColumn.Source => new GridLength(90),
        LibraryColumn.Media => new GridLength(60),
        _ => new GridLength(100),
    };

    public static string Label(LibraryColumn c) => L.T("column_name." + c);

    /// <summary>The sort a header click applies; null = not sortable (media icons).</summary>
    public static SongSort? Sort(LibraryColumn c) => c switch
    {
        LibraryColumn.Artist => SongSort.Artist,
        LibraryColumn.Year => SongSort.Year,
        LibraryColumn.Language => SongSort.Language,
        LibraryColumn.Genre => SongSort.Genre,
        LibraryColumn.Edition => SongSort.Edition,
        LibraryColumn.Creator => SongSort.Creator,
        LibraryColumn.Bpm => SongSort.Bpm,
        LibraryColumn.Rating => SongSort.Rating,
        LibraryColumn.Source => SongSort.Source,
        _ => null,
    };
}
