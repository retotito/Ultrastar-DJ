using Microsoft.Extensions.Logging.Abstractions;
using UltrastarDJ.Audio.PortAudio;
using Xunit.Abstractions;

namespace UltrastarDJ.Audio.Tests;

/// <summary>
/// Runs against this machine's real devices: the host API filter on Windows, the sound-server sinks on Linux (CI's
/// sound-card-less Linux has no sound server). Without devices they pass trivially.
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

    /// <summary>
    /// Linux: every PipeWire / PulseAudio speaker opens by sink name (the mic monitor follows the game output there).
    /// Without a sound server (CI) it passes trivially.
    /// </summary>
    [Fact]
    public void Linux_EverySinkOpens()
    {
        if (!OperatingSystem.IsLinux() || Pactl("list short sinks") is not { } sinks)
        {
            return;
        }

        using PortAudioBackend backend = new(NullLogger<PortAudioBackend>.Instance);
        foreach (string sink in sinks.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t')[1]))
        {
            if (backend.SinkOutputId(sink) is not { } id)
            {
                continue;
            }

            output.WriteLine(id);
            Assert.NotNull(backend.Find(id));
            using IAudioLine line = backend.OpenOutput(id, 0, null, (_, _, _) => { });
            Thread.Sleep(200);
            Assert.True(line.IsActive);
        }
    }

    private static string? Pactl(string args)
    {
        try
        {
            using System.Diagnostics.Process p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pactl", args) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? text : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
