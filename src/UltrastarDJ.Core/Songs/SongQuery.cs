namespace UltrastarDJ.Core.Songs;

public enum SongSort
{
    Artist,
    Title,
    Year,
    Language,
    Source,
    /// <summary>By USDB views; local songs (no views) count as lowest.</summary>
    Rating,
}

/// <summary>
/// Library search, filters and sort. Text comparisons ignore case. Ties break by artist (or by title when
/// sorting by artist), always ascending.
/// </summary>
public sealed record SongQuery
{
    /// <summary>Substring of title or artist; empty matches everything.</summary>
    public string Search { get; init; } = "";
    /// <summary><c>null</c> = any language.</summary>
    public string? Language { get; init; }
    /// <summary><c>null</c> = any genre.</summary>
    public string? Genre { get; init; }
    /// <summary>Song sources to keep (<see cref="Song.SourceId"/>); <c>null</c> = all sources.</summary>
    public IReadOnlySet<string>? SourceIds { get; init; }
    /// <summary>Exact <see cref="Song.Stars"/>; songs without stars (local) are dropped. <c>null</c> = no rating filter.</summary>
    public int? Stars { get; init; }
    public SongSort SortBy { get; init; } = SongSort.Artist;
    public bool Descending { get; init; }

    /// <param name="songs">The whole library.</param>
    /// <param name="sourceLabel">Display name of a source id; sorting by source orders by this, not by the id.</param>
    public IEnumerable<Song> Apply(IEnumerable<Song> songs, Func<string, string> sourceLabel)
    {
        string q = Search.Trim();
        IEnumerable<Song> filtered = songs;
        if (q.Length > 0)
        {
            filtered = filtered.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (Language is not null)
        {
            filtered = filtered.Where(s => string.Equals(s.Language, Language, StringComparison.OrdinalIgnoreCase));
        }

        if (Genre is not null)
        {
            filtered = filtered.Where(s => string.Equals(s.Genre, Genre, StringComparison.OrdinalIgnoreCase));
        }

        if (Stars is { } stars)
        {
            filtered = filtered.Where(s => s.Stars == stars);
        }

        if (SourceIds is not null)
        {
            filtered = filtered.Where(s => SourceIds.Contains(s.SourceId));
        }

        IOrderedEnumerable<Song> sorted = SortBy switch
        {
            SongSort.Title => Order(filtered, s => s.Title),
            SongSort.Year => Order(filtered, s => s.Year ?? 0),
            SongSort.Language => Order(filtered, s => s.Language ?? ""),
            SongSort.Source => Order(filtered, s => sourceLabel(s.SourceId)),
            SongSort.Rating => Order(filtered, s => s.UsdbViews ?? -1),
            _ => Order(filtered, s => s.Artist),
        };

        return SortBy == SongSort.Artist
            ? sorted.ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            : sorted.ThenBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
    }

    private IOrderedEnumerable<Song> Order<TKey>(IEnumerable<Song> songs, Func<Song, TKey> key)
    {
        IComparer<TKey>? cmp = typeof(TKey) == typeof(string) ? (IComparer<TKey>)StringComparer.OrdinalIgnoreCase : null;
        return Descending ? songs.OrderByDescending(key, cmp) : songs.OrderBy(key, cmp);
    }
}
