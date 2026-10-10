using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Core.Tests.Playback;

public class OutputPresenceTests
{
    private static readonly HashSet<string> Present = ["coreaudio/speakers", "coreaudio/lg-tv"];

    [Fact]
    public void ChosenOutputConnected_IsUsed() => Assert.Equal("coreaudio/lg-tv", OutputPresence.Effective("coreaudio/lg-tv", Present));

    [Fact]
    public void ChosenOutputGone_PlaysOnTheSystemDefault()
        // The display with speakers was unplugged: play on, on the default — the choice itself is kept elsewhere.
        => Assert.Equal(OutputPresence.SystemDefault, OutputPresence.Effective("coreaudio/beamer", Present));

    [Fact]
    public void SystemDefault_IsAlwaysThere() => Assert.Equal(OutputPresence.SystemDefault, OutputPresence.Effective(OutputPresence.SystemDefault, new HashSet<string>()));

    [Fact]
    public void IsMissing_OnlyForARealDeviceThatIsGone()
    {
        Assert.True(OutputPresence.IsMissing("coreaudio/beamer", Present));
        Assert.False(OutputPresence.IsMissing("coreaudio/lg-tv", Present));
        Assert.False(OutputPresence.IsMissing(OutputPresence.SystemDefault, Present));
    }
}
