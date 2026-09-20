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

namespace UltrastarDJ.App.ViewModels;

/// <summary>One selectable mic input: a device channel.</summary>
public sealed record MicOption(string Label, MicBinding? Binding)
{
    public override string ToString() => Label;
}

/// <summary>Audio Input panel: four player cards with mic binding, gain/gate/mix, live meter, note, latency test.</summary>
public sealed partial class PlayersPanelViewModel : ViewModelBase, IDisposable
{
    private readonly AudioInputService _audio;
    private readonly PlayersService _players;
    private readonly ILogger<PlayersPanelViewModel> _log;
    private readonly DispatcherTimer _meterTimer;

    [ObservableProperty] private bool _testing;
    [ObservableProperty] private bool _monitoring;
    [ObservableProperty] private AudioDeviceInfo? _monitorOutput;
    [ObservableProperty] private string _status = "";

    public PlayersPanelViewModel(AudioInputService audio, PlayersService players, ILogger<PlayersPanelViewModel> log)
    {
        _audio = audio;
        _players = players;
        _log = log;
        RefreshDevices();
        Players = new ObservableCollection<PlayerCardViewModel>(players.All.Select(p => new PlayerCardViewModel(p, this)));
        _audio.Mics.Analyzed += OnAnalyzed;
        _audio.Mics.DeviceLost += id => Dispatcher.UIThread.Post(() => Status = $"Microphone disconnected: {id}");
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => PollMeters());
        _meterTimer.Start();
    }

    public ObservableCollection<PlayerCardViewModel> Players { get; }
    public ObservableCollection<MicOption> MicOptions { get; } = [];
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = [];

    internal PlayersService PlayersService => _players;
    internal AudioInputService Audio => _audio;

    [RelayCommand]
    private void RefreshDevices()
    {
        if (!_audio.RefreshDevices() && MicOptions.Count > 0)
        {
            Status = "Stop the mic test before refreshing devices";
            return;
        }

        MicOptions.Clear();
        MicOptions.Add(new MicOption("— no microphone —", null));
        foreach (AudioDeviceInfo d in _audio.InputDevices)
        {
            if (d.MaxInputChannels >= 2)
            {
                MicOptions.Add(new MicOption($"{d.Name} — Left", new MicBinding(d.Id, MicChannelSide.Left)));
                MicOptions.Add(new MicOption($"{d.Name} — Right", new MicBinding(d.Id, MicChannelSide.Right)));
                MicOptions.Add(new MicOption($"{d.Name} — Mono (L+R)", new MicBinding(d.Id, MicChannelSide.Mono)));
            }
            else
            {
                MicOptions.Add(new MicOption(d.Name, new MicBinding(d.Id, MicChannelSide.Mono)));
            }
        }

        Outputs.Clear();
        foreach (AudioDeviceInfo d in _audio.OutputDevices)
        {
            Outputs.Add(d);
        }

        MonitorOutput ??= Outputs.FirstOrDefault(o => o.IsDefaultOutput) ?? Outputs.FirstOrDefault();
        foreach (PlayerCardViewModel c in Players ?? [])
        {
            c.SyncMicOption();
        }
    }

    [RelayCommand]
    private void ToggleTest()
    {
        if (Testing)
        {
            _audio.StopAll();
            Testing = false;
            Monitoring = false;
            Status = "";
            return;
        }

        if (!_players.WithMic.Any())
        {
            Status = "Assign a microphone to at least one player first";
            return;
        }

        _audio.StartAll();
        Testing = _audio.Mics.IsRunning;
        Status = Testing ? "Listening — sing into each mic" : "Could not open the microphones (see log)";
    }

    // Bound two-way to the ear toggle: the property change *is* the command.
    partial void OnMonitoringChanged(bool value)
    {
        if (!value)
        {
            _audio.StopMonitor();
            return;
        }

        if (!Testing || MonitorOutput is null)
        {
            Status = Testing ? "Choose a monitor output first" : "Start the mic test first";
            Dispatcher.UIThread.Post(() => Monitoring = false);
            return;
        }

        _audio.StartMonitor(MonitorOutput.Id);
        if (!_audio.Monitor.IsRunning)
        {
            Status = "Could not open the monitor output (see log)";
            Dispatcher.UIThread.Post(() => Monitoring = false);
        }
    }

    partial void OnMonitorOutputChanged(AudioDeviceInfo? value)
    {
        if (Monitoring && value is not null)
        {
            _audio.StartMonitor(value.Id);
        }
    }

    /// <summary>Mic bindings changed → streams must be reopened.</summary>
    internal void RestartIfTesting()
    {
        if (Testing)
        {
            bool monitor = Monitoring;
            _audio.StopAll();
            _audio.StartAll();
            Testing = _audio.Mics.IsRunning;
            if (monitor && Testing && MonitorOutput is not null)
            {
                _audio.StartMonitor(MonitorOutput.Id);
            }
            else
            {
                Monitoring = false;
            }
        }
    }

    internal async Task CalibrateAsync(PlayerCardViewModel card)
    {
        if (card.Config.Mic is null || MonitorOutput is null)
        {
            Status = "Bind a mic and choose an output to calibrate";
            return;
        }

        bool wasTesting = Testing;
        if (wasTesting)
        {
            _audio.StopAll();
            Testing = false;
            Monitoring = false;
        }

        card.Calibrating = true;
        Status = $"Calibrating {card.Config.Name}: playing 5 beeps — hold the mic near the speaker";
        try
        {
            LatencyTest.Result r = await _audio.Latency.RunAsync(MonitorOutput.Id, card.Config.Mic, trials: 5,
                new Progress<double>(ms => Status = $"Calibrating {card.Config.Name}: {ms:F0} ms…"));
            _players.Update(card.Config.Id, p => p with { MicDelayMs = Math.Round(r.MedianMs) });
            card.Reload();
            Status = $"{card.Config.Name}: mic delay {r.MedianMs:F0} ms (trials {string.Join(", ", r.TrialsMs.Select(t => t.ToString("F0")))})";
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
                _audio.StartAll();
                Testing = _audio.Mics.IsRunning;
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
        _audio.Mics.Analyzed -= OnAnalyzed;
        _audio.StopAll();
    }
}

