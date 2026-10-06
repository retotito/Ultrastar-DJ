namespace UltrastarDJ.Core.Playback;

/// <summary>What the combined play/pause button does in a state.</summary>
public enum PlayButton
{
    Play,
    Pause,
    Resume,
}

/// <summary>
/// Which Game Player transport action is allowed in which state (docs/03-game-engine.md "Playback state machine").
/// Four buttons: Home (beamers to the start view, song stays loaded), Get ready, Play/Pause, Stop (score screen).
/// </summary>
public static class PlaybackRules
{
    /// <summary>Back to the beamers' start view. Never while a song runs — that is Stop's job.</summary>
    public static bool CanHome(PlaybackState s) => s is PlaybackState.Preview or PlaybackState.Score;

    public static bool CanGetReady(PlaybackState s, bool anyDisplayOpen)
        => anyDisplayOpen && s is PlaybackState.Loaded or PlaybackState.Score;

    /// <summary>Starts the countdown; from the score screen it replays the same song from the beginning.</summary>
    public static bool CanPlay(PlaybackState s, bool anyDisplayOpen)
        => anyDisplayOpen && s is PlaybackState.Loaded or PlaybackState.Preview or PlaybackState.Score;

    public static bool CanPause(PlaybackState s) => s == PlaybackState.Playing;
    public static bool CanResume(PlaybackState s) => s == PlaybackState.Paused;
    public static bool CanStop(PlaybackState s) => s is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused;

    /// <summary>Pause only once the song plays — during the countdown the button still shows play (disabled).</summary>
    public static PlayButton PlayButtonFor(PlaybackState s) => s switch
    {
        PlaybackState.Playing => PlayButton.Pause,
        PlaybackState.Paused => PlayButton.Resume,
        _ => PlayButton.Play,
    };

    public static bool PlayButtonEnabled(PlaybackState s, bool anyDisplayOpen) => PlayButtonFor(s) switch
    {
        PlayButton.Pause => CanPause(s),
        PlayButton.Resume => CanResume(s),
        _ => CanPlay(s, anyDisplayOpen),
    };
}
