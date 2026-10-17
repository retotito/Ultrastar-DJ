using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace UltrastarDJ.Core.Songs;

/// <summary>
/// The library search (docs/04-ui.md "Search"), forgiving the way people type:
/// <list type="number">
/// <item>accents and special letters do not matter — "beyonce" finds "Beyoncé", "motorhead" "Motörhead", "strasse" "Straße";</item>
/// <item>punctuation does not matter — "dont" finds "Don't", "acdc" "AC/DC"; "&amp;" and "and" are the same;</item>
/// <item>every typed word only has to appear somewhere in title or artist, in any order — "queen bohemian";</item>
/// <item><see cref="MatchesSimilar"/> also forgives typos: one per word from 4 letters, two from 8 ("bohemain").
/// The library adds those matches only when the plain search finds few songs.</item>
/// </list>
/// The songbook page (songbook.html) runs the same rules on the guests' phones.
/// </summary>
public sealed class SongSearch
{
    private static readonly ConditionalWeakTable<Song, Key> Keys = [];
    private static readonly Lock CacheLock = new();
    private static SongSearch _last = new("", []);

    private readonly string[] _tokens;

    private SongSearch(string query, string[] tokens)
    {
        Query = query;
        _tokens = tokens;
    }

    public string Query { get; }
    public bool IsEmpty => _tokens.Length == 0;

    /// <summary>The parsed search; the last one is kept (the library asks for it once per song and keystroke).</summary>
    public static SongSearch For(string query)
    {
        lock (CacheLock)
        {
            if (_last.Query != query)
            {
                _last = new SongSearch(query, [.. Words(Fold(query)).Select(Compact).Where(t => t.Length > 0)]);
            }

            return _last;
        }
    }

    /// <summary>Every typed word appears in title or artist (accents, punctuation and order ignored).</summary>
    public bool Matches(Song song)
    {
        if (IsEmpty)
        {
            return true;
        }

        Key key = KeyOf(song);
        return _tokens.All(t => key.Compact.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>Like <see cref="Matches"/>, but each word may also be a word of the song with a typo or two.</summary>
    public bool MatchesSimilar(Song song)
    {
        if (IsEmpty)
        {
            return true;
        }

        Key key = KeyOf(song);
        return _tokens.All(t => key.Compact.Contains(t, StringComparison.Ordinal) || key.Words.Any(w => Close(t, w)));
    }

    /// <summary>Typos a word may have: none up to 3 letters (else "love" finds "live"), one from 4, two from 8.</summary>
    public static int AllowedTypos(int length) => length >= 8 ? 2 : length >= 4 ? 1 : 0;

    // The typed word against a word of the song — or its beginning, while the DJ is still typing it.
    private static bool Close(string typed, string word)
    {
        int allowed = AllowedTypos(typed.Length);
        if (allowed == 0)
        {
            return false;
        }

        return Distance(typed, word, allowed) <= allowed
            || (word.Length > typed.Length && Distance(typed, word[..typed.Length], allowed) <= allowed);
    }

    /// <summary>
    /// Letters to their plain form, in lower case: accents dropped (é → e, ö → o, ñ → n), letters without a plain
    /// form spelled out (ß → ss, æ → ae, ø → o), "&amp;" → " and "; anything else that is not a letter or digit → a space.
    /// Other scripts (Cyrillic, Japanese …) stay as they are.
    /// </summary>
    public static string Fold(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            // Accents that come as a separate mark (decomposed text) are dropped.
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(c);
            int accented = AccentedLetters.IndexOf(lower, StringComparison.Ordinal);
            if (accented >= 0)
            {
                sb.Append(PlainLetters[accented]);
                continue;
            }

            string? spelled = lower switch
            {
                'ß' => "ss",
                'æ' => "ae",
                'œ' => "oe",
                'ø' => "o",
                'ł' => "l",
                'đ' or 'ð' => "d",
                'þ' => "th",
                'ı' => "i",
                '&' => " and ",
                // Apostrophes join: "don't" is one word, "dont".
                '\'' or '’' or '`' or '´' => "",
                _ => null,
            };
            if (spelled is not null)
            {
                sb.Append(spelled);
            }
            else
            {
                sb.Append(char.IsLetterOrDigit(lower) ? lower : ' ');
            }
        }

        return sb.ToString();
    }

    // Accented Latin letters (Latin-1, Latin Extended-A/B, Vietnamese) and their plain letter — a table, because the app
    // runs with InvariantGlobalization, where string.Normalize does not take "é" apart.
    private const string AccentedLetters = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿāăąćĉċčďēĕėęěĝğġģĥĩīĭįi̇ĵķĺļľńņňōŏőŕŗřśŝşšţťũūŭůűųŵŷźżžơưǎǐǒǔǖǘǚǜǟǡǧǩǫǭǰǵǹǻȁȃȅȇȉȋȍȏȑȓȕȗșțȟȧȩȫȭȯȱȳḁḃḅḇḉḋḍḏḑḓḕḗḙḛḝḟḡḣḥḧḩḫḭḯḱḳḵḷḹḻḽḿṁṃṅṇṉṋṍṏṑṓṕṗṙṛṝṟṡṣṥṧṩṫṭṯṱṳṵṷṹṻṽṿẁẃẅẇẉẋẍẏẑẓẕẖẗẘẙạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ";
    private const string PlainLetters = "aaaaaaceeeeiiiinooooouuuuyyaaaccccdeeeeegggghiiiiijklllnnnooorrrssssttuuuuuuwyzzzouaiouuuuuaagkoojgnaaaeeiioorruusthaeooooyabbbcdddddeeeeefghhhhhiikkkllllmmmnnnnoooopprrrrsssssttttuuuuuvvwwwwwxxyzzzhtwyaaaaaaaaaaaaeeeeeeeeiioooooooooooouuuuuuuyyyy";

    private static string[] Words(string folded) => folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Letters and digits only: "AC/DC" → "acdc", so "acdc" and "ac dc" both find it.
    private static string Compact(string folded) => string.Concat(folded.Where(char.IsLetterOrDigit));

    private static Key KeyOf(Song song) => Keys.GetValue(song, s =>
    {
        string folded = Fold($"{s.Title} {s.Artist}");
        return new Key(Compact(folded), Words(folded));
    });

    /// <summary>
    /// Typing distance (optimal string alignment: a wrong, missing or extra letter, or two swapped ones, is one step),
    /// giving up beyond <paramref name="max"/>.
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return max + 1;
        }

        int[] before = new int[b.Length + 1];
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int d = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d = Math.Min(d, before[j - 2] + 1);
                }

                current[j] = d;
                rowMin = Math.Min(rowMin, d);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            (before, previous, current) = (previous, current, before);
        }

        return previous[b.Length];
    }

    private sealed record Key(string Compact, string[] Words);
}
