using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class PlaybackErrorTests
{
    [Theory]
    [InlineData("ERROR: [youtube] OPXUeeFXc90: Sign in to confirm your age. Use --cookies-from-browser", "age-restricted")]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you’re not a bot. Use --cookies", "blocking")]
    [InlineData("ERROR: [youtube] abc: Video unavailable. This video has been removed by the uploader", "no longer available")]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access", "private")]
    [InlineData("ERROR: [youtube] abc: The uploader has not made this video available in your country", "your country")]
    [InlineData("ERROR: [youtube] abc: Unable to download webpage: <urlopen error [Errno 8] nodename nor servname provided>", "internet")]
    [InlineData("ERROR: [youtube] abc: Requested format is not available. Use --list-formats", "yt-dlp")]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden", "yt-dlp")]
    [InlineData("youtube-dl failed: not found or not enough permissions", "yt-dlp is missing")]
    public void Explain_KnownYouTubeErrors_GivesPlainReason(string raw, string expectedPart)
    {
        PlaybackError e = PlaybackError.Explain(raw);

        Assert.Contains(expectedPart, e.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(raw, e.Details);
    }

    [Fact]
    public void Explain_Unknown_KeepsRawAsDetails()
    {
        PlaybackError e = PlaybackError.Explain("unrecognized file format");

        Assert.Equal("The media could not be played.", e.Reason);
        Assert.Equal("unrecognized file format", e.Details);
    }

    [Fact]
    public void Explain_DetailsAreVerbatim()
    {
        // Details stay verbatim so a bug report carries the exact message.
        Assert.StartsWith("ERROR:", PlaybackError.Explain("ERROR: [youtube] x: Sign in to confirm your age").Details);
    }
}
