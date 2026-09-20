using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class MediaGameClockTests
{
    [Fact]
    public void PositionSec_SubtractsOrigin()
    {
        MediaGameClock clock = new(() => 25.0, () => false, () => 1.0, originSec: 19.5);

        Assert.Equal(5.5, clock.PositionSec, 3);
    }

    [Fact]
    public void PositionSec_NeverNegative()
    {
        MediaGameClock clock = new(() => 1.0, () => false, () => 1.0, originSec: 5);

        Assert.Equal(0, clock.PositionSec);
    }

    [Fact]
    public void PositionSec_WhilePaused_DoesNotExtrapolate()
    {
        MediaGameClock clock = new(() => 10.0, () => false, () => 1.0);

        double first = clock.PositionSec;
        Thread.Sleep(30);
        Assert.Equal(first, clock.PositionSec);
    }

    [Fact]
    public void PositionSec_WhilePlaying_ExtrapolatesButIsClamped()
    {
        MediaGameClock clock = new(() => 10.0, () => true, () => 1.0);

        double first = clock.PositionSec;
        Thread.Sleep(200);
        double later = clock.PositionSec;

        Assert.True(later > first, "clock should advance between reports");
        Assert.InRange(later, 10.0, 10.1 + 0.001);
    }

    [Fact]
    public void PositionSec_NewReport_ResetsExtrapolation()
    {
        double reported = 10.0;
        MediaGameClock clock = new(() => reported, () => true, () => 1.0);

        _ = clock.PositionSec;
        Thread.Sleep(50);
        reported = 12.0;

        Assert.InRange(clock.PositionSec, 12.0, 12.01);
    }

    [Fact]
    public void IsRunning_ReflectsPlayer()
    {
        bool playing = false;
        MediaGameClock clock = new(() => 0, () => playing, () => 1.0);

        Assert.False(clock.IsRunning);
        playing = true;
        Assert.True(clock.IsRunning);
    }
}
