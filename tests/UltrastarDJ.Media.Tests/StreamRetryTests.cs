using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class StreamRetryTests
{
    [Theory]
    [InlineData("ffmpeg", "https: HTTP error 403 Forbidden", true)]      // the failure seen in the log
    [InlineData("ffmpeg", "https: HTTP error 503 Service Unavailable", true)]
    [InlineData("stream", "HTTP error 502 Bad Gateway", true)]
    [InlineData("ffmpeg", "https: HTTP error 404 Not Found", false)]
    [InlineData("ffmpeg", "tls: IO error: Connection reset by peer", false)] // ffmpeg reconnects by itself
    [InlineData("ytdl_hook", "ERROR: [youtube] abc: Video unavailable", false)]
    [InlineData("cplayer", "HTTP error 403 Forbidden", false)]
    [InlineData("ffmpeg", null, false)]
    public void IsRejectedStream_OnlyServerRefusals(string? prefix, string? text, bool expected)
    {
        Assert.Equal(expected, StreamRetry.IsRejectedStream(prefix, text));
    }
}
