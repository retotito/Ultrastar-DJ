using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Songbook;

/// <param name="Id"><see cref="Song.Id"/> — what a request names.</param>
/// <param name="Title">Song title.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Year">Release year, 0 = unknown.</param>
/// <param name="Languages">Indexes into <see cref="SongbookCatalog.Languages"/>.</param>
/// <param name="Genres">Indexes into <see cref="SongbookCatalog.Genres"/>.</param>
/// <param name="Stars">USDB popularity 1–4, 0 = not rated (local songs, few views).</param>
/// <param name="Usdb">From the USDB catalog (played from YouTube).</param>
/// <param name="YouTubeId">Known for local songs with a YouTube link; USDB songs fetch theirs on demand.</param>
public sealed record SongbookEntry(string Id, string Title, string Artist, int Year, int[] Languages, int[] Genres, int Stars, bool Usdb, string? YouTubeId);

/// <summary>
/// The library as guests' phones get it — once, then they search and filter locally, so the party does not send a
/// request per keystroke. Languages and genres are listed once and referenced by index (the payload stays small:
/// ~30 000 songs ≈ 0.5 MB compressed). Sorted by artist, then title, like the DJ's library.
/// </summary>
public sealed record SongbookCatalog(IReadOnlyList<string> Languages, IReadOnlyList<string> Genres, IReadOnlyList<SongbookEntry> Songs)
{
    public static SongbookCatalog Build(IEnumerable<Song> songs)
    {
        List<Song> sorted = [.. songs.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string> languages = ValueList.Distinct(sorted.Select(s => s.Language), ValueList.LanguageSeparators);
        IReadOnlyList<string> genres = ValueList.Distinct(sorted.Select(s => s.Genre), ValueList.GenreSeparators);
        Dictionary<string, int> languageIndex = Index(languages);
        Dictionary<string, int> genreIndex = Index(genres);

        List<SongbookEntry> entries = new(sorted.Count);
        foreach (Song s in sorted)
        {
            entries.Add(new SongbookEntry(s.Id, s.Title, s.Artist, s.Year ?? 0,
                Indexes(ValueList.Split(s.Language, ValueList.LanguageSeparators), languageIndex),
                Indexes(ValueList.Split(s.Genre, ValueList.GenreSeparators), genreIndex),
                s.Stars ?? 0, s.UsdbId is not null, s.HasYouTube ? s.YouTubeId : null));
        }

        return new SongbookCatalog(languages, genres, entries);
    }

    private static Dictionary<string, int> Index(IReadOnlyList<string> values)
    {
        Dictionary<string, int> index = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Count; i++)
        {
            index[values[i]] = i;
        }

        return index;
    }

    private static int[] Indexes(IReadOnlyList<string> values, Dictionary<string, int> index)
        => [.. values.Select(v => index[v]).Distinct()];
}
