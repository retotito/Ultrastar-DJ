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
    // Linux: the sound server's sinks are the native route; JACK's default is a server nobody runs.
    [InlineData("pulse/alsa_output.pci-0000_00_1f.3.analog-stereo", false)]
    [InlineData("pipewire/bluez_output.F8_4E_17_5A_0B_21.1", false)]
    [InlineData("jack", true)]
    [InlineData("auto", false)]
    public void SecondRoutesAreLeftOut(string id, bool leftOut) => Assert.Equal(leftOut, MpvPlayer.IsSecondRoute(id));

    // mpv's list on Linux Mint 22 (PipeWire with its PulseAudio server), shortened: each speaker through PipeWire,
    // PulseAudio and a dozen ALSA names.
    private static readonly string[] LinuxMint22 =
    [
        "auto", "pipewire",
        "pipewire/alsa_output.pci-0000_00_1b.0.analog-stereo",
        "pipewire/alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo",
        "pulse/alsa_output.pci-0000_00_1b.0.analog-stereo",
        "pulse/alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo",
        "alsa", "alsa/pipewire", "alsa/plughw:CARD=PCH,DEV=0", "alsa/sysdefault:CARD=PCH", "alsa/front:CARD=PCH,DEV=0",
        "alsa/surround51:CARD=PCH,DEV=0", "alsa/dmix:CARD=PCH,DEV=0", "alsa/hdmi:CARD=HDMI,DEV=0",
        "alsa/sysdefault:CARD=H340", "alsa/iec958:CARD=H340,DEV=0",
        "jack", "sdl",
    ];

    [Fact]
    public void Linux_EachSpeakerOnce_ThroughPulseAudio()
        => Assert.Equal(
            ["auto", "pulse/alsa_output.pci-0000_00_1b.0.analog-stereo", "pulse/alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo"],
            MpvPlayer.Offered(LinuxMint22));

    [Fact]
    public void Linux_WithoutPulseAudio_PipeWire()
        => Assert.Equal(
            ["auto", "pipewire/alsa_output.pci-0000_00_1b.0.analog-stereo", "pipewire/alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo"],
            MpvPlayer.Offered(LinuxMint22.Where(id => !id.StartsWith("pulse/", StringComparison.Ordinal))));

    [Fact]
    public void Linux_AlsaOnly_EachCardsDefaultAndHdmi()
        => Assert.Equal(
            ["auto", "alsa/sysdefault:CARD=PCH", "alsa/hdmi:CARD=HDMI,DEV=0", "alsa/sysdefault:CARD=H340"],
            MpvPlayer.Offered(LinuxMint22.Where(id => !id.StartsWith("pulse/", StringComparison.Ordinal) && !id.StartsWith("pipewire/", StringComparison.Ordinal))));

    [Fact]
    public void WindowsAndMac_Unchanged()
        => Assert.Equal(
            ["auto", "wasapi/{3ed2a6a1}", "coreaudio/BuiltInSpeakerDevice"],
            MpvPlayer.Offered(["auto", "wasapi/{3ed2a6a1}", "openal", "coreaudio/BuiltInSpeakerDevice", "avfoundation/BuiltInSpeakerDevice"]));

    [Theory]
    [InlineData("pulse/alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo", "alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo")]
    [InlineData("pipewire/bluez_output.F8_4E_17_5A_0B_21.1", "bluez_output.F8_4E_17_5A_0B_21.1")]
    [InlineData("alsa/sysdefault:CARD=H340", null)]
    [InlineData("pulse", null)]
    [InlineData("wasapi/{3ed2a6a1}", null)]
    [InlineData("auto", null)]
    public void SoundServerSink(string mpvId, string? sink) => Assert.Equal(sink, MpvPlayer.SoundServerSink(mpvId));
}
