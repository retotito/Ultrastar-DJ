using System.Text.RegularExpressions;

namespace UltrastarDJ.Core.Songs;

/// <summary>Extracts an 11-character YouTube video id from URLs (<c>watch?v=</c>, <c>youtu.be/</c>, <c>embed/</c>) or a bare id.</summary>
public static partial class YouTubeId
{
    [GeneratedRegex(@"[?&]v=([A-Za-z0-9_-]{11})")]
    private static partial Regex Watch();

    [GeneratedRegex(@"youtu\.be/([A-Za-z0-9_-]{11})")]
    private static partial Regex Short();

    [GeneratedRegex(@"youtube\.com/(?:embed|shorts)/([A-Za-z0-9_-]{11})")]
    private static partial Regex Embed();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex Bare();

    public static string? TryExtract(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        foreach (Regex rx in (ReadOnlySpan<Regex>)[Watch(), Short(), Embed()])
        {
            Match m = rx.Match(value);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return Bare().IsMatch(value) ? value : null;
    }
}
