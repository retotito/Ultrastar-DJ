using Avalonia.Media.Imaging;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Services;

/// <summary>Loads a song's still pictures (picture / backdrop, see <see cref="SongPicture"/>) as bitmaps.</summary>
public static class SongImages
{
    public static Task<Bitmap?> PictureAsync(Song song, ThumbnailService thumbnails) => FirstAsync(SongPicture.Candidates(song), thumbnails);

    public static Task<Bitmap?> BackdropAsync(Song song, ThumbnailService thumbnails) => FirstAsync(SongPicture.BackdropCandidates(song), thumbnails);

    /// <summary>The first candidate that can be loaded, or null.</summary>
    private static async Task<Bitmap?> FirstAsync(IReadOnlyList<SongPicture> candidates, ThumbnailService thumbnails)
    {
        foreach (SongPicture candidate in candidates)
        {
            string? path = candidate.YouTubeId is { } id ? await thumbnails.GetAsync(id) : candidate.FilePath;
            if (Load(path) is { } bitmap)
            {
                return bitmap;
            }
        }

        return null;
    }

    public static Bitmap? Load(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
