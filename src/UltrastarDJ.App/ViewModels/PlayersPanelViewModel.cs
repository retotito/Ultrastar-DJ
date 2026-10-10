using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Core.Timing;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

/// <summary>One selectable mic input: a device channel. <see cref="UsedBy"/>: another player already sings on it.</summary>
public sealed record MicOption(string Label, MicBinding? Binding, string? UsedBy = null)
{
    public bool IsAvailable => UsedBy is null;
    public override string ToString() => UsedBy is null ? Label : L.F("audio_input.used_by", Label, UsedBy);
}

/// <summary>
/// Audio Input panel: four player cards with mic binding, per-card mic test, gain/gate/mix, live meter, note, latency
/// test. Tested mics are one set: toggling a card reopens exactly that set (two players on one dongle share a stream).
/// </summary>
public sealed partial class PlayersPanelViewModel : ViewModelBase, IDisposable
{
    private readonly AudioInputService _audio;
    private readonly PlayersService _players;
    private readonly OutputsService _outputs;
    private readonly ILogger<PlayersPanelViewModel> _log;
    private readonly DispatcherTimer _meterTimer;
    private readonly HashSet<int> _tested = [];

    [ObservableProperty] private bool _testing;
    [ObservableProperty] private AudioDeviceInfo? _monitorOutput;
    [ObservableProperty] private string _status = "";

