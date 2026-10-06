namespace UltrastarDJ.Core.Songs;

/// <summary>
/// A still picture for a song in the preview / Game Player box, shown until the video plays. Either a local image
/// file or a YouTube thumbnail (fetched and cached by the app).
/// </summary>
public sealed record SongPicture(string? FilePath, string? YouTubeId)
{
    public static SongPicture File(string path) => new(path, null);
    public static SongPicture YouTubeThumbnail(string youTubeId) => new(null, youTubeId);

    /// <summary>
    /// The song's picture, best first: its own cover (the artwork, as on the prototype's get-ready screen), else the
    /// YouTube thumbnail when YouTube is the video source (USDB songs have no local files), else the background.
    /// A local video has no thumbnail: its own first frame is the fallback.
    /// </summary>
    public static IReadOnlyList<SongPicture> Candidates(Song song)
    {
        List<SongPicture> list = [];
        if (!string.IsNullOrEmpty(song.CoverPath))
        {
            list.Add(File(song.CoverPath));
        }

        if (song.HasYouTube && !song.HasLocalVideo)
        {
            list.Add(YouTubeThumbnail(song.YouTubeId!));
        }

        if (!string.IsNullOrEmpty(song.BackgroundPath))
        {
            list.Add(File(song.BackgroundPath));
        }

        return list;
    }

    /// <summary>
    /// What fills the beamer while a song without video plays (prototype: "background image fills beamer"):
    /// the background, else the cover.
    /// </summary>
    public static IReadOnlyList<SongPicture> BackdropCandidates(Song song)
    {
        List<SongPicture> list = [];
        if (!string.IsNullOrEmpty(song.BackgroundPath))
        {
            list.Add(File(song.BackgroundPath));
        }

        if (!string.IsNullOrEmpty(song.CoverPath))
        {
            list.Add(File(song.CoverPath));
        }

        return list;
    }
}
