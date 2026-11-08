namespace UltrastarDJ.Core.Playback;

/// <summary>
/// The single time authority during a song. 0 = game time 0 (audio start, after any
/// <c>#VIDEOGAP</c> handling); <c>#GAP</c>, lyrics and notes are all relative to this.
/// </summary>
public interface IGameClock
{
    /// <summary>Seconds of game time. Monotonic while running; frozen while paused.</summary>
    double PositionSec { get; }

    bool IsRunning { get; }

    /// <summary>How fast game time runs against real time (1 = normal, 0.5 = half speed to practise).</summary>
    double Speed { get; }
}
