using System.Text.RegularExpressions;

namespace UltrastarDJ.Media;

/// <summary>
/// YouTube sometimes refuses a stream address yt-dlp just resolved (HTTP 403 on googlevideo.com); a fresh yt-dlp run
/// gets one that works. <see cref="MpvPlayer"/> retries such loads quietly before reporting an error. Only refusals
/// of the stream itself count — "video unavailable", age gates or a missing network are not helped by retrying.
/// </summary>
public static partial class StreamRetry
{
    /// <summary>Retries after the first attempt: three tries in all.</summary>
    public const int MaxRetries = 2;

    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether an mpv log line says the server refused the stream (403, or a 5xx server error). ffmpeg logs globally,
    /// so the 403 itself can land in another player's log: a YouTube stream this player could not open counts too.
    /// </summary>
    public static bool IsRejectedStream(string? prefix, string? text)
        => prefix is "ffmpeg" or "stream" && text is not null
            && (RejectedStatus().IsMatch(text) || (text.StartsWith("Failed to open", StringComparison.Ordinal) && text.Contains("googlevideo.com", StringComparison.Ordinal)));

    [GeneratedRegex(@"HTTP error (403|5\d\d)\b")]
    private static partial Regex RejectedStatus();
}
