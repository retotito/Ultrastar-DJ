using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Media.Tests;

public class OutputKindsTests
{
    [Theory]
    // Windows: what the device registry says (JBL Charge 5, ARZOPA monitor, laptop speakers, a Bluetooth headset's hands-free part).
    [InlineData("wasapi/{3ed2a6a1}", "BTHENUM", 1, OutputKind.Bluetooth)]
    [InlineData("wasapi/{2f0c29cf}", "BTHHFENUM", 5, OutputKind.Bluetooth)]
    [InlineData("wasapi/{045e1b6e}", "HDAUDIO", 9, OutputKind.Display)]
    [InlineData("wasapi/{db05aaee}", "HDAUDIO", 1, OutputKind.Other)]
    [InlineData("wasapi/{6eeaff21}", "USB", 1, OutputKind.Other)]
    // macOS: a Bluetooth device's uid is its address.
    [InlineData("coreaudio/F8-4E-17-5A-0B-21:output", null, null, OutputKind.Bluetooth)]
    [InlineData("coreaudio/BuiltInSpeakerDevice", null, null, OutputKind.Other)]
    // Linux: PulseAudio / PipeWire sink names.
    [InlineData("pulse/bluez_output.F8_4E_17_5A_0B_21.1", null, null, OutputKind.Bluetooth)]
    [InlineData("pipewire/alsa_output.pci-0000_00_1f.3.hdmi-stereo", null, null, OutputKind.Display)]
    [InlineData("pulse/alsa_output.pci-0000_00_1f.3.analog-stereo", null, null, OutputKind.Other)]
    [InlineData("auto", null, null, OutputKind.Other)]
    public void Classify(string mpvId, string? enumerator, int? formFactor, OutputKind expected)
        => Assert.Equal(expected, OutputKinds.Classify(mpvId, enumerator, formFactor));
}
