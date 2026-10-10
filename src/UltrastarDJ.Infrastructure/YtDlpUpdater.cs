using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using UltrastarDJ.Core.Localization;

namespace UltrastarDJ.Infrastructure;

/// <summary>
/// Keeps yt-dlp current without a terminal: asks GitHub for the newest release and installs its folder build into the
/// app's data folder (<c>sidecars/yt-dlp/</c>), which <see cref="SidecarLocator"/> searches first — a signed app bundle
/// may not change its own files. <c>yt-dlp -U</c> cannot update folder builds, so this swaps the folder itself.
/// </summary>
public sealed class YtDlpUpdater(HttpClient http, AppPaths paths)
{
    private const string Releases = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
    private const string Download = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

    /// <summary>Where updates go; searched before the bundled copy.</summary>
    public string SidecarsDir => Path.Combine(paths.Data, "sidecars");

    /// <summary>The newest release's version ("2026.10.05").</summary>
    public async Task<string?> LatestVersionAsync(CancellationToken ct = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, Releases);
        req.Headers.UserAgent.ParseAdd("UltrastarDJ");   // GitHub refuses API calls without one
        using HttpResponseMessage res = await http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        Release? r = await res.Content.ReadFromJsonAsync<Release>(ct).ConfigureAwait(false);
        return r?.TagName;
    }

    /// <summary>Downloads and installs the newest folder build; returns the installed executable and its version.</summary>
    /// <exception cref="HttpRequestException">Download failed.</exception>
    /// <exception cref="IOException">Could not swap the folder (a song loading right now on Windows keeps files open).</exception>
    public async Task<(string Path, string Version)> InstallLatestAsync(CancellationToken ct = default)
    {
        (string asset, string inside) = OperatingSystem.IsWindows() ? ("yt-dlp_win.zip", "yt-dlp.exe")
            : OperatingSystem.IsMacOS() ? ("yt-dlp_macos.zip", "yt-dlp_macos")
            : throw new PlatformNotSupportedException(Text.T("ytdlp.platform", "yt-dlp updates are supported on macOS and Windows."));
        string exeName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

        // Staged next to the target (same volume), so the final swap is a rename.
        Directory.CreateDirectory(SidecarsDir);
        string stage = Path.Combine(SidecarsDir, "yt-dlp.new");
        string target = Path.Combine(SidecarsDir, "yt-dlp");
        string old = Path.Combine(SidecarsDir, "yt-dlp.old");
        DeleteDir(stage);
        DeleteDir(old);

        string zip = Path.Combine(SidecarsDir, asset);
        using (HttpResponseMessage res = await http.GetAsync(Download + asset, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            res.EnsureSuccessStatusCode();
            await using FileStream file = File.Create(zip);
            await res.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(zip, stage);
        File.Delete(zip);
        string exe = Path.Combine(stage, exeName);
        if (!string.Equals(inside, exeName, StringComparison.Ordinal))
        {
            File.Move(Path.Combine(stage, inside), exe);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        // Proves the download runs, and pays macOS's first-run scan (~9 s) here instead of on the next song.
        string version = await VersionOfAsync(exe, ct).ConfigureAwait(false)
            ?? throw new IOException(Text.T("ytdlp.does_not_start", "The downloaded yt-dlp does not start."));

        if (Directory.Exists(target))
        {
            Directory.Move(target, old);
        }

        Directory.Move(stage, target);
        DeleteDir(old);
        return (Path.Combine(target, exeName), version);
    }

    /// <summary>Runs <c>yt-dlp --version</c>; null if it does not start.</summary>
    public static async Task<string?> VersionOfAsync(string exe, CancellationToken ct = default)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo(exe, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("no process");
            string v = (await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return p.ExitCode == 0 && v.Length > 0 ? v : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    private static void DeleteDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed record Release([property: JsonPropertyName("tag_name")] string? TagName);
}
