using System.Runtime.InteropServices;

namespace UltrastarDJ.Infrastructure;

/// <summary>
/// Finds sidecar executables (yt-dlp, ffmpeg): next to the app in <c>natives/</c>, then the
/// repository's <c>natives/&lt;rid&gt;/</c> during development, then <c>PATH</c>.
/// </summary>
public sealed class SidecarLocator
{
    private readonly Lazy<IReadOnlyList<string>> _dirs = new(BuildDirs);

    public string? YtDlp => Find("yt-dlp");
    public string? Ffmpeg => Find("ffmpeg");

    public string? Find(string name)
    {
        string file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (string dir in _dirs.Value)
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path))
            {
                return path;
            }
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static List<string> BuildDirs()
    {
        List<string> dirs = [Path.Combine(AppContext.BaseDirectory, "natives"), AppContext.BaseDirectory];
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "natives", RuntimeInformation.RuntimeIdentifier);
            if (Directory.Exists(candidate))
            {
                dirs.Add(candidate);
                break;
            }
        }

        return dirs;
    }
}
