using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Services;

/// <summary>One selectable output: a device, or one stereo pair of a multichannel device.</summary>
public sealed record OutputOption(string MpvDeviceId, string Name, int DeviceChannels, int ChannelOffset, bool IsDefault)
{
    public string Label => DeviceChannels > 2 ? $"{Name} — Ch {ChannelOffset + 1}–{ChannelOffset + 2}" : Name;
    public string Key => $"{MpvDeviceId}|{ChannelOffset}";
    public override string ToString() => Label;
}

/// <summary>
/// Output routing and gain for the game and preview channels, persisted. Device names come from mpv (which
/// does the playing) and channel counts from PortAudio (which knows the hardware); they are joined by name.
/// </summary>
public sealed class OutputsService
{
    private const string SettingsName = "outputs";
    private static readonly string[] HiddenPatterns = ["blackhole", "loopback", "audio recorder", "screen recorder"];

    private readonly ISettingsStore _settings;
    private readonly MediaService _media;
    private readonly IAudioBackend _backend;
    private readonly ILogger<OutputsService> _log;
    private OutputsDocument _doc;
    // Read by the game ticker and the beamers' render loop every frame: no key string building there.
    private double _gameLatencyMs;

    public OutputsService(ISettingsStore settings, MediaService media, IAudioBackend backend, AppSettingsService app, ILogger<OutputsService> log)
    {
        _settings = settings;
        _media = media;
        _backend = backend;
        _log = log;
        _doc = settings.Load(SettingsName, OutputsDocument.Default());
        Apply(_media.Game, _doc.Game);
        Apply(_media.Preview, _doc.Preview);
        _gameLatencyMs = LatencyFor(GameOutputKey);

        // The old global "Lyrics offset" (position + offset) becomes the current game device's latency once.
        if (_doc.LatencyMs.Count == 0 && app.LyricsOffsetMs != 0)
        {
            SetGameLatencyMs(-app.LyricsOffsetMs);
            app.ClearLyricsOffset();
        }
    }

    /// <summary>Latencies are kept per output (device + stereo pair): Bluetooth keeps its 250 ms, the cable its 15 ms.</summary>
    public static string KeyOf(ChannelOutput c) => $"{c.MpvDeviceId}|{c.ChannelOffset}";

    public string GameOutputKey => KeyOf(_doc.Game);

    /// <summary>Output latency of an output in ms (app plays → audience hears); 0 when never set.</summary>
    public double LatencyFor(string? key) => key is not null && _doc.LatencyMs.TryGetValue(key, out double ms) ? ms : 0;

    public double GameLatencyMs => _gameLatencyMs;

    public void SetGameLatencyMs(double ms)
    {
        Dictionary<string, double> latency = new(_doc.LatencyMs) { [GameOutputKey] = Math.Clamp(Math.Round(ms), MinLatencyMs, MaxLatencyMs) };
        _doc = _doc with { LatencyMs = latency };
        _gameLatencyMs = LatencyFor(GameOutputKey);
        _settings.Save(SettingsName, _doc);
        Changed?.Invoke();
    }

    public const double MinLatencyMs = 0;
    public const double MaxLatencyMs = 800;

    /// <summary>
    /// The PortAudio device behind the game output (mpv names it "coreaudio/&lt;uid&gt;"; its device list carries the
    /// human name PortAudio uses). The mic monitor, the sync clicks and the calibration tone play here, so they go
    /// through the same latency as the song.
    /// </summary>
    public string? GamePortAudioDeviceId()
    {
        string mpvId = _media.Game.DeviceId;
        List<AudioDeviceInfo> outs = [.. _backend.Devices.Where(d => d.IsOutput && !IsHidden(d.Name))];
        if (mpvId == AudioOutputDevice.Auto.Id)
        {
            return outs.FirstOrDefault(d => d.IsDefaultOutput)?.Id ?? (outs.Count > 0 ? outs[0].Id : null);
        }

        try
        {
            string? name = _media.Game.ListAudioDevicesAsync().GetAwaiter().GetResult().FirstOrDefault(d => d.Id == mpvId)?.Name;
            return outs.FirstOrDefault(d => d.Name == name)?.Id ?? outs.FirstOrDefault(d => d.IsDefaultOutput)?.Id;
        }
        catch (MediaException)
        {
            return null;
        }
    }

    public event Action? Changed;

    public ChannelOutput Game => _doc.Game;
    public ChannelOutput Preview => _doc.Preview;

