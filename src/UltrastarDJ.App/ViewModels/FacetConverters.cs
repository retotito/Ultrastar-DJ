using Avalonia.Data.Converters;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// Library filter entries with their song count (<see cref="SongFacets"/>): "French (1,812)". Bound as a MultiBinding of
/// the entry and the view model's current facets, so every search or filter change re-labels all entries.
/// ConverterParameter names the filter: language, genre, rating or source.
/// </summary>
public static class FacetConverters
{
    public static readonly IMultiValueConverter Label = new FacetConverter(opacity: false);

    /// <summary>Entries that would show nothing (and unplugged sources) are greyed, still selectable.</summary>
    public static readonly IMultiValueConverter Opacity = new FacetConverter(opacity: true);

    // Values: [entry, facets]; parameter: the filter's name.
    private sealed class FacetConverter(bool opacity) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            object? entry = values.Count > 0 ? values[0] : null;
            if (entry is null)
            {
                return opacity ? 1.0 : "";
            }

            int count = Count(entry, values.Count > 1 ? values[1] as SongFacets : null, parameter as string);
            if (!opacity)
            {
                return $"{entry} ({count.ToString("N0", culture)})";
            }

            double greyed = entry is SourceOption { IsAvailable: false } ? 0.45 : 1.0;
            return count == 0 ? Math.Min(greyed, 0.4) : greyed;
        }
    }

    // The first entry of each filter ("Language", "Rating", "All sources") means no filter: the facet's total.
    private static int Count(object entry, SongFacets? f, string? kind)
    {
        if (f is null)
        {
            return 0;
        }

        return (kind, entry) switch
        {
            ("language", string s) => s == LibraryViewModel.AnyLanguage ? f.LanguageTotal : f.Languages.GetValueOrDefault(s),
            ("genre", string s) => s == LibraryViewModel.AnyGenre ? f.GenreTotal : f.Genres.GetValueOrDefault(s),
            ("rating", RatingOption r) => r.Stars is { } n ? f.Stars.GetValueOrDefault(n) : f.StarsTotal,
            ("source", SourceOption o) => o.Key switch
            {
                SourceOption.AllKey => f.SourcesTotal,
                SourceOption.LocalKey => f.Sources.Where(kv => kv.Key != UsdbCatalogEntry.SourceId).Sum(kv => kv.Value),
                string id => f.Sources.GetValueOrDefault(id),
            },
            _ => 0,
        };
    }
}
