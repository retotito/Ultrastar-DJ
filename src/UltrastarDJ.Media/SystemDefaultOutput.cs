using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltrastarDJ.Media;

/// <summary>
/// The output the system plays "System default" on right now, as mpv's id — asked from the OS, because PortAudio's list
/// is only re-read while no stream is open. Windows: the multimedia default endpoint (its id ends in the same
/// <c>{guid}</c> as mpv's <c>wasapi/{guid}</c>). Linux: the sound server's default sink as mpv's <c>pulse/&lt;sink&gt;</c>
/// (PortAudio's default there is ALSA's "default", which names no speaker). Elsewhere null: the caller falls back to
/// PortAudio's default by name.
/// </summary>
public static class SystemDefaultOutput
{
    public static string? CurrentId()
        => OperatingSystem.IsWindows() ? WindowsDefault()
            : OperatingSystem.IsLinux() ? LinuxDefault()
            : null;

    // pactl ships with PulseAudio and with PipeWire's PulseAudio server; without it there is no pulse/ route either.
    private static string? LinuxDefault()
    {
        try
        {
            using Process pactl = Process.Start(new ProcessStartInfo("pactl", "get-default-sink")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            if (!pactl.WaitForExit(TimeSpan.FromSeconds(1)))
            {
                pactl.Kill();
                return null;
            }

            string sink = pactl.StandardOutput.ReadToEnd().Trim();
            return pactl.ExitCode == 0 && sink.Length > 0 && !sink.Contains('\n') ? "pulse/" + sink : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsDefault()
    {
        try
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out IMMDevice? device) != 0 || device is null)
                {
                    return null;
                }

                try
                {
                    // "{0.0.0.00000000}.{3ed2a6a1-…}" → "wasapi/{3ed2a6a1-…}"
                    return device.GetId(out string? id) == 0 && id?.LastIndexOf('.') is int dot and >= 0 ? "wasapi/" + id[(dot + 1)..] : null;
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private const int DataFlowRender = 0;   // EDataFlow.eRender
    private const int RoleMultimedia = 1;   // ERole.eMultimedia (music, video — what Windows' sound settings switch)

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    // mmdeviceapi.h; only the methods up to the ones used, in vtable order.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }
}
