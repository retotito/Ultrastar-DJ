using UltrastarDJ.Audio.PortAudio;

namespace UltrastarDJ.Audio.Tests;

/// <summary>
/// Linux: PortAudio lists ALSA's devices only, where PipeWire / PulseAudio is one device on the default speaker. A sink
/// is reached through ALSA's plugin with the sink as argument.
/// </summary>
public class SoundServerSinkTests
{
    private const string Headset = "alsa_output.usb-Logitech_Inc._Logi_USB_Headset_H340-00.analog-stereo";

    [Fact]
    public void PipeWirePluginFirst()
        => Assert.Equal($"pipewire:NODE={Headset}", PortAudioBackend.SinkPcm(Headset, ["HDA Intel PCH: ALC282 Analog (hw:0,0)", "sysdefault", "pulse", "pipewire", "default"]));

    [Fact]
    public void PulseAudioPluginOtherwise()
        => Assert.Equal($"pulse:DEVICE={Headset}", PortAudioBackend.SinkPcm(Headset, ["sysdefault", "pulse", "default"]));

    [Fact]
    public void NoPlugin_Null() => Assert.Null(PortAudioBackend.SinkPcm(Headset, ["HDA Intel PCH: ALC282 Analog (hw:0,0)", "default"]));

    [Theory]
    // ALSA reads "," as the next argument and quotes as string delimiters: such a name would open something else.
    [InlineData("odd,sink")]
    [InlineData("odd\"sink")]
    [InlineData("odd sink")]
    [InlineData("")]
    public void NamesAlsaCannotCarry_Null(string sink) => Assert.Null(PortAudioBackend.SinkPcm(sink, ["pipewire"]));

    [Theory]
    [InlineData($"pipewire:NODE={Headset}", true)]
    [InlineData($"pulse:DEVICE={Headset}", true)]
    [InlineData("pipewire", false)]
    [InlineData("Logi USB Headset H340: Audio (hw:2,0)", false)]
    public void IsSinkPcm(string id, bool expected) => Assert.Equal(expected, PortAudioBackend.IsSinkPcm(id));
}
