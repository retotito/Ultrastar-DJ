using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Media;

/// <summary>
/// The kind of speaker behind an mpv output id, for the latency estimate of an unmeasured speaker. Windows keeps it in
/// the device registry (the bus that enumerated it, the form factor); macOS gives a Bluetooth device its address as
/// uid; PulseAudio / PipeWire name the sink after its bus.
/// </summary>
internal static partial class OutputKinds
{
    private static readonly ConcurrentDictionary<string, OutputKind> Cache = new();

    public static OutputKind Of(string mpvId) => Cache.GetOrAdd(mpvId, id =>
    {
        if (OperatingSystem.IsWindows() && id.StartsWith("wasapi/", StringComparison.Ordinal))
        {
            (string? enumerator, int? formFactor) = ReadWindowsEndpoint(id["wasapi/".Length..]);
            return Classify(id, enumerator, formFactor);
        }

        return Classify(id, null, null);
    });

    public static OutputKind Classify(string mpvId, string? windowsEnumerator, int? windowsFormFactor)
    {
        // BTHENUM (A2DP), BTHHFENUM (hands-free), BTHLEDEVICE (LE audio).
        if (windowsEnumerator is not null)
        {
            return windowsEnumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? OutputKind.Bluetooth
                : windowsFormFactor == DigitalAudioDisplayDevice ? OutputKind.Display
                : OutputKind.Other;
        }

        if (mpvId.StartsWith("coreaudio/", StringComparison.Ordinal))
        {
            return MacAddressUid().IsMatch(mpvId["coreaudio/".Length..]) ? OutputKind.Bluetooth : OutputKind.Other;
        }

        return mpvId.Contains("bluez", StringComparison.OrdinalIgnoreCase) ? OutputKind.Bluetooth
            : mpvId.Contains("hdmi", StringComparison.OrdinalIgnoreCase) ? OutputKind.Display
            : OutputKind.Other;
    }

    // EndpointFormFactor.DigitalAudioDisplayDevice (mmdeviceapi.h).
    private const int DigitalAudioDisplayDevice = 9;
    private const string RenderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\";
    private const string EnumeratorName = "{a45c254e-df1c-4efd-8020-67d146a850e0},24";   // PKEY_Device_EnumeratorName
    private const string FormFactor = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},0";        // PKEY_AudioEndpoint_FormFactor

    [SupportedOSPlatform("windows")]
    private static (string?, int?) ReadWindowsEndpoint(string guid)
    {
        try
        {
            using RegistryKey? props = Registry.LocalMachine.OpenSubKey(RenderKey + guid + @"\Properties");
            return (props?.GetValue(EnumeratorName) as string, props?.GetValue(FormFactor) as int?);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return (null, null);
        }
    }

    [GeneratedRegex("^[0-9A-Fa-f]{2}(-[0-9A-Fa-f]{2}){5}")]
    private static partial Regex MacAddressUid();
}
