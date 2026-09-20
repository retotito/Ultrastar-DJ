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
        }

        MpvPlayerFactory factory = new(new MpvOptions(ytdlp), loggers);
        Game = new MediaChannel(MediaChannelKind.Game, factory, loggers);
        Preview = new MediaChannel(MediaChannelKind.Preview, factory, loggers);
    }

    public MediaChannel Game { get; }
    public MediaChannel Preview { get; }

    public async ValueTask DisposeAsync()
    {
        await Game.DisposeAsync();
        await Preview.DisposeAsync();
    }
}
