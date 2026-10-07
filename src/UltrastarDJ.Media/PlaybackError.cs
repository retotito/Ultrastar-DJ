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
/// <param name="YtDlpMayHelp">
/// What a newer yt-dlp usually fixes: YouTube refused the stream, a "not a bot" block, formats or pages yt-dlp could
/// not read. The app then points the DJ to Settings → YouTube.
/// </param>
public sealed record PlaybackError(string Reason, string Details, bool SongProblem = false, bool YtDlpMayHelp = false)
{
    private static readonly (string[] Patterns, string Reason, bool SongProblem, bool YtDlpMayHelp)[] Known =
    [
        (["confirm your age"], "This video is age-restricted on YouTube and cannot be played without a YouTube login.", true, false),
        (["not a bot"], "YouTube is blocking playback right now.", false, true),
        (["private video", "this video is private"], "This video is private on YouTube.", true, false),
        (["in your country", "geo restrict"], "This video is blocked in your country.", true, false),
        (["copyright"], "This video was blocked on YouTube for copyright reasons.", true, false),
        (["video unavailable", "this video is unavailable", "has been removed", "account associated with this video has been terminated"],
            "This video is no longer available on YouTube.", true, false),
        (["unable to download webpage", "nodename nor servname", "failed to resolve", "getaddrinfo", "network is unreachable", "timed out"],
            "No connection to YouTube — check the internet connection.", false, false),
        (["requested format is not available", "http error 403"], "YouTube refused the stream.", false, true),
        (["unable to extract", "nsig extraction failed", "signature extraction failed"], "yt-dlp could not read YouTube's page.", false, true),
        (["youtube-dl failed: not found"], "yt-dlp is missing — run scripts/fetch-natives.", false, false),
    ];

    public static PlaybackError Explain(string raw)
    {
        foreach ((string[] patterns, string reason, bool songProblem, bool ytDlp) in Known)
        {
            if (patterns.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return new PlaybackError(reason, raw, songProblem, ytDlp);
            }
        }

        return new PlaybackError("The media could not be played.", raw);
    }
}
