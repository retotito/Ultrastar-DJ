using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Library;

/// <summary>
/// Songs that could not be loaded, in the library database. Its own table: library rescans replace the songs table,
/// the marks must stay (they disappear by fingerprint or a successful load, not by a rescan).
/// </summary>
public sealed class SqliteLoadFailureStore : ILoadFailureStore
{
    private readonly string _connectionString;

    public SqliteLoadFailureStore(AppPaths paths)
        : this(Path.Combine(paths.Data, "library.db"))
    {
    }

    public SqliteLoadFailureStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Cache = SqliteCacheMode.Shared }.ToString();
        using SqliteConnection c = Open();
        c.Execute("""
            CREATE TABLE IF NOT EXISTS load_failures (
                song_id TEXT PRIMARY KEY,
                reason TEXT NOT NULL,
                at_utc TEXT NOT NULL,
                fingerprint TEXT NOT NULL
            );
            """);
    }

    public IReadOnlyList<LoadFailure> All()
    {
        using SqliteConnection c = Open();
        return [.. c.Query<(string SongId, string Reason, string AtUtc, string Fingerprint)>(
                "SELECT song_id, reason, at_utc, fingerprint FROM load_failures")
            .Select(r => new LoadFailure(r.SongId, r.Reason,
                DateTime.Parse(r.AtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), r.Fingerprint))];
    }

    public void Save(LoadFailure f)
    {
        using SqliteConnection c = Open();
        c.Execute("""
            INSERT INTO load_failures (song_id, reason, at_utc, fingerprint) VALUES (@SongId, @Reason, @AtUtc, @Fingerprint)
            ON CONFLICT(song_id) DO UPDATE SET reason = excluded.reason, at_utc = excluded.at_utc, fingerprint = excluded.fingerprint
            """,
            new { f.SongId, f.Reason, AtUtc = f.AtUtc.ToString("O", CultureInfo.InvariantCulture), f.Fingerprint });
    }

    public void Remove(string songId)
    {
        using SqliteConnection c = Open();
        c.Execute("DELETE FROM load_failures WHERE song_id = @songId", new { songId });
    }

    private SqliteConnection Open()
    {
        SqliteConnection c = new(_connectionString);
        c.Open();
        return c;
    }
}
