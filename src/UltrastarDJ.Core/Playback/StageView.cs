namespace UltrastarDJ.Core.Playback;

/// <summary>What fills the background of a beamer / the Game Player box.</summary>
public enum StageLayer
{
    /// <summary>Nothing song-related (beamer start view, empty player).</summary>
    None,
    /// <summary>Song picture (<c>Songs.SongPicture.Candidates</c>: cover, YouTube thumbnail, background), blurred.</summary>
    BlurredPicture,
    /// <summary>Song picture, sharp.</summary>
    Picture,
    /// <summary>Song backdrop (<c>Songs.SongPicture.BackdropCandidates</c>: background, cover), sharp and dimmed — songs without video.</summary>
    Backdrop,
    /// <summary>The video; the song picture lies under it until its first frame.</summary>
    Video,
}

/// <summary>
/// One rule for the six media cases (docs/00-vision.md) on every display, so the Game Player box always shows what
/// the beamers show behind the game. Video cases (2, 3, 4, 6) differ from image cases (1, 5) only while the song runs.
/// </summary>
public static class StageView
{
    public static StageLayer Beamer(PlaybackState state, bool hasVideo) => state switch
    {
        PlaybackState.Preview or PlaybackState.Score => StageLayer.BlurredPicture,
        // The media only starts at "go": until then the picture, then the video takes over.
        PlaybackState.Countdown => StageLayer.Picture,
        PlaybackState.Playing or PlaybackState.Paused => hasVideo ? StageLayer.Video : StageLayer.Backdrop,
        _ => StageLayer.None,
    };

    /// <summary>The DJ's small monitor: the song picture whenever a song is loaded, the stage while it runs.</summary>
    public static StageLayer GamePlayerBox(PlaybackState state, bool hasVideo) => state switch
    {
        PlaybackState.Idle => StageLayer.None,
        PlaybackState.Playing or PlaybackState.Paused => hasVideo ? StageLayer.Video : StageLayer.Backdrop,
        _ => StageLayer.Picture,
    };
}
