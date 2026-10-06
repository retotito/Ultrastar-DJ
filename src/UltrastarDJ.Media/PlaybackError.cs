namespace UltrastarDJ.Media;

/// <summary>
/// A playback failure in words a DJ understands. mpv only says "unrecognized file format" when yt-dlp fails;
/// <see cref="MpvPlayer"/> passes yt-dlp's own error line instead, which is matched here.
/// </summary>
/// <param name="Reason">One plain sentence for the error dialog.</param>
/// <param name="Details">The raw message, verbatim, for "Show details" and bug reports.</param>
public sealed record PlaybackError(string Reason, string Details)
{
    private static readonly (string[] Patterns, string Reason)[] Known =
    [
        (["confirm your age"], "This video is age-restricted on YouTube and cannot be played without a YouTube login."),
        (["not a bot"], "YouTube is blocking playback right now. Try again later, or update yt-dlp."),
        (["private video", "this video is private"], "This video is private on YouTube."),
        (["in your country", "geo restrict"], "This video is blocked in your country."),
        (["copyright"], "This video was blocked on YouTube for copyright reasons."),
        (["video unavailable", "this video is unavailable", "has been removed", "account associated with this video has been terminated"],
            "This video is no longer available on YouTube."),
        (["unable to download webpage", "nodename nor servname", "failed to resolve", "getaddrinfo", "network is unreachable", "timed out"],
            "No connection to YouTube — check the internet connection."),
        (["requested format is not available", "http error 403"], "YouTube refused the stream. Updating yt-dlp usually fixes this."),
        (["youtube-dl failed: not found"], "yt-dlp is missing — run scripts/fetch-natives."),
    ];

    public static PlaybackError Explain(string raw)
    {
        foreach ((string[] patterns, string reason) in Known)
        {
            if (patterns.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return new PlaybackError(reason, raw);
            }
        }

        return new PlaybackError("The media could not be played.", raw);
    }
}
