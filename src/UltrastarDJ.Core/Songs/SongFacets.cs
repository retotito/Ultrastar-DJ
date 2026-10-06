namespace UltrastarDJ.Core.Songs;

/// <summary>
/// How many songs each filter entry would show — given the search and all the <em>other</em> filters (faceted
/// search). "French (1,812)" means: with this search, rating and source, picking French shows 1,812 songs.
/// One pass over the library; every song counts once per language / genre it lists.
/// </summary>
public sealed record SongFacets(
    IReadOnlyDictionary<string, int> Languages,
    int LanguageTotal,
    IReadOnlyDictionary<string, int> Genres,
    int GenreTotal,
    IReadOnlyDictionary<int, int> Stars,
    int StarsTotal,
    IReadOnlyDictionary<string, int> Sources,
    int SourcesTotal)
{
    public static SongFacets Of(IEnumerable<Song> songs, SongQuery query)
    {
        Dictionary<string, int> languages = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> genres = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, int> stars = [];
        Dictionary<string, int> sources = new(StringComparer.Ordinal);
        int languageTotal = 0, genreTotal = 0, starsTotal = 0, sourcesTotal = 0;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Song s in songs)
        {
            if (!query.MatchesSearch(s))
            {
                continue;
            }

            bool lang = query.MatchesLanguage(s), genre = query.MatchesGenre(s), star = query.MatchesStars(s), source = query.MatchesSource(s);
            if (genre && star && source)
            {
                languageTotal++;
                Count(languages, ValueList.Split(s.Language, ValueList.LanguageSeparators), seen);
            }

            if (lang && star && source)
            {
                genreTotal++;
                Count(genres, ValueList.Split(s.Genre, ValueList.GenreSeparators), seen);
            }

            if (lang && genre && source)
            {
                starsTotal++;
                if (s.Stars is { } n)
                {
                    stars[n] = stars.GetValueOrDefault(n) + 1;
                }
            }

            if (lang && genre && star)
            {
                sourcesTotal++;
                sources[s.SourceId] = sources.GetValueOrDefault(s.SourceId) + 1;
            }
        }

        return new SongFacets(languages, languageTotal, genres, genreTotal, stars, starsTotal, sources, sourcesTotal);
    }

    // A song listing "English, english" counts once for English.
    private static void Count(Dictionary<string, int> into, IReadOnlyList<string> values, HashSet<string> seen)
    {
        seen.Clear();
        foreach (string v in values)
        {
            if (seen.Add(v))
            {
                into[v] = into.GetValueOrDefault(v) + 1;
            }
        }
    }
}
