using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Media;

/// <summary>One stream yt-dlp found for a video: its address, what it carries, and the headers to fetch it with.</summary>
public sealed record YouTubeFormat(string Url, int? Height, string? VideoCodec, string? AudioCodec, double Bitrate, IReadOnlyDictionary<string, string> Headers)
{
    public bool HasVideo => VideoCodec is { Length: > 0 } and not "none";
    public bool HasAudio => AudioCodec is { Length: > 0 } and not "none";
}

/// <summary>A video's streams, kept until <see cref="ExpiresUtc"/> (shortly before YouTube's addresses stop working).</summary>
public sealed record YouTubeInfo(string VideoId, IReadOnlyList<YouTubeFormat> Formats, DateTime ExpiresUtc);

/// <summary>
/// Reading yt-dlp's answer and choosing from it — the same choice the player's format rule made when mpv ran yt-dlp
/// itself (<c>bestvideo[height&lt;=h][vcodec^=avc1]+bestaudio / bestvideo[height&lt;=h]+bestaudio / best[height&lt;=h]</c>).
/// </summary>
public static class YouTubeStreams
{
    // Kept addresses stop a little before YouTube's own expiry, so a song never starts on one about to die.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(15);
    // An answer without an expiry in its addresses: kept this long.
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    /// <summary>yt-dlp's <c>-J</c> output: the directly playable streams (no picture strips, no HLS playlists).</summary>
    public static YouTubeInfo Parse(string videoId, string json, DateTime nowUtc)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        List<YouTubeFormat> formats = [];
        long? expire = null;
        if (doc.RootElement.TryGetProperty("formats", out JsonElement list))
        {
            foreach (JsonElement f in list.EnumerateArray())
            {
                if (Str(f, "protocol") is not ("https" or "http") || Str(f, "url") is not { } url)
                {
                    continue;
                }

                YouTubeFormat format = new(url, Int(f, "height"), Str(f, "vcodec"), Str(f, "acodec"),
                    Num(f, "tbr") ?? Num(f, "abr") ?? 0, Headers(f));
                if (!format.HasVideo && !format.HasAudio)
                {
                    continue;
                }

                formats.Add(format);
                if (ExpireOf(url) is { } e)
                {
                    expire = expire is null ? e : Math.Min(expire.Value, e);
                }
            }
        }

        DateTime expires = expire is { } unix
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime - ExpiryMargin
            : nowUtc + DefaultLifetime;
        return new YouTubeInfo(videoId, formats, expires);
    }

    /// <summary>The streams for a load, or null when none fits (the "requested format is not available" case).</summary>
    public static (YouTubeFormat? Video, YouTubeFormat? Audio)? Pick(YouTubeInfo info, bool video, bool audio, int maxHeight)
    {
        IEnumerable<YouTubeFormat> fs = info.Formats;
        YouTubeFormat? bestAudio = fs.Where(f => f.HasAudio && !f.HasVideo).MaxBy(f => f.Bitrate);
        List<YouTubeFormat> videoOnly = [.. fs.Where(f => f.HasVideo && !f.HasAudio && (f.Height ?? 0) <= maxHeight)];
        YouTubeFormat? bestVideo = Best(videoOnly.Where(f => f.VideoCodec!.StartsWith("avc1", StringComparison.Ordinal))) ?? Best(videoOnly);

        if (!video)
        {
            return bestAudio is not null ? (null, bestAudio) : Combined() is { } c ? (c, null) : null;
        }

        if (!audio)
        {
            return bestVideo is not null ? (bestVideo, null) : Combined() is { } c ? (c, null) : null;
        }

        if (bestVideo is not null && bestAudio is not null)
        {
            return (bestVideo, bestAudio);
        }

        return Combined() is { } both ? (both, null) : null;

        YouTubeFormat? Combined() => Best(fs.Where(f => f.HasVideo && f.HasAudio && (f.Height ?? 0) <= maxHeight));
    }

    /// <summary>
    /// What mpv opens: one address, or video and audio joined in an EDL — each address prefixed with its length in
    /// UTF-8 bytes, so the commas and semicolons in YouTube's addresses cannot break the list.
    /// </summary>
    public static string MpvUrl(YouTubeFormat? video, YouTubeFormat? audio)
    {
        if (video is null || audio is null)
        {
            return (video ?? audio)!.Url;
        }

        return $"edl://!no_clip;!no_chapters;{Escaped(video.Url)};!new_stream;!no_clip;!no_chapters;{Escaped(audio.Url)}";

        static string Escaped(string url) => $"%{Encoding.UTF8.GetByteCount(url)}%{url}";
    }

    private static YouTubeFormat? Best(IEnumerable<YouTubeFormat> fs) => fs.OrderByDescending(f => f.Height ?? 0).ThenByDescending(f => f.Bitrate).FirstOrDefault();

    private static long? ExpireOf(string url)
    {
        int i = url.IndexOf("expire=", StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        int start = i + "expire=".Length;
        int end = start;
        while (end < url.Length && char.IsAsciiDigit(url[end]))
        {
            end++;
        }

        return long.TryParse(url.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out long v) ? v : null;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    private static double? Num(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static Dictionary<string, string> Headers(JsonElement f)
    {
        Dictionary<string, string> h = new(StringComparer.OrdinalIgnoreCase);
        if (f.TryGetProperty("http_headers", out JsonElement hs) && hs.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in hs.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    h[p.Name] = p.Value.GetString()!;
                }
            }
        }

        return h;
    }
}

