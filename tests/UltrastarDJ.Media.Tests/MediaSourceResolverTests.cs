using UltrastarDJ.Media;

namespace UltrastarDJ.Media.Tests;

public class MediaSourceResolverTests
{
    private const string Mp3 = "/songs/a/song.mp3";
    private const string Mp4 = "/songs/a/video.mp4";
    private const string Yt = "dQw4w9WgXcQ";

    [Fact]
    public void Resolve_Mp3AndBackgroundImage_IsCase1WithStaticImage()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { AudioPath = Mp3, BackgroundPath = "/bg.jpg" });

        Assert.Equal(1, plan.Case);
        Assert.Null(plan.Visual);
        Assert.False(plan.AudioRoleHasVideo);
        Assert.Equal(StaticBackground.Image, plan.Background);
        Assert.Equal(VideoGapMode.None, plan.GapMode);
    }

    [Fact]
    public void Resolve_Mp3AndLocalVideo_IsCase2WithMutedVisualOffsetByGap()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { AudioPath = Mp3, VideoPath = Mp4, VideoGapSec = 19.5 });

        Assert.Equal(2, plan.Case);
        Assert.False(plan.Audio.Options.Video);
        Assert.NotNull(plan.Visual);
        Assert.False(plan.Visual.Options.Audio);
        Assert.Equal(TimeSpan.FromSeconds(19.5), plan.Visual.Options.StartAt);
        Assert.Equal(VideoGapMode.OffsetVisual, plan.GapMode);
        Assert.Equal(0, plan.AudioOriginSec);
    }

    [Fact]
    public void Resolve_Mp3AndYouTube_IsCase3WithVideoOnlyYouTube()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { AudioPath = Mp3, YouTubeId = Yt });

        Assert.Equal(3, plan.Case);
        Assert.IsType<MediaSource.LocalFile>(plan.Audio.Source);
        Assert.IsType<MediaSource.YouTube>(plan.Visual!.Source);
        Assert.False(plan.Visual.Options.Audio);
    }

    [Fact]
    public void Resolve_YouTubeOnly_IsCase4SkippingIntroOnBothTracks()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { YouTubeId = Yt, VideoGapSec = 5, EndSec = 100 });

        Assert.Equal(4, plan.Case);
        Assert.Null(plan.Visual);
        Assert.True(plan.AudioRoleHasVideo);
        Assert.Equal(VideoGapMode.SkipIntro, plan.GapMode);
        Assert.Equal(TimeSpan.FromSeconds(5), plan.Audio.Options.StartAt);
        Assert.Equal(TimeSpan.FromSeconds(105), plan.Audio.Options.EndAt);
        Assert.Equal(5, plan.AudioOriginSec);
    }

    [Fact]
    public void Resolve_Mp3AndCoverOnly_IsCase5()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { AudioPath = Mp3, CoverPath = "/co.jpg" });

        Assert.Equal(5, plan.Case);
        Assert.Equal(StaticBackground.Cover, plan.Background);
    }

    [Fact]
    public void Resolve_LocalVideoOnly_IsCase6()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { VideoPath = Mp4, VideoGapSec = 2 });

        Assert.Equal(6, plan.Case);
        Assert.True(plan.AudioRoleHasVideo);
        Assert.Equal(VideoGapMode.SkipIntro, plan.GapMode);
        Assert.Equal(2, plan.AudioOriginSec);
    }

    [Fact]
    public void Resolve_Mp3TakesPriorityOverVideoAudio()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { AudioPath = Mp3, VideoPath = Mp4, YouTubeId = Yt });

        // #MP3 is always the audio; local video beats YouTube as the visual.
        Assert.Equal(2, plan.Case);
    }

    [Fact]
    public void Resolve_NoMedia_Throws()
    {
        Assert.Throws<MediaException>(() => MediaSourceResolver.Resolve(new SongMedia()));
    }

    [Fact]
    public void Resolve_NegativeVideoGap_IsClampedToZero()
    {
        MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia { VideoPath = Mp4, VideoGapSec = -3 });

        Assert.Equal(0, plan.VideoGapSec);
    }
}
