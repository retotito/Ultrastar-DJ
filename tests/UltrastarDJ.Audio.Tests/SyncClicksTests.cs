using System.Diagnostics;
using UltrastarDJ.Audio.Monitor;

namespace UltrastarDJ.Audio.Tests;

public class SyncClicksTests
{
    [Fact]
    public void Clicks_StartExactlyOnEveryPeriod()
    {
        SyncClicks clicks = new(rate: 1000, periodSec: 1.0, now: () => 0);
        float[] buf = new float[2 * 3000];
        for (int block = 0; block < 6; block++)
        {
            clicks.OnOutput(buf.AsSpan(block * 1000, 1000), 500, 2);
        }

        // Sound right after 0, 1000 and 2000 frames, silence in between.
        Assert.NotEqual(0, buf[2 * 1]);
        Assert.NotEqual(0, buf[2 * 1001]);
        Assert.NotEqual(0, buf[2 * 2001]);
        Assert.Equal(0, buf[2 * 500]);
        Assert.Equal(0, buf[2 * 1500]);
    }

    [Fact]
    public void FirstClick_IsTheEarliestNowMinusWrittenTime()
    {
        long now = 0;
        long tick = Stopwatch.Frequency;
        SyncClicks clicks = new(rate: 1000, periodSec: 1.0, now: () => now);
        float[] buf = new float[2 * 100];

        Assert.Null(clicks.FirstClickTimestamp);
        now = 10 * tick;
        clicks.OnOutput(buf, 100, 2);            // 10 s − 0
        now = 10 * tick;
        clicks.OnOutput(buf, 100, 2);            // prefill: 10 s − 0.1 s
        now = 10 * tick + tick / 5;
        clicks.OnOutput(buf, 100, 2);            // woken late: 10.2 s − 0.2 s, not earlier

        Assert.Equal(10 * tick - tick / 10, clicks.FirstClickTimestamp);
    }
}
