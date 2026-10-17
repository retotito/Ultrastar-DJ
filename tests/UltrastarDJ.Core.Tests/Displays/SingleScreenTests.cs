using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.Core.Tests.Displays;

public class SingleScreenTests
{
    [Theory]
    [InlineData(SingleScreenMode.Automatic, 1, true)]
    [InlineData(SingleScreenMode.Automatic, 2, false)]
    [InlineData(SingleScreenMode.On, 3, true)]
    [InlineData(SingleScreenMode.Off, 1, false)]
    public void IsActive_FollowsModeAndScreens(SingleScreenMode mode, int screens, bool expected)
        => Assert.Equal(expected, SingleScreen.IsActive(mode, screens));

    [Fact]
    public void PlayersOn_SingleScreen_DisplayOneShowsEveryone_DisplayTwoNone()
    {
        DisplayConfig first = new(DisplayId.Beamer1, [2, 1]);
        DisplayConfig second = new(DisplayId.Beamer2, [4, 3]);

        Assert.Equal([1, 2, 3, 4], SingleScreen.PlayersOn(DisplayId.Beamer1, first, second, oneScreen: true));
        Assert.Empty(SingleScreen.PlayersOn(DisplayId.Beamer2, first, second, oneScreen: true));
    }

    [Fact]
    public void PlayersOn_TwoScreens_AsAssigned()
    {
        DisplayConfig first = new(DisplayId.Beamer1, [1, 2]);
        DisplayConfig second = new(DisplayId.Beamer2, [3]);

        Assert.Equal([1, 2], SingleScreen.PlayersOn(DisplayId.Beamer1, first, second, oneScreen: false));
        Assert.Equal([3], SingleScreen.PlayersOn(DisplayId.Beamer2, first, second, oneScreen: false));
    }
}
