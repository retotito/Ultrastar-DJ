using Microsoft.Extensions.Logging;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// Every 30 s one log line: the process's memory (what macOS sees and may kill the app for), the .NET heap inside it,
/// and how much was allocated since the last line. Process memory growing while the heap stays flat points below .NET
/// (rendering, mpv); both growing points at our objects. Added after macOS killed the app at 23 GB.
/// </summary>
public static class MemoryLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static Timer? _timer;
    private static long _lastAllocated;

    public static void Start(ILogger log)
    {
        _lastAllocated = GC.GetTotalAllocatedBytes();
        _timer = new Timer(_ => Write(log), null, Interval, Interval);
    }

    private static void Write(ILogger log)
    {
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        long allocated = GC.GetTotalAllocatedBytes();
        log.LogInformation(
            "Memory: process {ProcessMb:F0} MB, .NET heap {HeapMb:F0} MB (committed {CommittedMb:F0} MB), allocated {RateMb:F1} MB/s, gen2 collections {Gen2}",
            Environment.WorkingSet / 1048576.0, gc.HeapSizeBytes / 1048576.0, gc.TotalCommittedBytes / 1048576.0,
            (allocated - _lastAllocated) / 1048576.0 / Interval.TotalSeconds, GC.CollectionCount(2));
        _lastAllocated = allocated;
    }
}
