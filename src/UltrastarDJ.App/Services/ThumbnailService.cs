using Microsoft.Extensions.Logging;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.App.Services;

/// <summary>
/// YouTube thumbnails for the player boxes, cached on disk (<c>cache/thumbs/&lt;id&gt;.jpg</c>) so they also show
/// offline next time. Uses YouTube's 16:9 "mqdefault" (320×180): the 4:3 sizes carry black bars.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private readonly string _dir;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ILogger<ThumbnailService> _log;

    public ThumbnailService(AppPaths paths, ILogger<ThumbnailService> log)
    {
        _dir = Path.Combine(paths.Cache, "thumbs");
        Directory.CreateDirectory(_dir);
        _log = log;
    }

    /// <summary>Local path of the thumbnail, downloading it first if needed; null when it cannot be had.</summary>
    public async Task<string?> GetAsync(string youTubeId, CancellationToken ct = default)
    {
        // Ids are 11 chars of [A-Za-z0-9_-] (Core.Songs.YouTubeId) — safe as a file name.
        string path = Path.Combine(_dir, youTubeId + ".jpg");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(new Uri($"https://i.ytimg.com/vi/{youTubeId}/mqdefault.jpg"), ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogDebug("No thumbnail for {Id}: {Error}", youTubeId, ex.Message);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
