using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Core.Tests.Players;

public class MonitorMuteTests
{
    private static PlayerConfig Player(bool mixMuted) => new() { Id = 1, Color = "blue", MixMuted = mixMuted };

    [Fact]
    public void MutedInTheGamePlayer_IsSilentDuringASong() => Assert.True(Player(mixMuted: true).MutedInMonitor(duringSong: true));

    [Fact]
    public void MutedInTheGamePlayer_IsStillHeardInTheMicTest()
        // Regression: the Game Player's mute also silenced the Audio Input test — the DJ could not hear the mic being set up.
        => Assert.False(Player(mixMuted: true).MutedInMonitor(duringSong: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotMuted_IsAlwaysHeard(bool duringSong) => Assert.False(Player(mixMuted: false).MutedInMonitor(duringSong));
}
