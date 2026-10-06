using UltrastarDJ.Core.Playback;
using static UltrastarDJ.Core.Playback.PlaybackState;

namespace UltrastarDJ.Core.Tests.Playback;

public class PlaybackRulesTests
{
    [Theory]
    [InlineData(Preview, true)]
    [InlineData(Score, true)]
    [InlineData(Loaded, false)]   // already the home view
    [InlineData(Idle, false)]
    [InlineData(Countdown, false)] // a running song is stopped with Stop, never by Home
    [InlineData(Playing, false)]
    [InlineData(Paused, false)]
    public void CanHome(PlaybackState state, bool expected) => Assert.Equal(expected, PlaybackRules.CanHome(state));

    [Theory]
    [InlineData(Loaded, true)]
    [InlineData(Preview, true)]
    [InlineData(Score, true)]      // replay the same song after Stop
    [InlineData(Idle, false)]
    [InlineData(Countdown, false)]
    [InlineData(Playing, false)]
    [InlineData(Paused, false)]
    public void CanPlay_WithBeamer(PlaybackState state, bool expected) => Assert.Equal(expected, PlaybackRules.CanPlay(state, anyDisplayOpen: true));

    [Fact]
    public void CanPlay_NoBeamer_IsFalse() => Assert.False(PlaybackRules.CanPlay(Loaded, anyDisplayOpen: false));

    [Theory]
    [InlineData(Loaded, true)]
    [InlineData(Score, true)]
    [InlineData(Preview, false)]
    [InlineData(Playing, false)]
    public void CanGetReady(PlaybackState state, bool expected) => Assert.Equal(expected, PlaybackRules.CanGetReady(state, anyDisplayOpen: true));

    [Theory]
    [InlineData(Countdown, true)]
    [InlineData(Playing, true)]
    [InlineData(Paused, true)]
    [InlineData(Score, false)]
    [InlineData(Loaded, false)]
    public void CanStop(PlaybackState state, bool expected) => Assert.Equal(expected, PlaybackRules.CanStop(state));

    [Theory]
    [InlineData(Loaded, PlayButton.Play)]
    [InlineData(Preview, PlayButton.Play)]
    [InlineData(Score, PlayButton.Play)]
    [InlineData(Countdown, PlayButton.Play)] // not pause yet: the song has not started
    [InlineData(Playing, PlayButton.Pause)]
    [InlineData(Paused, PlayButton.Resume)]
    [InlineData(Idle, PlayButton.Play)]
    public void PlayButtonFor(PlaybackState state, PlayButton expected) => Assert.Equal(expected, PlaybackRules.PlayButtonFor(state));

    [Theory]
    [InlineData(Countdown, false)]
    [InlineData(Idle, false)]
    [InlineData(Playing, true)]
    [InlineData(Paused, true)]
    [InlineData(Loaded, true)]
    public void PlayButtonEnabled(PlaybackState state, bool expected) => Assert.Equal(expected, PlaybackRules.PlayButtonEnabled(state, anyDisplayOpen: true));
}