/// <summary>
/// Looks a YouTube video up with yt-dlp once and keeps the answer (docs/02-media-engine.md "YouTube lookups"): preview,
/// game and a song sung again all play from the same lookup while its addresses are valid. YouTube counts these
/// lookups — after too many it pauses ("confirm you're not a bot") — so a party costs about one per song instead of
/// two or three. A lookup is shared by everyone waiting for it and runs to its end even if one of them gives up; a
/// failed one is not kept. Thread-safe.
/// </summary>
public sealed class YouTubeResolver
{
    /// <summary>How long yt-dlp may take before the lookup gives up (the load itself gives up later, MpvPlayer).</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(45);

    private readonly Func<MpvOptions> _options;
    private readonly ILogger _log;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<(int Exit, string Out, string Err)>> _run;
    private readonly Func<DateTime> _now;
    private readonly ConcurrentDictionary<string, Task<YouTubeInfo>> _lookups = new();

    /// <param name="options">yt-dlp's path, read for every lookup (an update replaces it).</param>
    /// <param name="log">Lookups and their failures.</param>
    /// <param name="run">Test seam: runs yt-dlp with the arguments, returns exit code, stdout and stderr.</param>
    /// <param name="now">Test seam: the clock (UTC).</param>
    public YouTubeResolver(Func<MpvOptions> options, ILogger log,
        Func<IReadOnlyList<string>, CancellationToken, Task<(int Exit, string Out, string Err)>>? run = null, Func<DateTime>? now = null)
    {
        _options = options;
        _log = log;
        _run = run ?? ((args, ct) => RunYtDlpAsync(_options().YtDlpPath!, args, ct));
        _now = now ?? (() => DateTime.UtcNow);
    }

    public bool IsAvailable => !string.IsNullOrEmpty(_options().YtDlpPath);

    public async Task<YouTubeInfo> ResolveAsync(string videoId, CancellationToken ct)
    {
        string key = videoId;
        while (true)
        {
            bool fresh = false;
            Task<YouTubeInfo> lookup = _lookups.GetOrAdd(key, _ =>
            {
                fresh = true;
                return LookupAsync(videoId);
            });
            YouTubeInfo info;
            try
            {
                info = await lookup.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (MediaException)
            {
                _lookups.TryRemove(new KeyValuePair<string, Task<YouTubeInfo>>(key, lookup));
                throw;
            }

            // A fresh answer is used as it is (even if the computer's clock calls it expired — never loop).
            if (fresh || info.ExpiresUtc > _now())
            {
                return info;
            }

            // Kept too long: look it up again.
            _lookups.TryRemove(new KeyValuePair<string, Task<YouTubeInfo>>(key, lookup));
        }
    }

    /// <summary>YouTube refused a kept address (expired early, or the internet address changed): the next load looks it up again.</summary>
    public void Forget(string videoId)
    {
        _lookups.TryRemove(videoId, out _);
    }

    private async Task<YouTubeInfo> LookupAsync(string videoId)
    {
        // -f: any choice that exists, so yt-dlp lists the streams instead of failing — without ffmpeg it would insist on
        // one file with video and audio together, which many videos lack. The app picks the streams itself (Pick).
        // --no-js-runtimes: as the player (MpvPlayer) — a JavaScript runtime found on the computer (Deno) made YouTube
        // refuse every stream.
        List<string> args = ["-J", "--no-playlist", "--no-js-runtimes", "-f", "bv*+ba/b/ba/bv*"];
        args.Add($"https://www.youtube.com/watch?v={videoId}");
        Stopwatch sw = Stopwatch.StartNew();
        using CancellationTokenSource timeout = new(LookupTimeout);
        (int exit, string stdout, string stderr) = await _run(args, timeout.Token).ConfigureAwait(false);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            // yt-dlp's own ERROR line first (PlaybackError explains it), then the rest for the details.
            string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string error = lines.FirstOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal)) ?? (lines.LastOrDefault() ?? $"yt-dlp exited with {exit}");
            _log.LogWarning("YouTube lookup {Video} failed after {Ms} ms: {Error}", videoId, sw.ElapsedMilliseconds, error);
            throw new MediaException(string.Join('\n', [error, .. lines.Where(l => l != error).TakeLast(15)]));
        }

        YouTubeInfo info = YouTubeStreams.Parse(videoId, stdout, _now());
        _log.LogInformation("YouTube lookup {Video}: {Count} streams in {Ms} ms, kept until {Until:HH:mm}", videoId, info.Formats.Count, sw.ElapsedMilliseconds, info.ExpiresUtc.ToLocalTime());
        return info;
    }

    private static async Task<(int, string, string)> RunYtDlpAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        ProcessStartInfo psi = new(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using Process p = Process.Start(psi) ?? throw new MediaException("youtube-dl failed: not found or not enough permissions");
        Task<string> stdout = p.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new MediaException(MpvPlayer.TooSlowMessage);
        }

        return (p.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
