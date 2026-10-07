using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Media;

/// <summary>Creates mpv players. <see cref="Options"/> may change (a yt-dlp update): every new player takes the current ones.</summary>
public sealed class MpvPlayerFactory(MpvOptions options, ILoggerFactory loggers) : IMediaPlayerFactory
{
    public MpvOptions Options { get; set; } = options;

    public IMediaPlayer Create(string name, bool video) => new MpvPlayer(name, video, Options, loggers.CreateLogger<MpvPlayer>());
}
