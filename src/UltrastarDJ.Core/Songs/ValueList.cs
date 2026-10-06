using System.Buffers;

namespace UltrastarDJ.Core.Songs;

/// <summary>
/// <c>#LANGUAGE</c> and <c>#GENRE</c> hold lists ("English, French", "Pop, Rock"). The library filters offer each
/// value once and match a song when the chosen value is one of its entries — as usdb_syncer does
/// (<c>usdb_song.py</c> languages/genres). Qualifiers stay part of the value: "Chinese (romanized)" is its own entry.
/// </summary>
public static class ValueList
{
    /// <summary>usdb_syncer's language fix treats these as separators too.</summary>
    public static readonly SearchValues<char> LanguageSeparators = SearchValues.Create(",;/|");

    /// <summary>Genres split on commas only: "R&amp;B/Soul" is one genre.</summary>
    public static readonly SearchValues<char> GenreSeparators = SearchValues.Create(",");

    public static IReadOnlyList<string> Split(string? text, SearchValues<char> separators)
    {
        List<string> values = [];
        foreach (Range r in Entries(text, separators))
        {
            values.Add(text![r].Trim());
        }

        return values;
    }

    /// <summary>Whether <paramref name="value"/> is one of the entries, ignoring case. No allocation (filters 28k songs per keystroke).</summary>
    public static bool Contains(string? text, string value, SearchValues<char> separators)
    {
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> rest = text;
        ReadOnlySpan<char> wanted = value.AsSpan().Trim();
        while (true)
        {
            int cut = rest.IndexOfAny(separators);
            ReadOnlySpan<char> entry = (cut < 0 ? rest : rest[..cut]).Trim();
            if (entry.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (cut < 0)
            {
                return false;
            }

            rest = rest[(cut + 1)..];
        }
    }

    /// <summary>Every value once (first spelling wins), sorted ignoring case — the filter drop-down.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<string?> texts, SearchValues<char> separators)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> values = [];
        foreach (string? text in texts)
        {
            foreach (string v in Split(text, separators))
            {
                if (seen.Add(v))
                {
                    values.Add(v);
                }
            }
        }

        values.Sort(StringComparer.OrdinalIgnoreCase);
        return values;
    }

    private static IEnumerable<Range> Entries(string? text, SearchValues<char> separators)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        int start = 0;
        while (start <= text.Length)
        {
            int cut = text.AsSpan(start).IndexOfAny(separators);
            int end = cut < 0 ? text.Length : start + cut;
            if (!text.AsSpan(start, end - start).IsWhiteSpace())
            {
                yield return new Range(start, end);
            }

            start = end + 1;
        }
    }
}
