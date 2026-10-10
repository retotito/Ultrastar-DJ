using UltrastarDJ.Core.Game;

namespace UltrastarDJ.Core.Tests.Game;

public class PodiumTests
{
    [Fact]
    public void FourPlayers_GoldSilverBronze_FourthNothing()
        => Assert.Equal([2, 0, 1, 3], Podium.Places([7000, 1200, 9100, 5000]));

    [Fact]
    public void Ties_ShareAPlace_AndTheNextSkips()
        => Assert.Equal([1, 1, 3, 0], Podium.Places([8000, 8000, 6000, 2000]));

    [Fact]
    public void NoPoints_NoTrophy()
        => Assert.Equal([1, 0, 0], Podium.Places([4000, 0, 0]));

    [Fact]
    public void OnePlayer_Gold()
        => Assert.Equal([1], Podium.Places([3000]));

    [Fact]
    public void RevealOrder_BronzeFirst_GoldLast()
        => Assert.Equal([3, 2, 1], Podium.RevealOrder([1, 2, 3, 0]));

    [Fact]
    public void RevealOrder_OnlyPlacesThatExist()
        => Assert.Equal([3, 1], Podium.RevealOrder([1, 1, 3]));
}
