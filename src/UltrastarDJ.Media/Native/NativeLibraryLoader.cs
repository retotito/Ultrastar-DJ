using System.Runtime.InteropServices;

namespace UltrastarDJ.Media.Native;

/// <summary>
/// Locates native libraries shipped in <c>natives/</c> next to the executable (release) or in the
/// repository's <c>natives/&lt;rid&gt;/</c> folder (development), before falling back to the OS search path.
/// </summary>
public static class NativeLibraryLoader
{
    private static readonly Lazy<IReadOnlyList<string>> SearchDirs = new(BuildSearchDirs);

    /// <summary>Directory that contains the native libraries and sidecars, or null if none was found.</summary>
    public static string? NativesDirectory => SearchDirs.Value.FirstOrDefault(Directory.Exists);

    internal static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibMpv.LibraryName)
        {
            return 0;
        }

        foreach (string dir in SearchDirs.Value)
        {
            foreach (string candidate in CandidateFileNames())
            {
                string path = Path.Combine(dir, candidate);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
                {
                    return handle;
                }
            }
        }

        // Last resort: whatever the OS finds (e.g. Homebrew's libmpv on a dev machine).
        return NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "libmpv-2.dll" : "libmpv.2.dylib", out nint sys) ? sys : 0;
    }

    private static IEnumerable<string> CandidateFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "libmpv-2.dll";
            yield return "mpv-2.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "libmpv.2.dylib";
            yield return "libmpv.dylib";
        }
        else
        {
            yield return "libmpv.so.2";
            yield return "libmpv.so";
        }
    }

    private static List<string> BuildSearchDirs()
    {
        List<string> dirs = [];
        string baseDir = AppContext.BaseDirectory;
        dirs.Add(Path.Combine(baseDir, "natives"));
        dirs.Add(baseDir);

        // Development: walk up from bin/ to the repo root and use natives/<rid>/.
        string rid = RuntimeInformation.RuntimeIdentifier;
        DirectoryInfo? dir = new(baseDir);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "natives", rid);
            if (Directory.Exists(candidate))
            {
                dirs.Add(candidate);
                break;
            }
        }

        return dirs;
    }
}
