using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class MediaGameClockTests
{
    /// <summary>A fake audio player and wall clock the test moves by hand.</summary>
    private sealed class Rig
    {
        public double Now;
        public double Reported;
        public bool Playing = true;
        public double Origin;

        public MediaGameClock Clock() => new(() => Reported, () => Playing, () => 1.0, Origin, () => Now);
    }

    [Fact]
    public void PositionSec_SubtractsOrigin()
    {
        Rig r = new() { Reported = 25, Playing = false, Origin = 19.5 };

        Assert.Equal(5.5, r.Clock().PositionSec, 3);
    }

    [Fact]
    public void PositionSec_NeverNegative()
    {
        Rig r = new() { Reported = 1, Playing = false, Origin = 5 };

        Assert.Equal(0, r.Clock().PositionSec);
    }

    [Fact]
    public void Paused_FollowsTheAudioExactly()
    {
        Rig r = new() { Reported = 10, Playing = false };
        MediaGameClock c = r.Clock();

        r.Now = 1;
        Assert.Equal(10, c.PositionSec, 6);
        r.Reported = 42; // seek while paused
        Assert.Equal(42, c.PositionSec, 6);
    }

    [Fact]
    public void Playing_AdvancesWithTheWallClockBetweenReports()
    {
        Rig r = new() { Reported = 10 };
        MediaGameClock c = r.Clock();
        _ = c.PositionSec;

        r.Now = 0.016;
        Assert.Equal(10.016, c.PositionSec, 4);
        r.Now = 0.300; // no report for 300 ms: keeps running (no clamp, no stall)
        Assert.Equal(10.300, c.PositionSec, 4);
    }

    [Fact]
    public void AudioBehind_HoldsInsteadOfGoingBackwards()
    {
        Rig r = new() { Reported = 10 };
        MediaGameClock c = r.Clock();
        _ = c.PositionSec;

        r.Now = 0.100;
        double ahead = c.PositionSec;     // 10.100
        r.Reported = 10.060;              // audio says 40 ms earlier
        double held = c.PositionSec;
        r.Now = 0.120;
        double stillHeld = c.PositionSec;

        Assert.True(held >= ahead, "never backwards");
        Assert.Equal(held, stillHeld, 6);
    }

    [Fact]
    public void AudioAhead_JumpsForward()
    {
        Rig r = new() { Reported = 10 };
        MediaGameClock c = r.Clock();
        _ = c.PositionSec;

        r.Now = 0.050;
        r.Reported = 10.090; // audio 40 ms ahead of the timer
        Assert.True(c.PositionSec > 10.075);
    }

    [Fact]
    public void BigJump_FollowsImmediately()
    {
        Rig r = new() { Reported = 10 };
        MediaGameClock c = r.Clock();
        _ = c.PositionSec;

        r.Now = 0.02;
        r.Reported = 70; // seek
        Assert.Equal(70, c.PositionSec, 3);
    }

    [Fact]
    public void JitteryReports_StayMonotonicAndClose()
    {
        // mpv-like: a report every ~50 ms, each off by up to ±15 ms. Render reads at 60 Hz.
        Rig r = new() { Reported = 0 };
        MediaGameClock c = r.Clock();
        Random rnd = new(7);
        double last = c.PositionSec;
        double nextReport = 0;
        for (double t = 0; t < 10; t += 1 / 60.0)
        {
            r.Now = t;
            if (t >= nextReport)
            {
                r.Reported = Math.Max(0, t + (rnd.NextDouble() - 0.5) * 0.03);
                nextReport += 0.05;
            }

            double p = c.PositionSec;
            Assert.True(p >= last - 1e-9, $"went backwards at {t:F3}: {p} < {last}");
            Assert.InRange(p - t, -0.05, 0.05);
            last = p;
        }
    }

    [Fact]
    public void IsRunning_ReflectsPlayer()
    {
        Rig r = new() { Playing = false };
        MediaGameClock c = r.Clock();

        Assert.False(c.IsRunning);
        r.Playing = true;
        Assert.True(c.IsRunning);
    }
}
