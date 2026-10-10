using System.Globalization;
using System.Text;

namespace UltrastarDJ.Core.Songs;

/// <summary>Which USDB song texts are kept on this computer beyond the ones loaded.</summary>
public enum UsdbTextsMode
{
    /// <summary>Only songs that were loaded (each is kept once fetched).</summary>
    Loaded,
    /// <summary>Also every favourite.</summary>
    Favourites,
    /// <summary>The whole catalog.</summary>
    All,
}

/// <summary>
/// The USDB song texts kept on disk: one readable file per song, "Artist - Title [id].txt", so the folder makes sense
/// in Finder and to other karaoke programs if USDB ever goes away. The id keeps names unique (USDB has many versions
/// of one song) and finds the file again. Older files are named "id.txt".
/// </summary>
public static class UsdbTextFiles
{
    /// <summary>Well under every file system's limit (255) — room for the folder path on Windows too.</summary>
    public const int MaxNameLength = 150;

    public static string FileName(int id, string artist, string title)
    {
        string suffix = string.Create(CultureInfo.InvariantCulture, $" [{id}].txt");
        string name = Clean($"{artist.Trim()} - {title.Trim()}");
        if (name.Length + suffix.Length > MaxNameLength)
        {
            name = name[..(MaxNameLength - suffix.Length)].TrimEnd();
        }

        return name + suffix;
    }

    /// <summary>The song id in a file name of either form; null for other files.</summary>
    public static int? IdOf(string fileName)
    {
        if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string stem = fileName[..^4];
        if (stem.EndsWith(']') && stem.LastIndexOf('[') is int open and >= 0)
        {
            stem = stem[(open + 1)..^1];
        }

        return int.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;
    }

    /// <summary>A catalog song: its id and when it last changed on USDB (unix seconds).</summary>
    public readonly record struct Wanted(int Id, long UsdbMtime);

    /// <param name="Ids">To download, in order: favourites first.</param>
    /// <param name="Target">How many texts the mode wants on disk — the 100 % of the progress.</param>
    public sealed record Plan(IReadOnlyList<int> Ids, int Target);

    /// <summary>
    /// What to download for the mode: the wanted songs that are missing on disk or older than their last change on
    /// USDB. <paramref name="onDisk"/>: id → the file's last write (unix seconds).
    /// </summary>
    public static Plan ToFetch(IEnumerable<Wanted> catalog, IReadOnlyDictionary<int, long> onDisk, UsdbTextsMode mode, IReadOnlySet<int> favourites)
    {
        if (mode == UsdbTextsMode.Loaded)
        {
            return new Plan([], 0);
        }

        List<Wanted> wanted = [.. catalog.Where(w => mode == UsdbTextsMode.All || favourites.Contains(w.Id))];
        List<int> ids = [.. wanted
            .Where(w => !onDisk.TryGetValue(w.Id, out long written) || written < w.UsdbMtime)
            .OrderBy(w => favourites.Contains(w.Id) ? 0 : 1)
            .Select(w => w.Id)];
        return new Plan(ids, wanted.Count);
    }

    // Characters Windows, macOS or Linux refuse in a file name (and control characters).
    private static string Clean(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char ch in name)
        {
            sb.Append(ch < 32 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        }

        // Windows refuses a trailing dot or space.
        return sb.ToString().TrimEnd('.', ' ');
    }
}
