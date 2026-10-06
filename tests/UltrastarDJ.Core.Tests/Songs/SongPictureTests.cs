using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongPictureTests
{
    private static Song S(string? cover = null, string? background = null, string? video = null, string? audio = null, string? youTube = null)
        => new() { Id = "x", SourceId = "a", Title = "T", Artist = "A", Bpm = 300, CoverPath = cover, BackgroundPath = background, VideoPath = video, AudioPath = audio, YouTubeId = youTube };

    [Fact]
    public void YouTubeVideo_CoverFirst_ThenThumbnail_ThenBackground()
    {
        Assert.Equal(
            [SongPicture.File("c.jpg"), SongPicture.YouTubeThumbnail("dQw4w9WgXcQ"), SongPicture.File("b.jpg")],
            SongPicture.Candidates(S(cover: "c.jpg", background: "b.jpg", youTube: "dQw4w9WgXcQ")));
    }

    [Fact]
    public void UsdbSong_NoLocalFiles_Thumbnail()
    {
        Assert.Equal([SongPicture.YouTubeThumbnail("abcdefghijk")], SongPicture.Candidates(S(youTube: "abcdefghijk")));
    }

    [Fact]
    public void Backdrop_BackgroundFirst_ThenCover_NoThumbnail()
    {
        Assert.Equal([SongPicture.File("b.jpg"), SongPicture.File("c.jpg")],
            SongPicture.BackdropCandidates(S(cover: "c.jpg", background: "b.jpg", youTube: "abcdefghijk")));
    }

    [Fact]
    public void LocalVideo_CoverThenBackground_NoThumbnail()
    {
        // The local video wins over a YouTube link, so its own first frame (not a thumbnail) is the fallback picture.
        Assert.Equal([SongPicture.File("c.jpg"), SongPicture.File("b.jpg")],
            SongPicture.Candidates(S(cover: "c.jpg", background: "b.jpg", video: "v.mp4", youTube: "abcdefghijk")));
    }

    [Fact]
    public void AudioOnly_CoverThenBackground()
    {
        Assert.Equal([SongPicture.File("b.jpg")], SongPicture.Candidates(S(background: "b.jpg", audio: "a.mp3")));
    }

    [Fact]
    public void Nothing_IsEmpty()
    {
        Assert.Empty(SongPicture.Candidates(S(audio: "a.mp3")));
    }
}
