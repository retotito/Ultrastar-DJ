using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Audio.PortAudio;
using Xunit.Abstractions;

namespace UltrastarDJ.Audio.Tests;

/// <summary>
/// Runs against this machine's real devices, on Windows only: the host API filter it checks exists there, and CI's
/// sound-card-less Linux cannot even start PortAudio. Without devices it passes trivially.
/// </summary>
public class PortAudioBackendTests(ITestOutputHelper output)
{
    [Fact]
    public void Devices_AreListedOnce_WithOneDefaultPerDirection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using PortAudioBackend backend = new(NullLogger<PortAudioBackend>.Instance);
        foreach (AudioDeviceInfo d in backend.Devices)
        {
            output.WriteLine($"{d.Id} (in {d.MaxInputChannels}, out {d.MaxOutputChannels}, {d.DefaultSampleRateHz} Hz{(d.IsDefaultInput ? ", default in" : "")}{(d.IsDefaultOutput ? ", default out" : "")})");
        }

        // Windows lists each device once per host API (MME, DirectSound, WASAPI, WDM-KS); the second copy gets "(2)".
        Assert.DoesNotContain(backend.Devices, d => d.Id != d.Name);
        if (backend.Devices.Any(d => d.IsInput))
        {
            Assert.Single(backend.Devices, d => d.IsDefaultInput);
        }

        if (backend.Devices.Any(d => d.IsOutput))
        {
            Assert.Single(backend.Devices, d => d.IsDefaultOutput);
        }
    }
}
