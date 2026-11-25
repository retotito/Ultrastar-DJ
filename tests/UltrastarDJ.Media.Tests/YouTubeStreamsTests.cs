using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class YouTubeStreamsTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);
    private static readonly long ExpireIn6h = new DateTimeOffset(Now.AddHours(6)).ToUnixTimeSeconds();

    // The shape of yt-dlp -J (only the fields the app reads), with the kinds of formats a real lookup lists.
    private static string Json(long expire, bool withAvc1 = true, bool withSeparateAudio = true) => $$"""
        {
          "id": "abc",
          "formats": [
            { "format_id": "sb0", "protocol": "mhtml", "ext": "mhtml", "url": "https://i.ytimg.com/sb", "vcodec": "none", "acodec": "none", "height": 180 },
            { "format_id": "233", "protocol": "m3u8_native", "url": "https://manifest/233", "vcodec": "none", "acodec": "mp4a.40.5" },
            {{(withSeparateAudio ? $$"""
            { "format_id": "140", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.5, "tbr": 129.5,
              "http_headers": { "User-Agent": "UA/1.0 (X, like Y)", "Accept": "a,b" } },
            { "format_id": "251", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=251", "vcodec": "none", "acodec": "opus", "abr": 137.2, "tbr": 137.2 },
            """ : "")}}
            { "format_id": "18", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=18", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "height": 360, "tbr": 500 },
            {{(withAvc1 ? $$"""
            { "format_id": "134", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=134", "vcodec": "avc1.4D401E", "acodec": "none", "height": 360, "tbr": 202 },
            { "format_id": "136", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=136", "vcodec": "avc1.4D401F", "acodec": "none", "height": 720, "tbr": 1200 },
            { "format_id": "137", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=137", "vcodec": "avc1.640028", "acodec": "none", "height": 1080, "tbr": 2500 },
            """ : "")}}
            { "format_id": "247", "protocol": "https", "url": "https://rr1.googlevideo.com/videoplayback?expire={{expire}}&itag=247", "vcodec": "vp9", "acodec": "none", "height": 720, "tbr": 900 },
            { "format_id": "230", "protocol": "m3u8_native", "url": "https://manifest/230", "vcodec": "avc1.4D401F", "acodec": "none", "height": 720, "tbr": 1400 }
          ]
        }
        """;

    private static YouTubeInfo Info(bool withAvc1 = true, bool withSeparateAudio = true)
        => YouTubeStreams.Parse("abc", Json(ExpireIn6h, withAvc1, withSeparateAudio), Now);

    private static string Itag(YouTubeFormat? f) => f is null ? "-" : f.Url[(f.Url.IndexOf("itag=", StringComparison.Ordinal) + 5)..];

    [Fact]
    public void Parse_KeepsOnlyDirectStreams()
    {
        // Picture strips (mhtml) and HLS playlists (m3u8) are not what the player gets.
        Assert.Equal(["140", "251", "18", "134", "136", "137", "247"], Info().Formats.Select(Itag));
    }

    [Fact]
    public void Pick_VideoAndAudio_Avc1UpToTheHeight_PlusTheBestAudio()
    {
        // As the player's format rule: bestvideo[height<=720][vcodec^=avc1]+bestaudio.
        (YouTubeFormat? video, YouTubeFormat? audio) = YouTubeStreams.Pick(Info(), video: true, audio: true, maxHeight: 720)!.Value;

        Assert.Equal(("136", "251"), (Itag(video), Itag(audio)));
    }

    [Fact]
    public void Pick_NoAvc1_TakesAnotherCodec()
    {
        (YouTubeFormat? video, _) = YouTubeStreams.Pick(Info(withAvc1: false), video: true, audio: true, maxHeight: 720)!.Value;

        Assert.Equal("247", Itag(video));
    }

    [Fact]
    public void Pick_NoSeparateAudio_TakesAFormatWithBoth()
    {
        (YouTubeFormat? video, YouTubeFormat? audio) = YouTubeStreams.Pick(Info(withSeparateAudio: false), video: true, audio: true, maxHeight: 720)!.Value;

        Assert.Equal(("18", "-"), (Itag(video), Itag(audio)));
    }

    [Fact]
    public void Pick_AudioOnly_AndVideoOnly()
    {
        Assert.Equal("251", Itag(YouTubeStreams.Pick(Info(), video: false, audio: true, maxHeight: 720)!.Value.Audio));
        Assert.Equal(("136", "-"), Pick(video: true, audio: false));

        static (string, string) Pick(bool video, bool audio)
        {
            (YouTubeFormat? v, YouTubeFormat? a) = YouTubeStreams.Pick(Info(), video, audio, 720)!.Value;
            return (Itag(v), Itag(a));
        }
    }

    [Fact]
    public void Parse_KeptUntilShortlyBeforeTheAddressesExpire()
    {
        // Addresses work for ~6 h; the lookup is kept until 15 minutes before that.
        Assert.Equal(Now.AddHours(6).AddMinutes(-15), Info().ExpiresUtc);
    }

    [Fact]
    public void MpvUrl_TwoStreams_IsAnEdlWithByteLengths()
    {
        (YouTubeFormat? video, YouTubeFormat? audio) = YouTubeStreams.Pick(Info(), true, true, 720)!.Value;
        string url = YouTubeStreams.MpvUrl(video, audio);

        // Lengths in UTF-8 bytes, so commas and semicolons in the addresses cannot break the list.
        Assert.StartsWith("edl://!no_clip;!no_chapters;%" + Encoding.UTF8.GetByteCount(video!.Url) + "%" + video.Url + ";!new_stream;", url);
        Assert.EndsWith("%" + Encoding.UTF8.GetByteCount(audio!.Url) + "%" + audio.Url, url);
        Assert.Equal(video.Url, YouTubeStreams.MpvUrl(video, null));
    }

    [Fact]
    public void Headers_FromTheChosenFormat()
    {
        (_, YouTubeFormat? audio) = YouTubeStreams.Pick(Info(), false, true, 720)!.Value;
        YouTubeFormat m4a = Info().Formats.First(f => Itag(f) == "140");

        Assert.Equal("UA/1.0 (X, like Y)", m4a.Headers["User-Agent"]);
        Assert.Empty(audio!.Headers);
    }

    // ── The cache: one lookup per song, shared by preview, game and repeats ──

    private sealed class FakeYtDlp
    {
        public int Runs;
        public string Json = YouTubeStreamsTests.Json(ExpireIn6h);
        public bool Fail;
        public TaskCompletionSource? Gate;

        public async Task<(int, string, string)> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return Fail ? (1, "", "WARNING: x\nERROR: [youtube] abc: Sign in to confirm you're not a bot") : (0, Json, "");
        }
    }

    private static YouTubeResolver Resolver(FakeYtDlp yt, Func<DateTime> now)
        => new(() => new MpvOptions("yt-dlp"), NullLogger.Instance, yt.RunAsync, now);

    [Fact]
    public async Task SecondLookup_OfTheSameSong_IsServedFromTheCache()
    {
        FakeYtDlp yt = new();
        YouTubeResolver r = Resolver(yt, () => Now);

        await r.ResolveAsync("abc", CancellationToken.None);
        await r.ResolveAsync("abc", CancellationToken.None);

        Assert.Equal(1, yt.Runs);
    }

    [Fact]
    public async Task ExpiredOrForgotten_IsLookedUpAgain()
    {
        FakeYtDlp yt = new();
        DateTime now = Now;
        YouTubeResolver r = Resolver(yt, () => now);

        await r.ResolveAsync("abc", CancellationToken.None);
        now = Now.AddHours(6);
        await r.ResolveAsync("abc", CancellationToken.None);
        r.Forget("abc");   // YouTube refused a kept address (e.g. the router got a new IP)
        await r.ResolveAsync("abc", CancellationToken.None);

        Assert.Equal(3, yt.Runs);
    }

    [Fact]
    public async Task PreviewAndGameAtTheSameTime_ShareOneLookup()
    {
        FakeYtDlp yt = new() { Gate = new TaskCompletionSource() };
        YouTubeResolver r = Resolver(yt, () => Now);

        Task<YouTubeInfo> a = r.ResolveAsync("abc", CancellationToken.None);
        Task<YouTubeInfo> b = r.ResolveAsync("abc", CancellationToken.None);
        yt.Gate.SetResult();
        await Task.WhenAll(a, b);

        Assert.Equal(1, yt.Runs);
    }

    [Fact]
    public async Task FailedLookup_SaysWhy_AndIsNotKept()
    {
        FakeYtDlp yt = new() { Fail = true };
        YouTubeResolver r = Resolver(yt, () => Now);

        MediaException ex = await Assert.ThrowsAsync<MediaException>(() => r.ResolveAsync("abc", CancellationToken.None));
        Assert.StartsWith("ERROR: [youtube] abc: Sign in to confirm you're not a bot", ex.Message);
        await Assert.ThrowsAsync<MediaException>(() => r.ResolveAsync("abc", CancellationToken.None));
        Assert.Equal(2, yt.Runs);
    }

    [Fact]
    public async Task Arguments_AsThePlayerUsesThem()
    {
        IReadOnlyList<string>? seen = null;
        YouTubeResolver r = new(() => new MpvOptions("yt-dlp"), NullLogger.Instance,
            (args, _) => { seen = args; return Task.FromResult((0, Json(ExpireIn6h), "")); }, () => Now);

        await r.ResolveAsync("abc", CancellationToken.None);

        Assert.Equal(["-J", "--no-playlist", "--no-js-runtimes", "-f", "bv*+ba/b/ba/bv*", "https://www.youtube.com/watch?v=abc"], seen);
    }
}
