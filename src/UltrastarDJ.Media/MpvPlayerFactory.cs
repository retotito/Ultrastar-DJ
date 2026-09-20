using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Media;

public sealed class MpvPlayerFactory(MpvOptions options, ILoggerFactory loggers) : IMediaPlayerFactory
{
    public IMediaPlayer Create(string name, bool video) => new MpvPlayer(name, video, options, loggers.CreateLogger<MpvPlayer>());
}
