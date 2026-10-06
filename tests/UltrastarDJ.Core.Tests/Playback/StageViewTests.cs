using UltrastarDJ.Core.Playback;
using static UltrastarDJ.Core.Playback.PlaybackState;

namespace UltrastarDJ.Core.Tests.Playback;

public class StageViewTests
{
    [Theory]
    [InlineData(Idle, false, StageLayer.None)]
    [InlineData(Loaded, true, StageLayer.None)]          // start view
    [InlineData(Preview, true, StageLayer.BlurredPicture)]
    [InlineData(Preview, false, StageLayer.BlurredPicture)]
    [InlineData(Countdown, true, StageLayer.Picture)]    // video only starts at "go"
    [InlineData(Playing, true, StageLayer.Video)]        // cases 2, 3, 4, 6
    [InlineData(Paused, true, StageLayer.Video)]
    [InlineData(Playing, false, StageLayer.Backdrop)]    // cases 1, 5: background / cover, sharp and dimmed
    [InlineData(Score, true, StageLayer.BlurredPicture)]
    public void Beamer(PlaybackState state, bool hasVideo, StageLayer expected)
        => Assert.Equal(expected, StageView.Beamer(state, hasVideo));

    [Theory]
    [InlineData(Idle, false, StageLayer.None)]
    [InlineData(Loaded, true, StageLayer.Picture)]
    [InlineData(Preview, true, StageLayer.Picture)]
    [InlineData(Countdown, true, StageLayer.Picture)]
    [InlineData(Playing, true, StageLayer.Video)]
    [InlineData(Playing, false, StageLayer.Backdrop)]
    [InlineData(Score, true, StageLayer.Picture)]
    public void GamePlayerBox(PlaybackState state, bool hasVideo, StageLayer expected)
        => Assert.Equal(expected, StageView.GamePlayerBox(state, hasVideo));

    [Fact]
    public void GamePlayerBox_WhilePlaying_MatchesBeamer()
    {
        foreach (bool video in new[] { true, false })
        {
            Assert.Equal(StageView.Beamer(Playing, video), StageView.GamePlayerBox(Playing, video));
        }
    }
}
