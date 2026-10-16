namespace UltrastarDJ.Media.Tests;

public class AudioDeviceListTests
{
    [Theory]
    // Windows: mpv's list ends with OpenAL's default — the same speaker a second time.
    [InlineData("openal", true)]
    [InlineData("wasapi/{3ed2a6a1-fccb-48e0-98c6-82b9bf872b2a}", false)]
    // macOS: every CoreAudio device again through AVFoundation.
    [InlineData("avfoundation/BuiltInSpeakerDevice", true)]
    [InlineData("coreaudio/BuiltInSpeakerDevice", false)]
    // Linux: the sound server's sinks are the native route.
    [InlineData("pulse/alsa_output.pci-0000_00_1f.3.analog-stereo", false)]
    [InlineData("pipewire/bluez_output.F8_4E_17_5A_0B_21.1", false)]
    [InlineData("auto", false)]
    public void SecondRoutesAreLeftOut(string id, bool leftOut) => Assert.Equal(leftOut, MpvPlayer.IsSecondRoute(id));
}
