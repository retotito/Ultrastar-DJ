using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Media;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Services;

/// <summary>One selectable output: a device, or one stereo pair of a multichannel device.</summary>
public sealed record OutputOption(string MpvDeviceId, string Name, int DeviceChannels, int ChannelOffset, bool IsDefault)
{
    public string Label => DeviceChannels > 2 ? L.F("output.channels", Name, ChannelOffset + 1, ChannelOffset + 2) : Name;
    public string Key => $"{MpvDeviceId}|{ChannelOffset}";
    public override string ToString() => Label;
}

/// <summary>
/// Output routing and gain for the game and preview channels, persisted. Device names come from mpv (which
/// does the playing) and channel counts from PortAudio (which knows the hardware); they are joined by name.
/// Outputs are checked every 3 s: a chosen one that is unplugged plays on the system default meanwhile (the choice is
/// kept — <see cref="OutputPresence"/>), and the channel goes back when it returns.
/// </summary>
public sealed class OutputsService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const string SettingsName = "outputs";
    private static readonly string[] HiddenPatterns = ["blackhole", "loopback", "audio recorder", "screen recorder"];

    private readonly ISettingsStore _settings;
    private readonly MediaService _media;
    private readonly IAudioBackend _backend;
    private readonly ILogger<OutputsService> _log;
    private readonly NotificationService _notifications;
    private readonly CancellationTokenSource _pollCts = new();
    // Outputs present at the last check (mpv ids); null until the first check — until then the choices are used as is.
    private IReadOnlySet<string>? _present;
    // Every output name seen (mpv id → name): a chosen output keeps its name while unplugged.
    private readonly Dictionary<string, string> _names = [];
    private OutputsDocument _doc;
    // Read by the game ticker and the beamers' render loop every frame: no key string building there.
    private double _gameLatencyMs;

    public OutputsService(ISettingsStore settings, MediaService media, IAudioBackend backend, AppSettingsService app, NotificationService notifications,
        ILogger<OutputsService> log)
    {
        _settings = settings;
        _media = media;
        _backend = backend;
        _notifications = notifications;
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

        _ = Task.Run(() => PollAsync(_pollCts.Token));
    }

    /// <summary>The game output was unplugged: a running song has to stop (it would go on from the default speakers, out of sync).</summary>
    public event Action<string>? GameOutputLost;

    /// <summary>Outputs appeared or disappeared: the Audio Output popup refreshes its list.</summary>
    public event Action? DevicesChanged;

    /// <summary>The channel's chosen output is unplugged (it plays on the system default meanwhile).</summary>
    public bool IsMissing(MediaChannelKind kind) => _present is { } p && OutputPresence.IsMissing(Of(kind).MpvDeviceId, p);

    private ChannelOutput Of(MediaChannelKind kind) => kind == MediaChannelKind.Game ? _doc.Game : _doc.Preview;

    /// <summary>"ARZOPA" — the device's name as stored or last seen (never the technical id when we know better).</summary>
    public string NameOf(ChannelOutput cfg) => cfg.Name ?? (_names.TryGetValue(cfg.MpvDeviceId, out string? n) ? n : cfg.MpvDeviceId);
    private static string Label(MediaChannelKind kind) => kind == MediaChannelKind.Game ? L.T("output.game") : L.T("output.preview");

    // mpv's list works while a song plays (PortAudio's refresh is refused while a stream is open).
    private async Task PollAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(PollInterval);
        do
        {
            IReadOnlyList<AudioOutputDevice> devices;
            try
            {
                devices = await _media.Game.ListAudioDevicesAsync().ConfigureAwait(false);
            }
            catch (MediaException)
            {
                continue;
            }

            Dictionary<string, string> now = devices.Where(d => d.Id != AudioOutputDevice.Auto.Id && !d.Id.StartsWith("avfoundation/", StringComparison.Ordinal) && !IsHidden(d.Name))
                .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Name);
            Dispatcher.UIThread.Post(() => OnDevices(now));
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private void OnDevices(IReadOnlyDictionary<string, string> devices)
    {
        HashSet<string> now = [.. devices.Keys];
        foreach ((string id, string n) in devices)
        {
            _names[id] = n;
        }

        // Choices made before names were stored get theirs once the device is seen.
        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            if (Of(kind) is { Name: null } c && devices.TryGetValue(c.MpvDeviceId, out string? seen))
            {
                Store(kind, c with { Name = seen });
            }
        }

        IReadOnlySet<string>? before = _present;
        if (before is not null && before.SetEquals(now))
        {
            return;
        }

        bool[] wasMissing = [IsMissing(MediaChannelKind.Game), IsMissing(MediaChannelKind.Preview)];
        _present = now;
        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            ChannelOutput cfg = Of(kind);
            bool missing = IsMissing(kind);
            bool was = wasMissing[kind == MediaChannelKind.Game ? 0 : 1];
            string name = NameOf(cfg);
            if (missing && (!was || before is null))
            {
                Apply(kind == MediaChannelKind.Game ? _media.Game : _media.Preview, cfg);
                _log.LogWarning("{Channel} output {Device} is not connected — on the system default meanwhile", kind, cfg.MpvDeviceId);
                _notifications.Warn(L.F("output.not_connected", Label(kind)), L.F("output.not_connected_detail", name));
                if (kind == MediaChannelKind.Game && before is not null)
                {
                    GameOutputLost?.Invoke(name);
                }
            }
            else if (!missing && was && before is not null)
            {
                Apply(kind == MediaChannelKind.Game ? _media.Game : _media.Preview, cfg);
                _log.LogInformation("{Channel} output {Device} is back", kind, cfg.MpvDeviceId);
                _notifications.Success(L.F("output.back", name), L.F("output.back_detail", Label(kind)));
            }
        }

        // Outputs nobody uses, plugged in or out: a hint either way (not at start-up). Used ones got their toast above.
        if (before is not null)
        {
            foreach (string id in now.Except(before).Where(id => id != _doc.Game.MpvDeviceId && id != _doc.Preview.MpvDeviceId))
            {
                _notifications.Info(L.T("output.new"), L.F("output.new_detail", devices[id]));
            }

            foreach (string id in before.Except(now).Where(id => id != _doc.Game.MpvDeviceId && id != _doc.Preview.MpvDeviceId))
            {
                _notifications.Info(L.T("output.gone"), _names.GetValueOrDefault(id, id));
            }
        }

        DevicesChanged?.Invoke();
    }

    public void Dispose()
    {
        _pollCts.Cancel();
        _pollCts.Dispose();
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
        List<OutputOption> list = [new(AudioOutputDevice.Auto.Id, L.T("output.system_default"), 2, 0, true)];
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

    private void Set(MediaChannelKind kind, OutputOption o)
    {
        ChannelOutput cfg = (kind == MediaChannelKind.Game ? _doc.Game : _doc.Preview) with { MpvDeviceId = o.MpvDeviceId, DeviceChannels = o.DeviceChannels, ChannelOffset = o.ChannelOffset, Name = o.Name };
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

    // What the channel really plays on: the choice, or the system default while it is unplugged.
    private void Apply(MediaChannel channel, ChannelOutput cfg)
    {
        if (_present is { } present && OutputPresence.IsMissing(cfg.MpvDeviceId, present))
        {
            channel.SetRouting(AudioOutputDevice.Auto.Id, 2, 0);
        }
        else
        {
            channel.SetRouting(cfg.MpvDeviceId, cfg.DeviceChannels, cfg.ChannelOffset);
        }

        channel.Gain = cfg.Gain;
    }

    private static bool IsHidden(string name) => HiddenPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

    public sealed record ChannelOutput(string MpvDeviceId, int DeviceChannels, int ChannelOffset, double Gain)
    {
        /// <summary>The device's name when it was chosen — for "LG TV (not connected)" and toasts while it is unplugged.</summary>
        public string? Name { get; init; }

        public static ChannelOutput Default() => new(AudioOutputDevice.Auto.Id, 2, 0, 1.0);
    }

    public sealed record OutputsDocument(ChannelOutput Game, ChannelOutput Preview)
    {
        /// <summary>Output latency in ms per <see cref="KeyOf"/>.</summary>
        public IReadOnlyDictionary<string, double> LatencyMs { get; init; } = new Dictionary<string, double>();

        public static OutputsDocument Default() => new(ChannelOutput.Default(), ChannelOutput.Default());
    }
}
