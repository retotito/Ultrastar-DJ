using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class MediaChannelTests
{
    private sealed class FakePlayer(string name) : IMediaPlayer
    {
        public string Name { get; } = name;
        public Task LoadAsync(MediaSource source, MediaLoadOptions options, CancellationToken ct = default) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Unload() { }
        public void Seek(TimeSpan position) { }
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan? Duration => TimeSpan.FromMinutes(3);
        public MediaState State => MediaState.Ready;
        public double Volume { get; set; }
        public bool Muted { get; set; }
        public double Speed { get; set; } = 1;
        public string AudioDevice { get; set; } = "auto";
        public void SetChannelRouting(int totalChannels, int offset) { }
        public double LevelRms => 0;
        public IReadOnlyList<AudioOutputDevice> ListAudioDevices() => [AudioOutputDevice.Auto];
        public IFrameSource? Frames => null;
        public event Action<MediaState>? StateChanged { add { } remove { } }
        public event Action<string>? ErrorOccurred { add { } remove { } }
        public event Action? EndReached { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory : IMediaPlayerFactory
    {
        public List<FakePlayer> Created { get; } = [];
        public IMediaPlayer Create(string name, bool video)
        {
            FakePlayer p = new(name);
            Created.Add(p);
            return p;
        }
    }

    [Fact]
    public async Task OutputChange_ReachesTheLoadedSong()
    {
        // Bug: with a song loaded, switching the game output to the headphones only applied to the next load —
        // Play still sounded on the old device.
        FakeFactory factory = new();
        MediaChannel channel = new(MediaChannelKind.Game, factory, NullLoggerFactory.Instance);
        channel.SetRouting("coreaudio/speakers", 2, 0);
        await channel.LoadAsync(MediaSourceResolver.Resolve(new SongMedia { AudioPath = "/songs/a.mp3" }));

        channel.SetRouting("coreaudio/headphones", 2, 0);

        Assert.Equal("coreaudio/headphones", factory.Created[0].AudioDevice);
    }
}
