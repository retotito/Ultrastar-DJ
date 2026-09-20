using Dapper;
using Microsoft.Data.Sqlite;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Library;

/// <summary>
/// SQLite-backed library. One row per song header; notes are never stored (parsed on load).
/// Schema is created on first use; bump <see cref="SchemaVersion"/> and add a migration when columns change.
/// </summary>
public sealed class SqliteSongRepository : ISongRepository, IDisposable
{
    private const int SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly Lock _writeLock = new();

    public SqliteSongRepository(AppPaths paths)
        : this(Path.Combine(paths.Data, "library.db"))
    {
    }

    public SqliteSongRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Cache = SqliteCacheMode.Shared }.ToString();
        using SqliteConnection c = Open();
        c.Execute("PRAGMA journal_mode=WAL;");
        long version = c.ExecuteScalar<long>("PRAGMA user_version;");
        if (version < SchemaVersion)
        {
            c.Execute(Schema);
            c.Execute($"PRAGMA user_version={SchemaVersion};");
        }
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS songs (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL,
            title TEXT NOT NULL,
            artist TEXT NOT NULL,
            bpm REAL NOT NULL,
            gap_ms REAL NOT NULL,
            year INTEGER,
            language TEXT,
            genre TEXT,
            edition TEXT,
            creator TEXT,
            txt_path TEXT,
            audio_path TEXT,
            video_path TEXT,
            cover_path TEXT,
            background_path TEXT,
            youtube_id TEXT,
            video_gap_sec REAL,
            start_sec REAL,
            end_ms REAL,
            usdb_id INTEGER,
            usdb_views INTEGER
        );
        CREATE INDEX IF NOT EXISTS ix_songs_source ON songs(source_id);
        CREATE INDEX IF NOT EXISTS ix_songs_artist_title ON songs(artist COLLATE NOCASE, title COLLATE NOCASE);
        """;

    public void ReplaceSource(string sourceId, IReadOnlyList<Song> songs)
    {
        lock (_writeLock)
        {
            using SqliteConnection c = Open();
            using SqliteTransaction tx = c.BeginTransaction();
            c.Execute("DELETE FROM songs WHERE source_id = @sourceId", new { sourceId }, tx);
            c.Execute(
                """
                INSERT INTO songs (id, source_id, title, artist, bpm, gap_ms, year, language, genre, edition, creator,
                    txt_path, audio_path, video_path, cover_path, background_path, youtube_id, video_gap_sec, start_sec, end_ms, usdb_id, usdb_views)
                VALUES (@Id, @SourceId, @Title, @Artist, @Bpm, @GapMs, @Year, @Language, @Genre, @Edition, @Creator,
                    @TxtPath, @AudioPath, @VideoPath, @CoverPath, @BackgroundPath, @YouTubeId, @VideoGapSec, @StartSec, @EndMs, @UsdbId, @UsdbViews)
                """,
                songs.Select(Row.From), tx);
            tx.Commit();
        }
    }

    public void RemoveSource(string sourceId)
    {
        lock (_writeLock)
        {
            using SqliteConnection c = Open();
            c.Execute("DELETE FROM songs WHERE source_id = @sourceId", new { sourceId });
        }
    }

    public IReadOnlyList<Song> GetAll()
    {
        using SqliteConnection c = Open();
        return c.Query<Row>("SELECT * FROM songs ORDER BY artist COLLATE NOCASE, title COLLATE NOCASE").Select(r => r.ToSong()).ToList();
    }

    public int CountBySource(string sourceId)
    {
        using SqliteConnection c = Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM songs WHERE source_id = @sourceId", new { sourceId });
    }

    private SqliteConnection Open()
    {
        SqliteConnection c = new(_connectionString);
        c.Open();
        return c;
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    // Dapper maps snake_case columns onto these PascalCase members via DefaultTypeMap.MatchNamesWithUnderscores.
    private sealed class Row
    {
        static Row() => DefaultTypeMap.MatchNamesWithUnderscores = true;

        public string Id { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public double Bpm { get; set; }
        public double GapMs { get; set; }
        public int? Year { get; set; }
        public string? Language { get; set; }
        public string? Genre { get; set; }
        public string? Edition { get; set; }
        public string? Creator { get; set; }
        public string? TxtPath { get; set; }
        public string? AudioPath { get; set; }
        public string? VideoPath { get; set; }
        public string? CoverPath { get; set; }
        public string? BackgroundPath { get; set; }
        public string? YouTubeId { get; set; }
        public double? VideoGapSec { get; set; }
        public double? StartSec { get; set; }
        public double? EndMs { get; set; }
        public int? UsdbId { get; set; }
        public int? UsdbViews { get; set; }

        public static Row From(Song s) => new()
        {
            Id = s.Id, SourceId = s.SourceId, Title = s.Title, Artist = s.Artist, Bpm = s.Bpm, GapMs = s.GapMs, Year = s.Year,
            Language = s.Language, Genre = s.Genre, Edition = s.Edition, Creator = s.Creator, TxtPath = s.TxtPath,
            AudioPath = s.AudioPath, VideoPath = s.VideoPath, CoverPath = s.CoverPath, BackgroundPath = s.BackgroundPath,
            YouTubeId = s.YouTubeId, VideoGapSec = s.VideoGapSec, StartSec = s.StartSec, EndMs = s.EndMs, UsdbId = s.UsdbId, UsdbViews = s.UsdbViews,
        };

        public Song ToSong() => new()
        {
            Id = Id, SourceId = SourceId, Title = Title, Artist = Artist, Bpm = Bpm, GapMs = GapMs, Year = Year,
            Language = Language, Genre = Genre, Edition = Edition, Creator = Creator, TxtPath = TxtPath,
            AudioPath = AudioPath, VideoPath = VideoPath, CoverPath = CoverPath, BackgroundPath = BackgroundPath,
            YouTubeId = YouTubeId, VideoGapSec = VideoGapSec, StartSec = StartSec, EndMs = EndMs, UsdbId = UsdbId, UsdbViews = UsdbViews,
        };
    }
}
