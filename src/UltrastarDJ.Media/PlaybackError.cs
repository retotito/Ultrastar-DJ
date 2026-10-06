namespace UltrastarDJ.Media;

/// <summary>
/// A playback failure in words a DJ understands. mpv only says "unrecognized file format" when yt-dlp fails;
/// <see cref="MpvPlayer"/> passes yt-dlp's own error line instead, which is matched here.
/// </summary>
/// <param name="Reason">One plain sentence for the error dialog.</param>
/// <param name="Details">The raw message, verbatim, for "Show details" and bug reports.</param>
/// <param name="SongProblem">
/// The video itself is the problem (removed, private, age-restricted, blocked) — it fails again tomorrow, so the
/// library marks the song. Connection trouble, YouTube refusing streams or a missing yt-dlp are not.
/// </param>
public sealed record PlaybackError(string Reason, string Details, bool SongProblem = false)
{
    private static readonly (string[] Patterns, string Reason, bool SongProblem)[] Known =
    [
        (["confirm your age"], "This video is age-restricted on YouTube and cannot be played without a YouTube login.", true),
        (["not a bot"], "YouTube is blocking playback right now. Try again later, or update yt-dlp.", false),
        (["private video", "this video is private"], "This video is private on YouTube.", true),
        (["in your country", "geo restrict"], "This video is blocked in your country.", true),
        (["copyright"], "This video was blocked on YouTube for copyright reasons.", true),
        (["video unavailable", "this video is unavailable", "has been removed", "account associated with this video has been terminated"],
            "This video is no longer available on YouTube.", true),
        (["unable to download webpage", "nodename nor servname", "failed to resolve", "getaddrinfo", "network is unreachable", "timed out"],
            "No connection to YouTube — check the internet connection.", false),
        (["requested format is not available", "http error 403"], "YouTube refused the stream. Updating yt-dlp usually fixes this.", false),
        (["youtube-dl failed: not found"], "yt-dlp is missing — run scripts/fetch-natives.", false),
    ];

    public static PlaybackError Explain(string raw)
    {
        foreach ((string[] patterns, string reason, bool songProblem) in Known)
        {
            if (patterns.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return new PlaybackError(reason, raw, songProblem);
            }
        }

        return new PlaybackError("The media could not be played.", raw);
    }
}
