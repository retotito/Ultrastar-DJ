using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Services;

/// <summary>Owns the two media channels for the lifetime of the app.</summary>
public sealed class MediaService : IAsyncDisposable
{
    public MediaService(SidecarLocator sidecars, ILoggerFactory loggers)
    {
        ILogger log = loggers.CreateLogger<MediaService>();
        string? ytdlp = sidecars.YtDlp;
        if (ytdlp is null)
        {
            log.LogWarning("yt-dlp not found — YouTube playback will fail. Run scripts/fetch-natives.");
        }
        else
        {
            log.LogInformation("yt-dlp: {Path}", ytdlp);
            _ = Task.Run(() => WarmUpYtDlp(ytdlp, log));
        }

        MpvPlayerFactory factory = new(new MpvOptions(ytdlp), loggers);
        Game = new MediaChannel(MediaChannelKind.Game, factory, loggers);
        Preview = new MediaChannel(MediaChannelKind.Preview, factory, loggers);
    }

    public MediaChannel Game { get; }
    public MediaChannel Preview { get; }

    /// <summary>
    /// macOS scans a new executable on its first run (~9 s for yt-dlp). Running it once at start moves
    /// that cost off the first YouTube load, and logs the version for support.
    /// </summary>
    private static async Task WarmUpYtDlp(string path, ILogger log)
    {
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            using Process p = Process.Start(new ProcessStartInfo(path, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("process did not start");
            string version = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            log.LogInformation("yt-dlp {Version} (started in {Ms} ms)", version, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            log.LogWarning(ex, "yt-dlp could not be started — YouTube playback will fail.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Game.DisposeAsync();
        await Preview.DisposeAsync();
    }
}
