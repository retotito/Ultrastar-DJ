using System.Globalization;

namespace UltrastarDJ.Core.Localization;

/// <summary>
/// User-facing texts made below the app (song checks, error explanations, ratings, player names). Each call carries
/// its English text; the app sets <see cref="Lookup"/> to its language files (key → translation, null when none), so
/// Core, Media and Infrastructure stay free of UI concerns and their tests see English.
/// </summary>
public static class Text
{
    public static Func<string, string?> Lookup { get; set; } = _ => null;

    public static Func<IFormatProvider> Culture { get; set; } = () => CultureInfo.CurrentCulture;

    public static string T(string key, string english) => Lookup(key) ?? english;

    public static string F(string key, string english, params object?[] args) => string.Format(Culture(), Lookup(key) ?? english, args);
}
