using Dapper;
using Microsoft.Data.Sqlite;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Usdb;

/// <summary>USDB song list in its own table of the library database. ~27k rows, replaced/merged by sync.</summary>
public sealed class SqliteUsdbCatalog : IUsdbCatalog, IDisposable
{
    private readonly string _connectionString;
    private readonly Lock _writeLock = new();

    static SqliteUsdbCatalog() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public SqliteUsdbCatalog(AppPaths paths)
        : this(Path.Combine(paths.Data, "library.db"))
    {
    }

    public SqliteUsdbCatalog(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Cache = SqliteCacheMode.Shared }.ToString();
        using SqliteConnection c = Open();
        c.Execute("PRAGMA journal_mode=WAL;");
        c.Execute(Schema);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS usdb_catalog (
            song_id INTEGER PRIMARY KEY,
            artist TEXT NOT NULL,
            title TEXT NOT NULL,
            genre TEXT,
            year INTEGER,
            language TEXT,
            creator TEXT,
            edition TEXT,
            golden_notes INTEGER NOT NULL,
            rating REAL NOT NULL,
            views INTEGER NOT NULL,
            cover_url TEXT,
            usdb_mtime INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_usdb_mtime ON usdb_catalog(usdb_mtime);
        """;

    public int Count
    {
        get
        {
            using SqliteConnection c = Open();
            return c.ExecuteScalar<int>("SELECT COUNT(*) FROM usdb_catalog");
        }
    }

    public IReadOnlyList<UsdbCatalogEntry> GetAll()
    {
        using SqliteConnection c = Open();
        return c.Query<Row>("SELECT * FROM usdb_catalog").Select(r => r.ToEntry()).ToList();
    }

    public void Upsert(IReadOnlyList<UsdbCatalogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        lock (_writeLock)
        {
            using SqliteConnection c = Open();
            using SqliteTransaction tx = c.BeginTransaction();
            c.Execute(
                """
                INSERT INTO usdb_catalog (song_id, artist, title, genre, year, language, creator, edition, golden_notes, rating, views, cover_url, usdb_mtime)
                VALUES (@SongId, @Artist, @Title, @Genre, @Year, @Language, @Creator, @Edition, @GoldenNotes, @Rating, @Views, @CoverUrl, @UsdbMtime)
                ON CONFLICT(song_id) DO UPDATE SET
                    artist = excluded.artist, title = excluded.title, genre = excluded.genre, year = excluded.year,
                    language = excluded.language, creator = excluded.creator, edition = excluded.edition,
                    golden_notes = excluded.golden_notes, rating = excluded.rating, views = excluded.views,
                    cover_url = excluded.cover_url, usdb_mtime = excluded.usdb_mtime
                """,
                entries.Select(Row.From), tx);
            tx.Commit();
        }
    }

    public void Clear()
    {
        lock (_writeLock)
        {
            using SqliteConnection c = Open();
            c.Execute("DELETE FROM usdb_catalog");
        }
    }

    public (long LastMtime, IReadOnlySet<int> Ids) Watermark()
    {
        using SqliteConnection c = Open();
        long? last = c.ExecuteScalar<long?>("SELECT MAX(usdb_mtime) FROM usdb_catalog");
        if (last is null)
        {
            return (0, new HashSet<int>());
        }

        HashSet<int> ids = c.Query<int>("SELECT song_id FROM usdb_catalog WHERE usdb_mtime = @last", new { last }).ToHashSet();
        return (last.Value, ids);
    }

    private SqliteConnection Open()
    {
        SqliteConnection c = new(_connectionString);
        c.Open();
        return c;
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    private sealed class Row
    {
        public int SongId { get; set; }
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public string? Language { get; set; }
        public string? Creator { get; set; }
        public string? Edition { get; set; }
        public bool GoldenNotes { get; set; }
        public double Rating { get; set; }
        public int Views { get; set; }
        public string? CoverUrl { get; set; }
        public long UsdbMtime { get; set; }

        public static Row From(UsdbCatalogEntry e) => new()
        {
            SongId = e.SongId, Artist = e.Artist, Title = e.Title, Genre = e.Genre, Year = e.Year, Language = e.Language,
            Creator = e.Creator, Edition = e.Edition, GoldenNotes = e.GoldenNotes, Rating = e.Rating, Views = e.Views,
            CoverUrl = e.CoverUrl, UsdbMtime = e.UsdbMtime,
        };

        public UsdbCatalogEntry ToEntry() => new()
        {
            SongId = SongId, Artist = Artist, Title = Title, Genre = Genre, Year = Year, Language = Language,
            Creator = Creator, Edition = Edition, GoldenNotes = GoldenNotes, Rating = Rating, Views = Views,
            CoverUrl = CoverUrl, UsdbMtime = UsdbMtime,
        };
    }
}
