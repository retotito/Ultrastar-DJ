using System.Text;
using System.Text.RegularExpressions;

namespace UltrastarDJ.Core.Localization;

/// <summary>
/// The app's texts in one language: the translation where there is one, else English (a new text shows in English
/// until it is translated), else the key in ⟦brackets⟧ so a forgotten one is noticed.
/// </summary>
public sealed class TextTable(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string>? translated)
{
    public string Get(string key)
        => translated is not null && translated.TryGetValue(key, out string? t) && !string.IsNullOrWhiteSpace(t) ? t
            : english.TryGetValue(key, out string? e) ? e
            : $"⟦{key}⟧";
}

/// <summary>
/// The test language for layout (developer mode): every text about 40 % longer, accented and in brackets, so a label
/// that is too narrow cuts off its "]" and a text that was never moved to the language files shows without brackets.
/// </summary>
public static class Pseudo
{
    private const string Plain = "abcdeghiklmnorstuyzABCDEGHIKLNORSTUYZ";
    private const string Accented = "àƀçđéĝĥîķĺɱñöŕšţûýžÀßÇĐÉĜĤÎĶĹÑÖŔŠŢÛÝŽ";

    public static string Of(string english)
    {
        StringBuilder sb = new("[");
        int letters = 0;
        // Placeholders like {0:N0} stay as they are — string.Format needs them.
        foreach (string part in Regex.Split(english, @"(\{[^}]*\})"))
        {
            if (part.StartsWith('{'))
            {
                sb.Append(part);
                continue;
            }

            foreach (char ch in part)
            {
                int i = Plain.IndexOf(ch);
                sb.Append(i >= 0 ? Accented[i] : ch);
                letters += char.IsLetter(ch) ? 1 : 0;
            }
        }

        // German runs about a third longer than English; a little more so nothing slips through.
        return sb.Append(' ').Append('~', Math.Max(2, (int)Math.Ceiling(letters * 0.4))).Append(']').ToString();
    }
}

/// <summary>A translation must keep the English text's {0} placeholders, or string.Format fails or drops a value.</summary>
public static partial class Placeholders
{
    public static bool Match(string english, string translated)
        => Of(english).SetEquals(Of(translated));

    private static HashSet<string> Of(string text) => [.. Index().Matches(text).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Index();
}
