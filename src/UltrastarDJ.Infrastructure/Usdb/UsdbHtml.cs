using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Usdb;

/// <summary>
/// Scrapes the two USDB pages we depend on. Column order of the song list (details=1):
/// [0] id, [1] cover, [2] artist, [3] title, [4] genre, [5] year, [6] edition, [7] golden, [8] language,
/// [9] creator, [10] rating, [11] views. Rows carry <c>data-songid</c> and <c>data-lastchange</c>.
/// </summary>
public static class UsdbHtml
{
    public const string BaseUrl = "https://usdb.animux.de";
    private const int MinColumns = 12;
    private static readonly HtmlParser Parser = new();

    public static bool IsLoginRejected(string html) => html.Contains("Login or Password invalid", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<UsdbCatalogEntry> ParseSongList(string html)
    {
        IHtmlDocument doc = Parser.ParseDocument(html);
        List<UsdbCatalogEntry> songs = [];
        foreach (IElement row in doc.QuerySelectorAll("tr[data-songid]"))
        {
            if (!int.TryParse(row.GetAttribute("data-songid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                continue;
            }

            IHtmlCollection<IElement> tds = row.QuerySelectorAll("td");
            if (tds.Length < MinColumns)
            {
                continue;
            }

            long.TryParse(row.GetAttribute("data-lastchange"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long mtime);
            string? coverSrc = tds[1].QuerySelector("img")?.GetAttribute("src");

            songs.Add(new UsdbCatalogEntry
            {
                SongId = id,
                Artist = Text(tds[2]),
                Title = Text(tds[3]),
                Genre = NullIfEmpty(Text(tds[4])),
                Year = int.TryParse(Text(tds[5]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int year) ? year : null,
                Edition = NullIfEmpty(Text(tds[6])),
                GoldenNotes = Text(tds[7]).Equals("yes", StringComparison.OrdinalIgnoreCase),
                Language = NullIfEmpty(Text(tds[8])),
                Creator = NullIfEmpty(Text(tds[9])),
                Rating = double.TryParse(Text(tds[10]), NumberStyles.Float, CultureInfo.InvariantCulture, out double rating) ? rating : 0,
                Views = int.TryParse(Text(tds[11]).Replace(",", "", StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out int views) ? views : 0,
                CoverUrl = AbsoluteUrl(coverSrc),
                UsdbMtime = mtime,
            });
        }

        return songs;
    }

    /// <summary>The gettxt page wraps the file in a <c>&lt;textarea&gt;</c>.</summary>
    public static string? ExtractSongTxt(string html)
    {
        IHtmlDocument doc = Parser.ParseDocument(html);
        string? text = doc.QuerySelector("textarea")?.TextContent;
        return string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n', ' ', '\ufeff');
    }

    private static string Text(IElement el) => el.TextContent.Trim();
    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string? AbsoluteUrl(string? src) => src switch
    {
        null or "" => null,
        _ when src.StartsWith("http", StringComparison.OrdinalIgnoreCase) => src,
        _ => $"{BaseUrl}/{src.TrimStart('/')}",
    };
}
