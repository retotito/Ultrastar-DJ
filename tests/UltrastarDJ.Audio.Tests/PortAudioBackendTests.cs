using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Audio.PortAudio;
using Xunit.Abstractions;

namespace UltrastarDJ.Audio.Tests;

/// <summary>Runs against this machine's real devices, on Windows (the host API filter); elsewhere and without devices it passes trivially.</summary>
public class PortAudioBackendTests(ITestOutputHelper output)
{
    [Fact]
    public void Devices_AreListedOnce_WithOneDefaultPerDirection()
    {
        using PortAudioBackend backend = new(NullLogger<PortAudioBackend>.Instance);
        foreach (AudioDeviceInfo d in backend.Devices)
        {
            output.WriteLine($"{d.Id} (in {d.MaxInputChannels}, out {d.MaxOutputChannels}, {d.DefaultSampleRateHz} Hz{(d.IsDefaultInput ? ", default in" : "")}{(d.IsDefaultOutput ? ", default out" : "")})");
        }

        // Windows lists each device once per host API (MME, DirectSound, WASAPI, WDM-KS); the second copy gets "(2)".
        // Elsewhere nothing is filtered — and CI's sound-card-less Linux lists ALSA's virtual devices as they come.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

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
