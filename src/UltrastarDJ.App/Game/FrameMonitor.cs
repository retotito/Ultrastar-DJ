namespace UltrastarDJ.App.Game;

/// <summary>
/// How evenly the displays drew during a song. Each display reports the time between its frames
/// (<see cref="Controls.GameOverlayControl"/>, UI thread); after the song the DJ window asks whether this computer
/// kept up — if not, it suggests Settings → Reduce effects once. It never switches anything itself.
/// </summary>
public static class FrameMonitor
{
    /// <summary>A frame later than this is a visible stutter (under 20 frames per second).</summary>
    public const double SlowFrameMs = 50;
    // Gaps this long are pauses (song paused, window hidden), not slow frames.
    private const double PauseMs = 1000;

    private static long _frames;
    private static long _slow;

    public static void Record(double sinceLastMs)
    {
        if (sinceLastMs is <= 0 or >= PauseMs)
        {
            return;
        }

        _frames++;
        if (sinceLastMs > SlowFrameMs)
        {
            _slow++;
        }
    }

    /// <summary>The frames since the last call, and how many of them were slow; starts counting afresh.</summary>
    public static (long Frames, long Slow) Take()
    {
        (long, long) counts = (_frames, _slow);
        _frames = 0;
        _slow = 0;
        return counts;
    }

    /// <summary>
    /// Too slow for the effects: at least 10 s of song drawn (about 600 frames at 60 Hz; 300 is the floor for slower
    /// screens) and at least one frame in twenty a stutter.
    /// </summary>
    public static bool StruggledWith(long frames, long slow) => frames >= 300 && slow * 20 >= frames;
}
