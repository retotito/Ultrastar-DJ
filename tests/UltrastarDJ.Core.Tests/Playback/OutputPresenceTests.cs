using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Core.Tests.Playback;

public class OutputPresenceTests
{
    private static readonly HashSet<string> Present = ["coreaudio/speakers", "coreaudio/lg-tv"];

    [Fact]
    public void IsMissing_OnlyForARealDeviceThatIsGone()
    {
        Assert.True(OutputPresence.IsMissing("coreaudio/beamer", Present));
        Assert.False(OutputPresence.IsMissing("coreaudio/lg-tv", Present));
        Assert.False(OutputPresence.IsMissing(OutputPresence.SystemDefault, Present));
    }

    [Fact]
    public void SystemDefault_IsNeverMissing() => Assert.False(OutputPresence.IsMissing(OutputPresence.SystemDefault, new HashSet<string>()));

    [Fact]
    public void Replacement_IsTheSpeakerTheSystemPlaysOnNow()
    {
        // The Bluetooth speaker left; the OS switched to the laptop speakers. Pinned to those, so the speaker
        // coming back (and the OS making it its default again) does not take the game with it.
        Assert.Equal("coreaudio/speakers", OutputPresence.Replacement("coreaudio/jbl", Present, ["coreaudio/speakers"]));
    }

    [Fact]
    public void Replacement_SkipsCandidatesThatAreGoneOrTheLostOne()
    {
        // The OS still names the lost device (not switched yet), the next source names one that is unplugged.
        Assert.Equal("coreaudio/lg-tv", OutputPresence.Replacement("coreaudio/jbl", Present, ["coreaudio/jbl", "coreaudio/beamer", null, "coreaudio/lg-tv"]));
    }

    [Fact]
    public void Replacement_NoneKnown_SystemDefault()
        => Assert.Equal(OutputPresence.SystemDefault, OutputPresence.Replacement("coreaudio/jbl", Present, [null, null]));
}
