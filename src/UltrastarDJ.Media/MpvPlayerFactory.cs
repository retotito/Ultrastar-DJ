using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Media;

/// <summary>
/// Creates mpv players. <see cref="Options"/> may change (a yt-dlp update, the login setting): every new player takes
/// the current ones. All players share one <see cref="YouTubeResolver"/>, so preview and game look a song up once.
/// </summary>
public sealed class MpvPlayerFactory : IMediaPlayerFactory
{
    private readonly ILoggerFactory _loggers;

    public MpvPlayerFactory(MpvOptions options, ILoggerFactory loggers)
    {
        Options = options;
        _loggers = loggers;
        YouTube = new YouTubeResolver(() => Options, loggers.CreateLogger<YouTubeResolver>());
    }

    public MpvOptions Options { get; set; }

    public YouTubeResolver YouTube { get; }

    public IMediaPlayer Create(string name, bool video) => new MpvPlayer(name, video, Options, _loggers.CreateLogger<MpvPlayer>(), YouTube);
}
