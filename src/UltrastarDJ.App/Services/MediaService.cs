using Microsoft.Extensions.Logging;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Services;

/// <summary>Owns the two media channels for the lifetime of the app.</summary>
public sealed class MediaService : IAsyncDisposable
{
    private readonly MpvPlayerFactory _factory;

    private readonly AppSettingsService _settings;

    public MediaService(SidecarLocator sidecars, AppSettingsService settings, ILoggerFactory loggers)
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

        _settings = settings;
        _factory = new MpvPlayerFactory(new MpvOptions(ytdlp, LoginBrowser(settings)), loggers);
        // Settings → YouTube login: every song loaded from now on uses it, no restart needed.
        settings.Changed += RefreshYouTubeLogin;
        Game = new MediaChannel(MediaChannelKind.Game, _factory, loggers);
        Preview = new MediaChannel(MediaChannelKind.Preview, _factory, loggers);
    }

    public MediaChannel Game { get; }
    public MediaChannel Preview { get; }

    /// <summary>The yt-dlp mpv uses (YtDlpService reads its version at start).</summary>
    public string? YtDlpPath => _factory.Options.YtDlpPath;

    /// <summary>After a yt-dlp update: every song loaded from now on uses the new copy, no restart needed.</summary>
    public void SetYtDlpPath(string path) => _factory.Options = _factory.Options with { YtDlpPath = path };

    /// <summary>The setting changed, or Firefox may have come (or gone) since: songs loaded from now on follow.</summary>
    public void RefreshYouTubeLogin() => _factory.Options = _factory.Options with { YouTubeLoginBrowser = LoginBrowser(_settings) };

    // Switched on but no Firefox on this computer: songs play without a login instead of all failing.
    private static string? LoginBrowser(AppSettingsService settings) => settings.YouTubeLoginFirefox && FirefoxYouTubeLogin.HasProfile() ? "firefox" : null;

    public async ValueTask DisposeAsync()
    {
        await Game.DisposeAsync();
        await Preview.DisposeAsync();
    }
}