    /// <summary>All selectable outputs. The first entry is always the system default.</summary>
    public async Task<IReadOnlyList<OutputOption>> ListAsync()
    {
        List<OutputOption> list = [new(AudioOutputDevice.Auto.Id, "System default", 2, 0, true)];
        IReadOnlyList<AudioOutputDevice> mpv;
        try
        {
            mpv = await _media.Game.ListAudioDevicesAsync();
        }
        catch (MediaException ex)
        {
            _log.LogWarning(ex, "mpv device list failed");
            return list;
        }

        foreach (AudioOutputDevice d in mpv)
        {
            // mpv lists every host API (coreaudio + avfoundation on macOS); keep the native one only.
            if (d.Id == AudioOutputDevice.Auto.Id || d.Id.StartsWith("avfoundation/", StringComparison.Ordinal) || IsHidden(d.Name))
            {
                continue;
            }

            int channels = _backend.Devices.FirstOrDefault(p => p.Name == d.Name && p.IsOutput)?.MaxOutputChannels ?? 2;
            if (channels <= 2)
            {
                list.Add(new OutputOption(d.Id, d.Name, 2, 0, false));
                continue;
            }

            for (int offset = 0; offset + 1 < channels; offset += 2)
            {
                list.Add(new OutputOption(d.Id, d.Name, channels, offset, false));
            }
        }

        return list;
    }

    public void SetGameOutput(OutputOption o) => Set(MediaChannelKind.Game, o);
    public void SetPreviewOutput(OutputOption o) => Set(MediaChannelKind.Preview, o);

    public void SetGameGain(double gain) => SetGain(MediaChannelKind.Game, gain);
    public void SetPreviewGain(double gain) => SetGain(MediaChannelKind.Preview, gain);

    /// <summary>Call after a device refresh: falls back to default when a stored device vanished.</summary>
    public void ResetIfGone(IReadOnlyList<OutputOption> available)
    {
        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            ChannelOutput cfg = kind == MediaChannelKind.Game ? _doc.Game : _doc.Preview;
            if (cfg.MpvDeviceId != AudioOutputDevice.Auto.Id && !available.Any(a => a.MpvDeviceId == cfg.MpvDeviceId))
            {
                _log.LogWarning("{Channel} output {Device} is gone — back to system default", kind, cfg.MpvDeviceId);
                Set(kind, available[0]);
            }
        }
    }

    private void Set(MediaChannelKind kind, OutputOption o)
    {
        ChannelOutput cfg = (kind == MediaChannelKind.Game ? _doc.Game : _doc.Preview) with { MpvDeviceId = o.MpvDeviceId, DeviceChannels = o.DeviceChannels, ChannelOffset = o.ChannelOffset };
        Store(kind, cfg);
        Apply(kind == MediaChannelKind.Game ? _media.Game : _media.Preview, cfg);
    }

    private void SetGain(MediaChannelKind kind, double gain)
    {
        ChannelOutput cfg = (kind == MediaChannelKind.Game ? _doc.Game : _doc.Preview) with { Gain = Math.Clamp(gain, 0, 1) };
        Store(kind, cfg);
        (kind == MediaChannelKind.Game ? _media.Game : _media.Preview).Gain = cfg.Gain;
    }

    private void Store(MediaChannelKind kind, ChannelOutput cfg)
    {
        _doc = kind == MediaChannelKind.Game ? _doc with { Game = cfg } : _doc with { Preview = cfg };
        _gameLatencyMs = LatencyFor(GameOutputKey);
        _settings.Save(SettingsName, _doc);
        Changed?.Invoke();
    }

    private static void Apply(MediaChannel channel, ChannelOutput cfg)
    {
        channel.SetRouting(cfg.MpvDeviceId, cfg.DeviceChannels, cfg.ChannelOffset);
        channel.Gain = cfg.Gain;
    }

    private static bool IsHidden(string name) => HiddenPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

    public sealed record ChannelOutput(string MpvDeviceId, int DeviceChannels, int ChannelOffset, double Gain)
    {
        public static ChannelOutput Default() => new(AudioOutputDevice.Auto.Id, 2, 0, 1.0);
    }

    public sealed record OutputsDocument(ChannelOutput Game, ChannelOutput Preview)
    {
        /// <summary>Output latency in ms per <see cref="KeyOf"/>.</summary>
        public IReadOnlyDictionary<string, double> LatencyMs { get; init; } = new Dictionary<string, double>();

        public static OutputsDocument Default() => new(ChannelOutput.Default(), ChannelOutput.Default());
    }
}
