using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Services;

namespace UltrastarDJ.App.Library;

/// <summary>
/// What the library table's header and rows bind their column definitions to: a hidden column gets width 0
/// (and its cells are hidden), so header and rows stay aligned whatever the Layout panel switches off.
/// </summary>
public sealed class LibraryTableLayout : ObservableObject
{
    // Left and right padding of the header row and the list items (12 + 12, plus the list's 4 + 4).
    private const double RowPadding = 32;

    private readonly AppSettingsService _settings;
    private IReadOnlySet<LibraryColumn> _visible;

    public LibraryTableLayout(AppSettingsService settings)
    {
        _settings = settings;
        _visible = settings.VisibleColumns;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised when the columns change (the view recomputes the table width).</summary>
    public event Action? Changed;

    public bool ShowArtist => _visible.Contains(LibraryColumn.Artist);
    public GridLength ArtistWidth => WidthOf(LibraryColumn.Artist);

    public bool ShowYear => _visible.Contains(LibraryColumn.Year);
    public GridLength YearWidth => WidthOf(LibraryColumn.Year);

    public bool ShowLanguage => _visible.Contains(LibraryColumn.Language);
    public GridLength LanguageWidth => WidthOf(LibraryColumn.Language);

    public bool ShowGenre => _visible.Contains(LibraryColumn.Genre);
    public GridLength GenreWidth => WidthOf(LibraryColumn.Genre);

    public bool ShowEdition => _visible.Contains(LibraryColumn.Edition);
    public GridLength EditionWidth => WidthOf(LibraryColumn.Edition);

    public bool ShowCreator => _visible.Contains(LibraryColumn.Creator);
    public GridLength CreatorWidth => WidthOf(LibraryColumn.Creator);

    public bool ShowBpm => _visible.Contains(LibraryColumn.Bpm);
    public GridLength BpmWidth => WidthOf(LibraryColumn.Bpm);

    public bool ShowRating => _visible.Contains(LibraryColumn.Rating);
    public GridLength RatingWidth => WidthOf(LibraryColumn.Rating);

    public bool ShowSource => _visible.Contains(LibraryColumn.Source);
    public GridLength SourceWidth => WidthOf(LibraryColumn.Source);

    public bool ShowMedia => _visible.Contains(LibraryColumn.Media);
    public GridLength MediaWidth => WidthOf(LibraryColumn.Media);

    public double TitleMinWidth => LibraryColumns.TitleMinWidth;
    public double ArtistMinWidth => ShowArtist ? LibraryColumns.ArtistMinWidth : 0;
    public double ActionsWidth => LibraryColumns.ActionsWidth;

    /// <summary>Narrowest the table can get before it scrolls horizontally.</summary>
    public double MinWidth
    {
        get
        {
            double w = RowPadding + LibraryColumns.TitleMinWidth + LibraryColumns.ActionsWidth + ArtistMinWidth;
            foreach (LibraryColumn c in _visible)
            {
                if (c != LibraryColumn.Artist)
                {
                    w += LibraryColumns.Width(c).Value;
                }
            }

            return w;
        }
    }

    private GridLength WidthOf(LibraryColumn c) => _visible.Contains(c) ? LibraryColumns.Width(c) : new GridLength(0);

    private void OnSettingsChanged()
    {
        IReadOnlySet<LibraryColumn> visible = _settings.VisibleColumns;
        if (visible.SetEquals(_visible))
        {
            return;
        }

        _visible = visible;
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }
}
