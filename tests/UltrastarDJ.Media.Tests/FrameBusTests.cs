using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class FrameBusTests
{
    private sealed class FakeSource : IFrameSource
    {
        public event Action<FrameRef>? FrameReady;
        public void Emit(FrameRef f) => FrameReady?.Invoke(f);
    }

    [Fact]
    public void FansOutToAllSubscribers()
    {
        using FrameBus bus = new();
        FakeSource src = new();
        bus.SetSource(src);
        int a = 0, b = 0;
        bus.FrameReady += _ => a++;
        bus.FrameReady += _ => b++;

        src.Emit(new FrameRef(0, 4, 4, 16));

        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void SwappingSource_KeepsSubscribersAndDetachesOld()
    {
        using FrameBus bus = new();
        FakeSource first = new();
        FakeSource second = new();
        int received = 0;
        int changes = 0;
        bus.FrameReady += _ => received++;
        bus.SourceChanged += () => changes++;

        bus.SetSource(first);
        bus.SetSource(second);
        first.Emit(default);
        second.Emit(default);

        Assert.Equal(1, received);
        Assert.Equal(2, changes);
        Assert.True(bus.HasSource);
    }

    [Fact]
    public void SetSourceNull_ClearsHasSource()
    {
        using FrameBus bus = new();
        bus.SetSource(new FakeSource());
        bus.SetSource(null);

        Assert.False(bus.HasSource);
    }
}
