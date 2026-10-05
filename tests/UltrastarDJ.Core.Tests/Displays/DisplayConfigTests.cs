using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.Core.Tests.Displays;

public sealed class DisplayConfigTests
{
    [Fact]
    public void Default_HasNoPlayers()
    {
        DisplayConfig config = DisplayConfig.Default(DisplayId.Beamer2);

        Assert.Equal(DisplayId.Beamer2, config.Id);
        Assert.Empty(config.PlayerIds);
    }
}
