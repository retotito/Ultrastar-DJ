using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace UltrastarDJ.Infrastructure;

/// <summary>
/// Settings → YouTube: is Firefox logged in to YouTube (the login yt-dlp borrows with <c>cookies-from-browser</c>)?
/// Looks only on this computer, in the cookie database of the profile yt-dlp picks (the one used last), and only at
/// whether a YouTube login cookie exists and has not expired — never at its value, never asking YouTube. Which account
/// it is cannot be told from it: <see cref="OpenAccountPage"/> shows it in Firefox.
/// </summary>
public static class FirefoxYouTubeLogin
{
    public enum State
    {
        FirefoxNotFound,
        NotLoggedIn,
        LoggedIn,
    }

    // YouTube's own login cookie, set when someone signs in on youtube.com.
    private const string LoginCookie = "LOGIN_INFO";

    /// <summary>Where Firefox keeps its profiles on this system (as yt-dlp searches them, snap and flatpak included).</summary>
    public static IReadOnlyList<string> ProfileFolders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles")];
        }

        if (OperatingSystem.IsMacOS())
        {
            return [Path.Combine(home, "Library", "Application Support", "Firefox", "Profiles")];
        }

        return
        [
            Path.Combine(home, ".mozilla", "firefox"),
            Path.Combine(home, "snap", "firefox", "common", ".mozilla", "firefox"),
            Path.Combine(home, ".var", "app", "org.mozilla.firefox", ".mozilla", "firefox"),
        ];
    }

    public static State Check() => Check(ProfileFolders());

    /// <summary>Is there a Firefox profile yt-dlp could read? Without one, asking for its login fails every YouTube song.</summary>
    public static bool HasProfile() => HasProfile(ProfileFolders());

    public static bool HasProfile(IEnumerable<string> profileFolders)
        => profileFolders.Where(Directory.Exists).Any(f => Directory.EnumerateFiles(f, "cookies.sqlite", SearchOption.AllDirectories).Any());

    /// <param name="profileFolders">Folders holding Firefox profiles (one folder per profile below them).</param>
    public static State Check(IEnumerable<string> profileFolders)
    {
        FileInfo? newest = profileFolders
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "cookies.sqlite", SearchOption.AllDirectories))
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null)
        {
            return State.FirefoxNotFound;
        }

        // Firefox keeps the database open and locked (recent changes, a fresh login among them, in its -wal file): read
        // a copy, made the way yt-dlp makes it.
        string copy = Path.Combine(Path.GetTempPath(), "usdj-firefox-cookies-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            CopyDespiteLock(newest.FullName, copy);
            if (File.Exists(newest.FullName + "-wal"))
            {
                CopyDespiteLock(newest.FullName + "-wal", copy + "-wal");
            }

            using SqliteConnection c = new($"Data Source={copy};Pooling=False");
            c.Open();
            using SqliteCommand q = c.CreateCommand();
            // Newer Firefox stores the expiry in milliseconds, older in seconds.
            q.CommandText = "SELECT COUNT(*) FROM moz_cookies WHERE host LIKE '%youtube.com' AND name = $name"
                + " AND (CASE WHEN expiry > 100000000000 THEN expiry / 1000 ELSE expiry END) > $now";
            q.Parameters.AddWithValue("$name", LoginCookie);
            q.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return Convert.ToInt64(q.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0 ? State.LoggedIn : State.NotLoggedIn;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return State.NotLoggedIn;
        }
        finally
        {
            TryDelete(copy);
            TryDelete(copy + "-wal");
            TryDelete(copy + "-shm");
        }
    }

    /// <summary>YouTube's account page in Firefox — its profile picture and name show which account is logged in.</summary>
    public static void OpenAccountPage()
    {
        const string url = "https://www.youtube.com/account";
        if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", ["-a", "Firefox", url]);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Firefox registers itself under "App Paths": the shell finds it by name.
            Process.Start(new ProcessStartInfo("firefox", url) { UseShellExecute = true });
        }
        else
        {
            Process.Start("firefox", [url]);
        }
    }

    // .NET honours the lock Firefox holds on its open database and refuses to copy it ("used by another process");
    // the system's cp (macOS, Linux) does not. On Windows Firefox allows reading alongside it.
    private static void CopyDespiteLock(string source, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using FileStream from = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream to = File.Create(target);
            from.CopyTo(to);
            return;
        }

        using Process cp = Process.Start(new ProcessStartInfo("cp", [source, target]) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new IOException("cp did not start");
        cp.WaitForExit(5000);
        if (cp.ExitCode != 0 || !File.Exists(target))
        {
            throw new IOException($"Could not copy {source}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
