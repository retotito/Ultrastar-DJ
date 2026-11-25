using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// Logs when the UI thread stops answering for more than <see cref="Threshold"/>, and how long it lasted — so a freeze
/// the DJ saw once (Sprint 36: after switching the practice speed) shows in the log with what happened just before.
/// A background timer posts a tick to the UI thread every second and checks when the last one ran.
/// </summary>
public static class UiHangLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(2);
    private static Timer? _timer;
    private static long _lastTickTicks;
    private static long _hangStartTicks;

    public static void Start(ILogger log)
    {
        Interlocked.Exchange(ref _lastTickTicks, DateTime.UtcNow.Ticks);
        _timer = new Timer(_ => Check(log), null, Interval, Interval);
    }

    private static void Check(ILogger log)
    {
        DateTime now = DateTime.UtcNow;
        DateTime lastTick = new(Interlocked.Read(ref _lastTickTicks), DateTimeKind.Utc);
        long hangStart = Interlocked.Read(ref _hangStartTicks);
        if (now - lastTick > Threshold && hangStart == 0)
        {
            Interlocked.Exchange(ref _hangStartTicks, lastTick.Ticks);
            log.LogWarning("UI not responding for {Seconds:F1} s", (now - lastTick).TotalSeconds);
        }

        Dispatcher.UIThread.Post(() =>
        {
            DateTime answered = DateTime.UtcNow;
            Interlocked.Exchange(ref _lastTickTicks, answered.Ticks);
            long started = Interlocked.Exchange(ref _hangStartTicks, 0);
            if (started != 0)
            {
                log.LogWarning("UI responding again after {Seconds:F1} s", (answered - new DateTime(started, DateTimeKind.Utc)).TotalSeconds);
            }
        });
    }
}
