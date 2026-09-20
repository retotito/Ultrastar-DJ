using Microsoft.Extensions.Logging;
using UltrastarDJ.Media;

// Headless media smoke test:
//   dotnet run --project tools/MediaSmoke -- <video-file-or-youtube-id> [audio-file|-] [audio-device]
string input = args.Length > 0 ? args[0] : throw new ArgumentException("usage: MediaSmoke <file|youtubeId> [audioFile|-] [device]");
string? audioFile = args.Length > 1 && args[1] != "-" ? args[1] : null;
using ILoggerFactory loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; }).SetMinimumLevel(LogLevel.Debug));
ILogger log = loggers.CreateLogger("smoke");

string? ytdlp = FindYtDlp();
log.LogInformation("yt-dlp: {Path}", ytdlp ?? "(not found)");
MpvPlayerFactory factory = new(new MpvOptions(ytdlp), loggers);

await using MediaChannel game = new(MediaChannelKind.Game, factory, loggers);
IReadOnlyList<AudioOutputDevice> devices = await game.ListAudioDevicesAsync();
foreach (AudioOutputDevice d in devices)
{
    log.LogInformation("device: {Id} — {Name}", d.Id, d.Name);
}

if (args.Length > 2)
{
    game.DeviceId = args[2];
}

int frames = 0;
FrameRef last = default;
game.Frames.FrameReady += f => { frames++; last = f; };

SongMedia song = input.Length == 11 && !File.Exists(input)
    ? new SongMedia { YouTubeId = input, AudioPath = audioFile }
    : new SongMedia { VideoPath = input, AudioPath = audioFile };
MediaPlan plan = MediaSourceResolver.Resolve(song);
log.LogInformation("plan: case {Case}", plan.Case);

await game.LoadAsync(plan);
log.LogInformation("loaded: state={State} duration={Duration}", game.State, game.Duration);
game.Play();

for (int i = 0; i < 12; i++)
{
    await Task.Delay(500);
    log.LogInformation("t={T:F2}s state={State} level={Level:F3} frames={Frames} size={W}x{H} drift={Drift:+0;-0}ms",
        game.Clock!.PositionSec, game.State, game.LevelRms, frames, last.Width, last.Height, game.VisualDriftSec * 1000);
}

game.Seek(30);
await Task.Delay(1500);
log.LogInformation("after seek 30: t={T:F2}s", game.Clock!.PositionSec);
game.Pause();
await Task.Delay(500);
log.LogInformation("paused: state={State} t={T:F2}s", game.State, game.Clock!.PositionSec);
await game.UnloadAsync();
log.LogInformation("done. frames={Frames}", frames);

static string? FindYtDlp()
{
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
    {
        string p = Path.Combine(dir.FullName, "natives", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "yt-dlp");
        if (File.Exists(p))
        {
            return p;
        }
    }

    return null;
}
