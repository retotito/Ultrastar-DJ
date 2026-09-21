using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Usdb;

namespace UltrastarDJ.Infrastructure.Tests;

public sealed class UsdbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usdj-tests", Guid.NewGuid().ToString("N"));

    public UsdbTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Row(int id, long mtime, string artist = "Artist", string title = "Title", string year = "1999", string golden = "yes", string views = "1,234")
        => $"""
            <tr data-songid="{id}" data-lastchange="{mtime}">
              <td>{id}</td><td><img src="/data/cover/{id}.jpg"></td><td>{artist}</td><td>{title}</td><td>Pop</td><td>{year}</td>
              <td>SingStar</td><td>{golden}</td><td>English</td><td>creator</td><td>4.5</td><td>{views}</td>
            </tr>
            """;

    private static string Page(params string[] rows) => $"<html><body><table>{string.Concat(rows)}</table></body></html>";

    [Fact]
    public void ParseSongList_ReadsEveryColumn()
    {
        IReadOnlyList<UsdbCatalogEntry> list = UsdbHtml.ParseSongList(Page(Row(42, 1700000000, "Queen", "Bohemian Rhapsody", "1975")));

        UsdbCatalogEntry e = Assert.Single(list);
        Assert.Equal(42, e.SongId);
        Assert.Equal("Queen", e.Artist);
        Assert.Equal("Bohemian Rhapsody", e.Title);
        Assert.Equal("Pop", e.Genre);
        Assert.Equal(1975, e.Year);
        Assert.Equal("SingStar", e.Edition);
        Assert.True(e.GoldenNotes);
        Assert.Equal("English", e.Language);
        Assert.Equal("creator", e.Creator);
        Assert.Equal(4.5, e.Rating);
        Assert.Equal(1234, e.Views);
        Assert.Equal("https://usdb.animux.de/data/cover/42.jpg", e.CoverUrl);
        Assert.Equal(1700000000, e.UsdbMtime);
    }

    [Fact]
    public void ParseSongList_ToleratesMissingYearAndShortRows()
    {
        string html = Page(Row(1, 5, year: "", golden: "no"), "<tr data-songid=\"2\"><td>2</td></tr>", "<tr><td>no id</td></tr>");
        IReadOnlyList<UsdbCatalogEntry> list = UsdbHtml.ParseSongList(html);

        UsdbCatalogEntry e = Assert.Single(list);
        Assert.Null(e.Year);
        Assert.False(e.GoldenNotes);
        Assert.Equal(1, e.SongId);
    }

    [Fact]
    public void ExtractSongTxt_UnwrapsTextareaAndDecodesEntities()
    {
        string? txt = UsdbHtml.ExtractSongTxt("<html><body><textarea name=\"txt\">#TITLE:Rock &amp; Roll\r\n#BPM:300\r\n: 0 4 60 Hey\r\nE\r\n</textarea></body></html>");
        Assert.Equal("#TITLE:Rock & Roll\n#BPM:300\n: 0 4 60 Hey\nE", txt);
        Assert.Null(UsdbHtml.ExtractSongTxt("<html><body>Please login</body></html>"));
    }

    [Fact]
    public async Task FetchAllPages_PagesUntilShortPage()
    {
        // 100 rows, then 100, then 5 → three requests.
        FakeHandler handler = new(req =>
        {
            string start = FormValue(req, "start");
            int s = int.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
            int count = s >= 200 ? 5 : 100;
            return Page(Enumerable.Range(s + 1, count).Select(i => Row(i, i)).ToArray());
        });
        using UsdbClient client = new(handler, NullLogger<UsdbClient>.Instance);
        List<int> pageSizes = [];

        await foreach (IReadOnlyList<UsdbCatalogEntry> page in client.FetchAllPagesAsync())
        {
            pageSizes.Add(page.Count);
        }

        Assert.Equal([100, 100, 5], pageSizes);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("order=id", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchUpdated_StopsAtWatermark()
    {
        // Newest first: mtimes 110..11; watermark 100 with id 100 known → 10 new rows (110..101).
        FakeHandler handler = new(_ => Page(Enumerable.Range(0, 100).Select(i => Row(110 - i, 110 - i)).ToArray()));
        using UsdbClient client = new(handler, NullLogger<UsdbClient>.Instance);

        IReadOnlyList<UsdbCatalogEntry> updated = await client.FetchUpdatedAsync(100, new HashSet<int> { 100 });

        Assert.Equal(10, updated.Count);
        Assert.Equal(110, updated[0].SongId);
        Assert.Single(handler.Requests);
        Assert.Contains("order=lastchange", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_RejectedTextMeansFalse()
    {
        FakeHandler handler = new(_ => "<html>Login or Password invalid</html>");
        using UsdbClient client = new(handler, NullLogger<UsdbClient>.Instance);

        Assert.False(await client.LoginAsync("u", "p"));
        Assert.False(client.IsLoggedIn);
        Assert.Contains("user=u", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSongTxt_ThrowsWhenNoTextarea()
    {
        FakeHandler handler = new(_ => "<html>nope</html>");
        using UsdbClient client = new(handler, NullLogger<UsdbClient>.Instance);

        await Assert.ThrowsAsync<UsdbException>(() => client.GetSongTxtAsync(7));
    }

    [Fact]
    public void Catalog_UpsertMergesAndWatermarkReflectsNewest()
    {
        using SqliteUsdbCatalog catalog = new(Path.Combine(_dir, "lib.db"));
        catalog.Upsert([Entry(1, 10), Entry(2, 20), Entry(3, 20)]);
        catalog.Upsert([Entry(2, 25) with { Title = "Renamed" }]);

        Assert.Equal(3, catalog.Count);
        Assert.Equal("Renamed", catalog.GetAll().Single(e => e.SongId == 2).Title);
        (long last, IReadOnlySet<int> ids) = catalog.Watermark();
        Assert.Equal(25, last);
        Assert.Equal([2], ids.ToArray());

        catalog.Clear();
        Assert.Equal(0, catalog.Count);
        (long emptyLast, IReadOnlySet<int> emptyIds) = catalog.Watermark();
        Assert.Equal(0, emptyLast);
        Assert.Empty(emptyIds);
    }

    [Fact]
    public void Entry_ToSong_UsesUsdbIdScheme()
    {
        Song s = Entry(77, 1).ToSong();
        Assert.Equal("usdb::77", s.Id);
        Assert.Equal(UsdbCatalogEntry.SourceId, s.SourceId);
        Assert.Equal(77, s.UsdbId);
        Assert.Equal(0, s.Bpm);
    }

    private static UsdbCatalogEntry Entry(int id, long mtime) => new() { SongId = id, Artist = "A", Title = "T" + id, UsdbMtime = mtime, Views = 3, Rating = 4 };

    private static string FormValue(HttpRequestMessage req, string key)
    {
        string body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return body.Split('&').Select(kv => kv.Split('=')).First(kv => kv[0] == key)[1];
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) };
        }
    }
}
