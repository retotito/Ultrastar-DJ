using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.Infrastructure.Tests;

public sealed class SqliteSongRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usdj-tests", Guid.NewGuid().ToString("N"));

    public SqliteSongRepositoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Song Song(string id, string source = "s1") => new()
    {
        Id = id, SourceId = source, Title = "Title " + id, Artist = "Artist", Bpm = 300.5, GapMs = 16528,
        Year = 1999, Language = "English", TxtPath = "/x/" + id + ".txt", AudioPath = "/x/a.mp3", YouTubeId = "dQw4w9WgXcQ", VideoGapSec = 19.5, EndMs = 120000,
    };

    [Fact]
    public void ReplaceSource_RoundTripsAllColumns()
    {
        using SqliteSongRepository repo = new(Path.Combine(_dir, "lib.db"));
        Song original = Song("s1::a");

        repo.ReplaceSource("s1", [original]);
        Song loaded = Assert.Single(repo.GetAll());

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void ReplaceSource_OnlyTouchesThatSource()
    {
        using SqliteSongRepository repo = new(Path.Combine(_dir, "lib.db"));
        repo.ReplaceSource("s1", [Song("s1::a"), Song("s1::b")]);
        repo.ReplaceSource("s2", [Song("s2::c", "s2")]);

        repo.ReplaceSource("s1", [Song("s1::z")]);

        Assert.Equal(["s1::z", "s2::c"], repo.GetAll().Select(s => s.Id).Order());
        Assert.Equal(1, repo.CountBySource("s1"));
        Assert.Equal(1, repo.CountBySource("s2"));
    }

    [Fact]
    public void RemoveSource_DeletesItsSongs()
    {
        using SqliteSongRepository repo = new(Path.Combine(_dir, "lib.db"));
        repo.ReplaceSource("s1", [Song("s1::a")]);

        repo.RemoveSource("s1");

        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void Reopen_KeepsData()
    {
        string db = Path.Combine(_dir, "lib.db");
        using (SqliteSongRepository repo = new(db))
        {
            repo.ReplaceSource("s1", [Song("s1::a")]);
        }

        using SqliteSongRepository again = new(db);
        Assert.Single(again.GetAll());
    }

    [Fact]
    public async Task Scanner_FindsTxtFilesRecursively()
    {
        string songs = Path.Combine(_dir, "songs", "Artist - Title");
        Directory.CreateDirectory(songs);
        await File.WriteAllTextAsync(Path.Combine(songs, "Artist - Title.txt"), "#TITLE:Title\n#ARTIST:Artist\n#MP3:a.mp3\n: 0 1 0 la\nE\n");
        await File.WriteAllTextAsync(Path.Combine(songs, "notes.txt"), "just a note, not a song\n");

        LocalFolderScanner scanner = new(NullLogger<LocalFolderScanner>.Instance);
        IReadOnlyList<Song> result = await scanner.ScanAsync("src", Path.Combine(_dir, "songs"));

        Song song = Assert.Single(result);
        Assert.Equal("Title", song.Title);
        Assert.Equal(Path.Combine(songs, "a.mp3"), song.AudioPath);
    }
}