public sealed partial class PlayerCardViewModel : ObservableObject
{
    private readonly PlayersPanelViewModel _owner;
    private bool _loading;

    [ObservableProperty] private PlayerConfig _config;
    [ObservableProperty] private MicOption? _selectedMic;
    [ObservableProperty] private string _name;
    [ObservableProperty] private double _inputGain;
    [ObservableProperty] private double _gateDb;
    [ObservableProperty] private double _mixGain;
    [ObservableProperty] private double _level;
    [ObservableProperty] private bool _isGated;
    [ObservableProperty] private string _note = "—";
    [ObservableProperty] private bool _calibrating;

    public PlayerCardViewModel(PlayerConfig config, PlayersPanelViewModel owner)
    {
        _owner = owner;
        _config = config;
        _name = config.Name;
        _inputGain = config.InputGain;
        _gateDb = ToDb(config.Threshold);
        _mixGain = config.MixGain;
        SyncMicOption();
    }

    public const double GateMinDb = -70;
    public const double GateMaxDb = -20;

    public string Title => $"P{Config.Id}";
    public string ColorKey => $"BrushPlayer{Config.Id}";
    public string MicDelayText => $"{Config.MicDelayMs:F0} ms";
    public string GateText => $"{GateDb:F0} dB";
    /// <summary>Level in dB mapped to 0..1 over the meter's 70 dB range — linear RMS is useless for quiet mics.</summary>
    public double LevelDb => Level <= 0 ? 0 : Math.Clamp((20 * Math.Log10(Level) - GateMinDb) / -GateMinDb, 0, 1);
    /// <summary>Gate position on the same 0..1 meter scale, so the user sees where the gate cuts.</summary>
    public double GateMark => (GateDb - GateMinDb) / -GateMinDb;
    public string NoteText => IsGated ? "gated" : Note;

    partial void OnLevelChanged(double value) => OnPropertyChanged(nameof(LevelDb));
    partial void OnIsGatedChanged(bool value) => OnPropertyChanged(nameof(NoteText));
    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(NoteText));

    private static double ToDb(double linear) => Math.Clamp(linear <= 0 ? GateMinDb : 20 * Math.Log10(linear), GateMinDb, GateMaxDb);
    private static double ToLinear(double db) => Math.Pow(10, db / 20);

    public void SyncMicOption()
    {
        _loading = true;
        SelectedMic = _owner.MicOptions.FirstOrDefault(o => Equals(o.Binding, Config.Mic)) ?? _owner.MicOptions.FirstOrDefault();
        _loading = false;
    }

    public void Reload()
    {
        Config = _owner.PlayersService.Get(Config.Id);
        OnPropertyChanged(nameof(MicDelayText));
    }

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

    partial void OnNameChanged(string value) => Save(p => p with { Name = value });
    partial void OnInputGainChanged(double value) => Save(p => p with { InputGain = Math.Round(value, 2) });
    partial void OnGateDbChanged(double value)
    {
        OnPropertyChanged(nameof(GateText));
        OnPropertyChanged(nameof(GateMark));
        Save(p => p with { Threshold = ToLinear(Math.Round(value)) });
    }
    partial void OnMixGainChanged(double value) => Save(p => p with { MixGain = Math.Round(value, 2) });

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

    private static string NoteName(double midi)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int m = (int)Math.Round(midi);
        return $"{names[((m % 12) + 12) % 12]}{m / 12 - 1}";
    }
}