    public PlayersPanelViewModel(AudioInputService audio, PlayersService players, OutputsService outputs, ILogger<PlayersPanelViewModel> log)
    {
        _audio = audio;
        _players = players;
        _outputs = outputs;
        _log = log;
        RefreshDevices();
        Players = new ObservableCollection<PlayerCardViewModel>(players.All.Select(p => new PlayerCardViewModel(p, this)));
        RefreshCardOptions();
        _audio.Mics.Analyzed += OnAnalyzed;
        _players.Changed += OnConfigChanged;
        _audio.MicsChanged += OnMicsChanged;
        // The mic delay depends on the latency of the output it was calibrated through.
        _outputs.Changed += OnOutputsChanged;
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => PollMeters());
        _meterTimer.Start();
    }

    public ObservableCollection<PlayerCardViewModel> Players { get; }
    // Every input once; each card shows its own copy with the inputs other players use marked (RefreshCardOptions).
    private readonly List<MicOption> _baseOptions = [];
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = [];

    internal PlayersService PlayersService => _players;
    internal AudioInputService Audio => _audio;

    [RelayCommand]
    private void RefreshDevices()
    {
        if (!_audio.RefreshDevices() && _baseOptions.Count > 0)
        {
            Status = L.T("audio_input.stop_test_first");
            return;
        }

        ReloadDeviceLists();
    }

    /// <summary>Plugged / unplugged (background poll or a dying stream): rebuild the lists and the missing marks.</summary>
    private void OnMicsChanged()
    {
        ReloadDeviceLists();
    }

    private void ReloadDeviceLists()
    {
        // Labels use the device id: two devices with the same name get "(2)" there, so both stay tellable apart.
        _baseOptions.Clear();
        _baseOptions.Add(new MicOption(L.T("audio_input.no_mic"), null));
        foreach (AudioDeviceInfo d in _audio.InputDevices)
        {
            if (d.MaxInputChannels >= 2)
            {
                _baseOptions.Add(new MicOption(L.F("audio_input.left", d.Id), new MicBinding(d.Id, MicChannelSide.Left)));
                _baseOptions.Add(new MicOption(L.F("audio_input.right", d.Id), new MicBinding(d.Id, MicChannelSide.Right)));
                _baseOptions.Add(new MicOption(L.F("audio_input.mono", d.Id), new MicBinding(d.Id, MicChannelSide.Mono)));
            }
            else
            {
                _baseOptions.Add(new MicOption(d.Id, new MicBinding(d.Id, MicChannelSide.Mono)));
            }
        }

        Outputs.Clear();
        foreach (AudioDeviceInfo d in _audio.OutputDevices)
        {
            Outputs.Add(d);
        }

        // The game output by default: that is where the mics are heard during a song.
        MonitorOutput ??= Outputs.FirstOrDefault(o => o.Id == _outputs.GamePortAudioDeviceId())
            ?? Outputs.FirstOrDefault(o => o.IsDefaultOutput) ?? Outputs.FirstOrDefault();
        RefreshCardOptions();
    }

    /// <summary>Rebuilds every card's list: inputs another player uses (same side, or Mono on either) are disabled.</summary>
    internal void RefreshCardOptions()
    {
        foreach (PlayerCardViewModel c in Players ?? [])
        {
            List<PlayerConfig> others = _players.All.Where(p => p.Id != c.Config.Id && p.Mic is not null).ToList();
            c.RebuildOptions(_baseOptions.Select(o => o.Binding is { } b && others.FirstOrDefault(p => p.Mic!.ConflictsWith(b)) is { } user
                ? o with { UsedBy = user.Name }
                : o));
        }
    }

    /// <summary>The panel closed (✕, click outside, other panel): no mic stays open in the background.</summary>
    public void StopTests()
    {
        if (_tested.Count > 0)
        {
            _tested.Clear();
            ApplyTests();
        }
    }

    internal void ToggleCardTest(PlayerCardViewModel card)
    {
        if (!_tested.Remove(card.Config.Id))
        {
            if (card.Config.Mic is null)
            {
                return;
            }

            _tested.Add(card.Config.Id);
        }

        ApplyTests();
    }

    /// <summary>(Re)opens the tested players' mics; drops players that lost their mic. While any mic is tested it is heard on the monitor output.</summary>
    private void ApplyTests()
    {
        _tested.RemoveWhere(id => _players.Get(id).Mic is null);
        if (_tested.Count == 0)
        {
            _audio.StopAll();
            Testing = false;
            Status = "";
        }
        else
        {
            _audio.StartTest(_tested.Select(_players.Get));
            Testing = _audio.Mics.IsRunning;
            Status = Testing ? "" : L.T("audio_input.cannot_open_mic");
            StartMonitor();
        }

        foreach (PlayerCardViewModel c in Players)
        {
            c.Testing = Testing && _audio.Mics.Pipeline(c.Config.Id) is not null;
            if (!c.Testing)
            {
                c.Level = 0;
                c.Note = "—";
                c.IsGated = false;
            }
        }
    }

    private void StartMonitor()
    {
        if (!Testing || MonitorOutput is null)
        {
            return;
        }

        _audio.StartMonitor(MonitorOutput.Id);
        if (!_audio.Monitor.IsRunning)
        {
            Status = L.T("audio_input.cannot_open_monitor");
        }
    }

    partial void OnMonitorOutputChanged(AudioDeviceInfo? value) => StartMonitor();

    /// <summary>Mic bindings changed → streams must be reopened.</summary>
    internal void RestartIfTesting()
    {
        if (Testing)
        {
            ApplyTests();
        }
    }

    private void OnOutputsChanged()
    {
        foreach (PlayerCardViewModel card in Players)
        {
            card.RefreshDelay();
        }
    }

    /// <summary>Scoring delay of a player in ms, and whether it was calibrated or is the default.</summary>
    internal (double Ms, bool Calibrated) MicDelay(PlayerConfig p)
        => (LatencyModel.InputDelayMs(p.CalibratedTotalMs, _outputs.LatencyFor(p.CalibrationOutputKey)), p.CalibratedTotalMs is not null);

    internal async Task CalibrateAsync(PlayerCardViewModel card)
    {
        string? output = _outputs.GamePortAudioDeviceId();
        if (card.Config.Mic is null || output is null)
        {
            Status = card.Config.Mic is null ? L.T("audio_input.bind_mic_first") : L.T("audio_input.output_missing");
            return;
        }

        bool wasTesting = Testing;
        if (wasTesting)
        {
            _audio.StopAll();
            Testing = false;
        }

        card.Calibrating = true;
        Status = L.F("audio_input.calibrating", card.Config.Name);
        try
        {
            string outputKey = _outputs.GameOutputKey;
            LatencyTest.Result r = await _audio.Latency.RunAsync(output, card.Config.Mic, card.Config.InputGain, trials: 5,
                progress: new Progress<double>(ms => Status = L.F("audio_input.calibrating_ms", card.Config.Name, ms)));
            _players.Update(card.Config.Id, p => p with { CalibratedTotalMs = Math.Round(r.MedianMs), CalibrationOutputKey = outputKey });
            card.Reload();
            Status = L.F("audio_input.calibrated", card.Config.Name, r.MedianMs, card.MicDelayText, string.Join(", ", r.TrialsMs.Select(t => t.ToString("F0"))));
        }
        catch (AudioBackendException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            card.Calibrating = false;
            if (wasTesting)
            {
                ApplyTests();
            }
        }
    }

    private void OnAnalyzed(IReadOnlyList<PitchSample> samples)
    {
        PitchSample[] copy = [.. samples];
        Dispatcher.UIThread.Post(() =>
        {
            foreach (PitchSample s in copy)
            {
                Players.FirstOrDefault(p => p.Config.Id == s.PlayerId)?.ApplySample(s);
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>Config changed elsewhere (Now Playing mix, mic unplugged → unassigned): mirror it; a lost mic ends its test.</summary>
    private void OnConfigChanged(PlayerConfig p)
    {
        PlayerCardViewModel? card = Players.FirstOrDefault(c => c.Config.Id == p.Id);
        card?.ApplyExternal(p);
        // A mic (un)assigned anywhere frees or takes an input for the other cards. Deferred: never rebuild a
        // drop-down while it is still handling its own selection.
        Dispatcher.UIThread.Post(RefreshCardOptions);
        if (p.Mic is null && _tested.Contains(p.Id))
        {
            ApplyTests();
        }
    }

    private void PollMeters()
    {
        if (!Testing)
        {
            return;
        }

        foreach (PlayerCardViewModel c in Players)
        {
            MicPipeline? pipe = _audio.Mics.Pipeline(c.Config.Id);
            c.Level = pipe?.LevelRms ?? 0;
            c.IsGated = pipe?.IsGated ?? false;
        }
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _audio.MicsChanged -= OnMicsChanged;
        _audio.Mics.Analyzed -= OnAnalyzed;
        _players.Changed -= OnConfigChanged;
        _outputs.Changed -= OnOutputsChanged;
        _audio.StopAll();
    }
}

public sealed partial class PlayerCardViewModel : ObservableObject
{
    private readonly PlayersPanelViewModel _owner;
    private bool _loading;

    [ObservableProperty] private PlayerConfig _config;
    [ObservableProperty] private MicOption? _selectedMic;
    /// <summary>"Player 1" … — fixed, in the UI language.</summary>
    public string Name => Config.Name;
    /// <summary>The gain knob, in dB (−40…+20); stored as a linear factor.</summary>
    [ObservableProperty] private double _inputGainDb;
    [ObservableProperty] private double _gateDb;
    [ObservableProperty] private double _mixGain;
    [ObservableProperty] private double _level;
    [ObservableProperty] private bool _isGated;
    [ObservableProperty] private string _note = "—";
    [ObservableProperty] private bool _calibrating;
    /// <summary>This card's mic is open in the test (meter and note are live).</summary>
    [ObservableProperty] private bool _testing;

    public PlayerCardViewModel(PlayerConfig config, PlayersPanelViewModel owner)
    {
        _owner = owner;
        _config = config;
        _inputGainDb = InputGainScale.ToDb(config.InputGain);
        _gateDb = ToDb(config.Threshold);
        _mixGain = Math.Min(1, config.MixGain);   // mix is 0–100 % (older settings went to 200 %)
        SyncMicOption();
    }

    public const double GateMinDb = -70;
    public const double GateMaxDb = -20;

    public string Title => $"P{Config.Id}";
    public bool HasMic => Config.Mic is not null;
    public string ColorKey => $"BrushPlayer{Config.Id}";
    /// <summary>
    /// This card's own list (other players' inputs disabled). Bound directly so ItemsSource resolves before
    /// SelectedItem when the panel view is recreated.
    /// </summary>
    public ObservableCollection<MicOption> MicOptions { get; } = [];

    public void RebuildOptions(IEnumerable<MicOption> options)
    {
        _loading = true;
        MicOptions.Clear();
        foreach (MicOption o in options)
        {
            MicOptions.Add(o);
        }

        _loading = false;
        SyncMicOption();
    }
    public string MicDelayText => _owner.MicDelay(Config) is var (ms, calibrated) ? $"{ms:F0} ms{(calibrated ? "" : L.T("audio_input.default_suffix"))}" : "";

    public void RefreshDelay() => OnPropertyChanged(nameof(MicDelayText));
    public string GateText => $"{GateDb:F0} dB";
    public string GainText => $"{InputGainScale.Snap(InputGainDb):+0.#;-0.#;0} dB";
    /// <summary>What the speakers get from this mic: the monitor mixes the gated signal × mix (linear).</summary>
    public double MixLevel => IsGated ? 0 : Level * MixGain;
    public string NoteText => IsGated ? L.T("audio_input.gated") : Note;

    partial void OnLevelChanged(double value) => OnPropertyChanged(nameof(MixLevel));
    partial void OnIsGatedChanged(bool value)
    {
        OnPropertyChanged(nameof(NoteText));
        OnPropertyChanged(nameof(MixLevel));
    }

    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(NoteText));

    private static double ToDb(double linear) => Math.Clamp(linear <= 0 ? GateMinDb : 20 * Math.Log10(linear), GateMinDb, GateMaxDb);
    private static double ToLinear(double db) => Math.Pow(10, db / 20);

    public void SyncMicOption()
    {
        _loading = true;
        MicOption? match = MicOptions.FirstOrDefault(o => Equals(o.Binding, Config.Mic));
        if (match is null && Config.Mic is { } mic)
        {
            // Keep the binding visible (and persisted) while the device is unplugged.
            match = new MicOption(L.F("audio_input.not_connected", mic.DeviceId, mic.Channel), mic);
            MicOptions.Add(match);
        }

        SelectedMic = match ?? MicOptions.FirstOrDefault();
        _loading = false;
    }

    public void Reload()
    {
        Config = _owner.PlayersService.Get(Config.Id);
        OnPropertyChanged(nameof(MicDelayText));
    }

    /// <summary>Config changed elsewhere (Now Playing mix row, mic unassigned on unplug): mirror it without re-saving.</summary>
    public void ApplyExternal(PlayerConfig config)
    {
        bool micChanged = !Equals(Config.Mic, config.Mic);
        _loading = true;
        Config = config;
        MixGain = Math.Min(1, config.MixGain);
        _loading = false;
        if (micChanged)
        {
            SyncMicOption();
        }
    }

    partial void OnConfigChanged(PlayerConfig value) => OnPropertyChanged(nameof(HasMic));

    public void ApplySample(PitchSample s)
    {
        Note = s.MidiNote < 0 ? "—" : $"{NoteName(s.MidiNote)}  {s.FrequencyHz:F0} Hz";
    }

    partial void OnSelectedMicChanged(MicOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Save(p => p with { Mic = value.Binding });
        _owner.RestartIfTesting();
    }

    partial void OnInputGainDbChanged(double value)
    {
        OnPropertyChanged(nameof(GainText));
        Save(p => p with { InputGain = Math.Round(InputGainScale.ToGain(InputGainScale.Snap(value)), 4) });
    }
    partial void OnGateDbChanged(double value)
    {
        OnPropertyChanged(nameof(GateText));
        Save(p => p with { Threshold = ToLinear(Math.Round(value)) });
    }
    partial void OnMixGainChanged(double value)
    {
        OnPropertyChanged(nameof(MixLevel));
        Save(p => p with { MixGain = Math.Round(value, 2) });
    }

    private void Save(Func<PlayerConfig, PlayerConfig> change)
    {
        if (_loading)
        {
            return;
        }

        _owner.PlayersService.Update(Config.Id, change);
        Config = _owner.PlayersService.Get(Config.Id);
    }

    [RelayCommand]
    private Task CalibrateAsync() => _owner.CalibrateAsync(this);

    [RelayCommand]
    private void ToggleTest() => _owner.ToggleCardTest(this);

    private static string NoteName(double midi)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int m = (int)Math.Round(midi);
        return $"{names[((m % 12) + 12) % 12]}{m / 12 - 1}";
    }
}
