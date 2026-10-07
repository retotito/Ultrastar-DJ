namespace UltrastarDJ.Core.Sidecars;

/// <summary>
/// yt-dlp versions are dates — "2026.08.19", hotfixes "2026.08.19.1". YouTube changes often and yt-dlp follows within
/// days, so an old version is the usual reason YouTube songs stop playing.
/// </summary>
public static class YtDlpVersion
{
    /// <summary>Whether <paramref name="latest"/> is newer than <paramref name="current"/>; false when either is unreadable.</summary>
    public static bool IsNewer(string? latest, string? current)
    {
        int[]? l = Parse(latest), c = Parse(current);
        if (l is null || c is null)
        {
            return false;
        }

        for (int i = 0; i < Math.Max(l.Length, c.Length); i++)
        {
            int a = i < l.Length ? l[i] : 0, b = i < c.Length ? c[i] : 0;
            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }

    /// <summary>Days since the version was released ("2026.08.19" → its date), or null when unreadable.</summary>
    public static int? AgeInDays(string? version, DateOnly today)
        => Parse(version) is { Length: >= 3 } p && p[1] is >= 1 and <= 12 && p[2] is >= 1 and <= 31
            ? today.DayNumber - new DateOnly(p[0], p[1], Math.Min(p[2], DateTime.DaysInMonth(p[0], p[1]))).DayNumber
            : null;

    private static int[]? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string[] parts = version.Trim().TrimStart('v').Split('.');
        int[] numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return numbers;
    }
}
