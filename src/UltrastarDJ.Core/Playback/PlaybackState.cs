namespace UltrastarDJ.Core.Playback;

/// <summary>
/// Transport state of the game channel. Owned by the playback service; beamers only observe it.
/// See docs/03-game-engine.md for the transition rules.
/// </summary>
public enum PlaybackState
{
    /// <summary>No song loaded.</summary>
    Idle,
    /// <summary>Song loaded and media ready; nothing shown on beamers yet.</summary>
    Loaded,
    /// <summary>"Get ready" title screen on the beamers.</summary>
    Preview,
    /// <summary>3-2-1 running on the beamers; media not started.</summary>
    Countdown,
    Playing,
    Paused,
    /// <summary>Song finished or stopped; score screen visible. Song stays loaded.</summary>
    Score,
}
