using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.Infrastructure.Tests;

public sealed class SqliteLoadFailureStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usdj-tests", Guid.NewGuid().ToString("N"));

    public SqliteLoadFailureStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Save_RoundTrips_UpdatesAndRemoves()
    {
        string db = Path.Combine(_dir, "lib.db");
        DateTime at = new(2026, 10, 6, 18, 3, 5, DateTimeKind.Utc);
        SqliteLoadFailureStore store = new(db);

        store.Save(new LoadFailure("usdb::42", "no YouTube link", at, "usdb:100"));
        store.Save(new LoadFailure("usdb::42", "video removed", at.AddDays(1), "usdb:200"));
        store.Save(new LoadFailure("a::x", "no notes", at, "txt:1"));

        LoadFailure f = Assert.Single(new SqliteLoadFailureStore(db).All(), x => x.SongId == "usdb::42");
        Assert.Equal(("video removed", at.AddDays(1), "usdb:200"), (f.Reason, f.AtUtc, f.Fingerprint));
        Assert.Equal(DateTimeKind.Utc, f.AtUtc.Kind);

        store.Remove("usdb::42");
        Assert.Equal(["a::x"], store.All().Select(x => x.SongId));
    }

    [Fact]
    public void SharesTheLibraryDatabase()
    {
        string db = Path.Combine(_dir, "lib.db");
        using SqliteSongRepository repo = new(db);
        SqliteLoadFailureStore store = new(db);

        store.Save(new LoadFailure("s1::a", "broken", DateTime.UtcNow, "txt:1"));

        Assert.Single(store.All());
    }
}
