using Microsoft.Data.Sqlite;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.Infrastructure.Tests;

public sealed class FirefoxYouTubeLoginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "usdj-firefox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // A profile folder with Firefox's cookie database (only the columns the check reads).
    private string Profile(string name, params (string Host, string Name, long ExpirySec)[] cookies)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "cookies.sqlite");
        using (SqliteConnection c = new($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using SqliteCommand create = c.CreateCommand();
            create.CommandText = "CREATE TABLE moz_cookies (host TEXT, name TEXT, value TEXT, expiry INTEGER)";
            create.ExecuteNonQuery();
            foreach ((string host, string cookie, long expiry) in cookies)
            {
                using SqliteCommand insert = c.CreateCommand();
                insert.CommandText = "INSERT INTO moz_cookies (host, name, value, expiry) VALUES ($h, $n, 'secret', $e)";
                insert.Parameters.AddWithValue("$h", host);
                insert.Parameters.AddWithValue("$n", cookie);
                insert.Parameters.AddWithValue("$e", expiry);
                insert.ExecuteNonQuery();
            }
        }

        return db;
    }

    private static long InAYear => DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

    [Fact]
    public void NoFirefoxFolder_IsNotFound()
    {
        Assert.Equal(FirefoxYouTubeLogin.State.FirefoxNotFound, FirefoxYouTubeLogin.Check([Path.Combine(_root, "missing")]));
    }

    [Fact]
    public void HasProfile_OnlyWithACookieDatabase()
    {
        Assert.False(FirefoxYouTubeLogin.HasProfile([_root, Path.Combine(_root, "missing")]));
        Profile("abc.default-release");
        Assert.True(FirefoxYouTubeLogin.HasProfile([_root]));
    }

    [Fact]
    public void ProfileWithYouTubeLogin_IsLoggedIn()
    {
        Profile("abc.default-release", (".youtube.com", "LOGIN_INFO", InAYear));

        Assert.Equal(FirefoxYouTubeLogin.State.LoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }

    [Fact]
    public void ProfileWithoutLogin_OrExpired_IsNotLoggedIn()
    {
        Profile("abc.default-release", (".youtube.com", "PREF", InAYear), (".youtube.com", "LOGIN_INFO", 1000));

        Assert.Equal(FirefoxYouTubeLogin.State.NotLoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }

    [Fact]
    public void ExpiryInMilliseconds_AsNewerFirefoxStoresIt()
    {
        long inAYearMs = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeMilliseconds();
        Profile("abc.default-release", (".youtube.com", "LOGIN_INFO", inAYearMs));
        Assert.Equal(FirefoxYouTubeLogin.State.LoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }

    [Fact]
    public void ExpiredInMilliseconds_IsNotLoggedIn()
    {
        long yesterdayMs = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        Profile("abc.default-release", (".youtube.com", "LOGIN_INFO", yesterdayMs));
        Assert.Equal(FirefoxYouTubeLogin.State.NotLoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }

    [Fact]
    public void LoginStillInTheWalFile_IsFound()
    {
        // Bug: a fresh login sits in Firefox's cookies.sqlite-wal; the copy was opened read-only, which SQLite refuses
        // for a WAL database without its -shm file — the check failed and said "not logged in".
        string db = Profile("abc.default-release");
        using SqliteConnection c = new($"Data Source={db};Pooling=False");
        c.Open();
        using (SqliteCommand wal = c.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
            wal.ExecuteNonQuery();
        }

        using (SqliteCommand insert = c.CreateCommand())
        {
            insert.CommandText = "INSERT INTO moz_cookies (host, name, value, expiry) VALUES ('.youtube.com', 'LOGIN_INFO', 'secret', $e)";
            insert.Parameters.AddWithValue("$e", DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }

        // Firefox still has it open: the row is only in the -wal file.
        Assert.True(File.Exists(db + "-wal"));
        Assert.Equal(FirefoxYouTubeLogin.State.LoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }

    [Fact]
    public void SeveralProfiles_TheMostRecentlyUsedCounts()
    {
        // As yt-dlp picks it: the profile whose cookie database changed last.
        string old = Profile("old.default", (".youtube.com", "LOGIN_INFO", InAYear));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        Profile("new.default-release");

        Assert.Equal(FirefoxYouTubeLogin.State.NotLoggedIn, FirefoxYouTubeLogin.Check([_root]));
    }
}
