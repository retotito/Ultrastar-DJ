using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Core.Timing;
using UltrastarDJ.Media;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Services;

/// <summary>One selectable output: a device, or one stereo pair of a multichannel device. <see cref="IsDefault"/>: the system plays its sounds there now.</summary>
public sealed record OutputOption(string MpvDeviceId, string Name, int DeviceChannels, int ChannelOffset, bool IsDefault)
{
    public string Label => (DeviceChannels > 2 ? L.F("output.channels", Name, ChannelOffset + 1, ChannelOffset + 2) : Name)
        + (IsDefault ? L.T("output.default_suffix") : "");
    public string Key => $"{MpvDeviceId}|{ChannelOffset}";
    public override string ToString() => Label;
}

/// <summary>
/// Output routing and gain for the game and preview channels, persisted. Device names come from mpv (which
/// does the playing) and channel counts from PortAudio (which knows the hardware); they are joined by name.
/// Outputs are checked every 3 s: a chosen one that is unplugged is replaced by the system default
/// (<see cref="OutputPresence"/>); when it is back the DJ is told and may choose it again.
/// </summary>
public sealed class OutputsService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const string SettingsName = "outputs";
    private static readonly string[] HiddenPatterns = ["blackhole", "loopback", "audio recorder", "screen recorder", "stereo mix"];

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
    private readonly Dictionary<string, OutputKind> _kinds = [];
    private OutputsDocument _doc;
    // The output the game really plays on (see GameOutputKey) and its latency — read by the game ticker and the
    // beamers' render loop every frame: no key string building there.
    private string _gameKey;
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
        _gameKey = ResolveGameKey();
        _gameLatencyMs = LatencyFor(_gameKey);

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

    /// <summary>
    /// The game plays on another speaker (chosen by hand, replaced after unplugging, or a new system default): mic
    /// calibrations are per speaker, so the mics may need calibrating there.
    /// </summary>
    public event Action? GameOutputChanged;

    // The channel's chosen output is unplugged (OnDevices replaces it).
    private bool IsMissing(MediaChannelKind kind) => _present is { } p && OutputPresence.IsMissing(Of(kind).MpvDeviceId, p);

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

            Dictionary<string, AudioOutputDevice> now = devices.Where(d => d.Id != AudioOutputDevice.Auto.Id && !IsHidden(d.Name))
                .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
            Dispatcher.UIThread.Post(() => OnDevices(now));
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private void OnDevices(IReadOnlyDictionary<string, AudioOutputDevice> devices)
    {
        HashSet<string> now = [.. devices.Keys];
        foreach ((string id, AudioOutputDevice d) in devices)
        {
            _names[id] = d.Name;
            _kinds[id] = d.Kind;
        }

        // Choices made before names were stored get theirs once the device is seen.
        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            if (Of(kind) is { Name: null } c && devices.TryGetValue(c.MpvDeviceId, out AudioOutputDevice? seen))
            {
                Store(kind, c with { Name = seen.Name });
            }
        }

        IReadOnlySet<string>? before = _present;
        if (before is not null && before.SetEquals(now))
        {
            // Same devices, but the system default may have moved (it is what "System default" calibrates).
            PinSystemDefault();
            UpdateGameKey(announce: true);
            return;
        }

        _present = now;
        PinSystemDefault();
        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            ChannelOutput cfg = Of(kind);
            if (!IsMissing(kind))
            {
                continue;
            }

            // Gone (or not plugged in at start): the speaker the system plays on now, for good — not "System default",
            // which the OS turns back into the returning Bluetooth speaker. The DJ chooses it again when it is back.
            string name = NameOf(cfg);
            string replacement = OutputPresence.Replacement(cfg.MpvDeviceId, now, [SystemDefaultOutput.CurrentId(), PortAudioDefaultId()]);
            ChannelOutput next = replacement == AudioOutputDevice.Auto.Id
                ? ChannelOutput.Default() with { Gain = cfg.Gain }
                : new ChannelOutput(replacement, 2, 0, cfg.Gain) { Name = devices[replacement].Name };
            Store(kind, next);
            Apply(kind == MediaChannelKind.Game ? _media.Game : _media.Preview, next);
            _log.LogWarning("{Channel} output {Device} is not connected — {Replacement} from now on", kind, cfg.MpvDeviceId, replacement);
            _notifications.Warn(L.F("output.not_connected", Label(kind)), L.F("output.not_connected_detail", name, NameOf(next)));
            if (kind == MediaChannelKind.Game && before is not null)
            {
                GameOutputLost?.Invoke(name);
            }
        }

        // Plugged in or out (not at start-up): a hint either way; a returning one can be chosen again.
        if (before is not null)
        {
            foreach (string id in now.Except(before).Where(id => id != _doc.Game.MpvDeviceId && id != _doc.Preview.MpvDeviceId))
            {
                _notifications.Info(L.T("output.new"), L.F("output.new_detail", devices[id].Name));
            }

            foreach (string id in before.Except(now).Where(id => id != _doc.Game.MpvDeviceId && id != _doc.Preview.MpvDeviceId))
            {
                _notifications.Info(L.T("output.gone"), _names.GetValueOrDefault(id, id));
            }
        }

        // The first check only learns the names behind "System default": nothing changed for the DJ.
        UpdateGameKey(announce: before is not null);
        DevicesChanged?.Invoke();
    }

    /// <summary>
    /// The output the game really plays on, as latency and calibration key: the chosen one, or for "System default" the
    /// device that is the default now — calibrating on the system default calibrates that device.
    /// </summary>
    public string GameOutputKey => _gameKey;

    /// <summary>"JBL Charge 5", or "System default · Speakers (Realtek(R) Audio)".</summary>
    public string GameOutputName
    {
        get
        {
            string id = DeviceOf(_gameKey);
            string name = _names.GetValueOrDefault(id, id == AudioOutputDevice.Auto.Id ? "" : NameOf(_doc.Game));
            return _doc.Game.MpvDeviceId != AudioOutputDevice.Auto.Id ? name
                : string.IsNullOrEmpty(name) ? L.T("output.system_default")
                : $"{L.T("output.system_default")} · {name}";
        }
    }

    /// <summary>Mic delay of a player on the game output now, and whether it was calibrated there (else an estimate).</summary>
    public (double Ms, bool Calibrated) MicDelayFor(PlayerConfig p)
        => LatencyModel.MicDelay(p.CalibrationFor(_gameKey), _gameLatencyMs, _kinds.GetValueOrDefault(DeviceOf(_gameKey)));

    private static string DeviceOf(string key) => key[..key.LastIndexOf('|')];

    private string ResolveGameKey()
    {
        ChannelOutput game = _doc.Game;
        return game.MpvDeviceId == AudioOutputDevice.Auto.Id && DefaultDeviceId() is { } id
            ? KeyOf(game with { MpvDeviceId = id, ChannelOffset = 0 })
            : KeyOf(game);
    }

    // The device behind "System default": asked from the OS where we can (SystemDefaultOutput), else PortAudio's default
    // by name. Only a present one counts — PortAudio's list is only re-read while no stream is open.
    private string? DefaultDeviceId()
    {
        string? id = SystemDefaultOutput.CurrentId() ?? PortAudioDefaultId();
        return id is not null && _present is { } present && present.Contains(id) ? id : null;
    }

    private string? PortAudioDefaultId()
    {
        string? name = _backend.Devices.FirstOrDefault(d => d.IsDefaultOutput && !IsHidden(d.Name))?.Name;
        return name is null ? null : _names.FirstOrDefault(n => n.Value == name).Key;
    }

    // "System default" from older settings (or the fallback when no speaker was known) becomes the speaker it plays on,
    // once that is known: the list shows each speaker once and marks the default instead of offering it twice.
    private void PinSystemDefault()
    {
        if (DefaultDeviceId() is not { } id)
        {
            return;
        }

        foreach (MediaChannelKind kind in (ReadOnlySpan<MediaChannelKind>)[MediaChannelKind.Game, MediaChannelKind.Preview])
        {
            if (Of(kind) is { } cfg && cfg.MpvDeviceId == AudioOutputDevice.Auto.Id)
            {
                _log.LogInformation("{Channel} output: System default → {Device}", kind, _names.GetValueOrDefault(id, id));
                Store(kind, new ChannelOutput(id, 2, 0, cfg.Gain) { Name = _names.GetValueOrDefault(id) });
                Apply(kind == MediaChannelKind.Game ? _media.Game : _media.Preview, Of(kind));
            }
        }
    }

    private void UpdateGameKey(bool announce)
    {
        string key = ResolveGameKey();
        _gameLatencyMs = LatencyFor(key);
        if (key == _gameKey)
        {
            return;
        }

        _gameKey = key;
        _log.LogInformation("Game plays on {Output} ({Key})", GameOutputName, key);
        if (announce)
        {
            GameOutputChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _pollCts.Cancel();
        _pollCts.Dispose();
    }

    /// <summary>Latencies are kept per output (device + stereo pair): Bluetooth keeps its 250 ms, the cable its 15 ms.</summary>
    public static string KeyOf(ChannelOutput c) => $"{c.MpvDeviceId}|{c.ChannelOffset}";

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

        // Linux: PortAudio's list names no PipeWire / PulseAudio speaker; the sink is reached through ALSA's plugin.
        if (MpvPlayer.SoundServerSink(mpvId) is { } sink && _backend.SinkOutputId(sink) is { } sinkOutput)
        {
            return sinkOutput;
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

    /// <summary>
    /// All selectable outputs, each speaker once; the one the system plays on now is marked "(default)". "System default"
    /// itself is offered only while that speaker is unknown (or still chosen) — next to the speakers it was the same one twice.
    /// </summary>
    public async Task<IReadOnlyList<OutputOption>> ListAsync()
    {
        OutputOption systemDefault = new(AudioOutputDevice.Auto.Id, L.T("output.system_default"), 2, 0, false);
        IReadOnlyList<AudioOutputDevice> mpv;
        try
        {
            mpv = await _media.Game.ListAudioDevicesAsync();
        }
        catch (MediaException ex)
        {
            _log.LogWarning(ex, "mpv device list failed");
            return [systemDefault];
        }

        string? defaultId = DefaultDeviceId();
        List<OutputOption> list = [];
        foreach (AudioOutputDevice d in mpv)
        {
            if (d.Id == AudioOutputDevice.Auto.Id || IsHidden(d.Name))
            {
                continue;
            }

            int channels = _backend.Devices.FirstOrDefault(p => p.Name == d.Name && p.IsOutput)?.MaxOutputChannels ?? 2;
            if (channels <= 2)
            {
                list.Add(new OutputOption(d.Id, d.Name, 2, 0, d.Id == defaultId));
                continue;
            }

            for (int offset = 0; offset + 1 < channels; offset += 2)
            {
                list.Add(new OutputOption(d.Id, d.Name, channels, offset, d.Id == defaultId));
            }
        }

        if (defaultId is null || _doc.Game.MpvDeviceId == AudioOutputDevice.Auto.Id || _doc.Preview.MpvDeviceId == AudioOutputDevice.Auto.Id)
        {
            list.Insert(0, systemDefault);
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
        _settings.Save(SettingsName, _doc);
        UpdateGameKey(announce: _present is not null);
        Changed?.Invoke();
    }

    // What the channel plays on: the choice — or the system default if it vanished before the next check replaces it.
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

        /// <summary>System speaker at 80 %: room to turn up without leaving the slider's range.</summary>
        public static ChannelOutput Default() => new(AudioOutputDevice.Auto.Id, 2, 0, 0.8);
    }

    public sealed record OutputsDocument(ChannelOutput Game, ChannelOutput Preview)
    {
        /// <summary>Output latency in ms per <see cref="KeyOf"/>.</summary>
        public IReadOnlyDictionary<string, double> LatencyMs { get; init; } = new Dictionary<string, double>();

        public static OutputsDocument Default() => new(ChannelOutput.Default(), ChannelOutput.Default());
    }
}
