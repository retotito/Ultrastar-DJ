using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Audio Output panel: device (or channel pair) + fader + meter for the game and preview channels.</summary>
public sealed partial class AudioOutputPanelViewModel : ViewModelBase, IDisposable
{
    private readonly OutputsService _outputs;
    private readonly MediaService _media;
    private readonly DispatcherTimer _meter;

    [ObservableProperty] private string _status = "";

    public AudioOutputPanelViewModel(OutputsService outputs, MediaService media, SyncTestService sync)
    {
        _outputs = outputs;
        _media = media;
        Game = new OutputChannelViewModel("Game", "Beamer / main speakers", "music_note", media.Game, Options, o => outputs.SetGameOutput(o), g => outputs.SetGameGain(g))
        {
            Latency = new GameSyncViewModel(outputs, sync),
        };
        Preview = new OutputChannelViewModel("Preview", "DJ headphones / monitor", "headphones", media.Preview, Options, o => outputs.SetPreviewOutput(o), g => outputs.SetPreviewGain(g));
        _meter = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => { Game.Poll(); Preview.Poll(); });
        _meter.Start();
        // Volumes are also changed from the preview player and the Now Playing card: follow them live.
        _outputs.Changed += OnOutputsChanged;
        _ = RefreshAsync();
    }

    private void OnOutputsChanged()
    {
        Game.SyncGain(_outputs.Game.Gain);
        Preview.SyncGain(_outputs.Preview.Gain);
        Game.Latency?.SyncLatency();
    }

    /// <summary>The panel closed: the clicks end with it (like the mic tests of Audio Input).</summary>
    public void StopSyncTest() => Game.Latency?.Stop();

    public OutputChannelViewModel Game { get; }
    public OutputChannelViewModel Preview { get; }
    public ObservableCollection<OutputOption> Options { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IReadOnlyList<OutputOption> list = await _outputs.ListAsync();
        Options.Clear();
        foreach (OutputOption o in list)
        {
            Options.Add(o);
        }

        _outputs.ResetIfGone(list);
        Game.Sync(Options, _outputs.Game.MpvDeviceId, _outputs.Game.ChannelOffset, _outputs.Game.Gain);
        Preview.Sync(Options, _outputs.Preview.MpvDeviceId, _outputs.Preview.ChannelOffset, _outputs.Preview.Gain);
        Status = list.Count <= 1 ? "Only the system output was found — connect a USB or Bluetooth device and refresh" : "";
    }

    public void Dispose()
    {
        _meter.Stop();
        _outputs.Changed -= OnOutputsChanged;
        Game.Latency?.Dispose();
    }
}

/// <summary>Game card only: the game output's latency and the Test sync that finds it.</summary>
public sealed partial class GameSyncViewModel : ObservableObject, IDisposable
{
    private readonly OutputsService _outputs;
    private readonly SyncTestService _sync;
    private bool _loading;

    [ObservableProperty] private double _latencyMs;

    public GameSyncViewModel(OutputsService outputs, SyncTestService sync)
    {
        _outputs = outputs;
        _sync = sync;
        _latencyMs = outputs.GameLatencyMs;
        _sync.Changed += OnSyncChanged;
    }

    public double MinLatencyMs => OutputsService.MinLatencyMs;
    public double MaxLatencyMs => OutputsService.MaxLatencyMs;
    public bool IsRunning => _sync.IsRunning;
    public bool CanToggle => _sync.IsRunning || _sync.CanRun;
    public string ToggleTip => _sync.Blocker ?? (_sync.IsRunning
        ? "Stop the clicks"
        : "Plays a click every second on the game speakers and flashes the beamers when each click should be heard. Move LATENCY until flash and click happen together.");

    [RelayCommand]
    private void Toggle()
    {
        if (_sync.IsRunning)
        {
            _sync.Stop();
        }
        else
        {
            _sync.Start();
        }
    }

    public void Stop() => _sync.Stop();

    /// <summary>Game device changed (each has its own latency) or the value was migrated: mirror it.</summary>
    public void SyncLatency()
    {
        _loading = true;
        LatencyMs = _outputs.GameLatencyMs;
        _loading = false;
    }

    partial void OnLatencyMsChanged(double value)
    {
        if (!_loading)
        {
            _outputs.SetGameLatencyMs(value);
        }
    }

    private void OnSyncChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ToggleTip));
    }

    public void Dispose() => _sync.Changed -= OnSyncChanged;
}

public sealed partial class OutputChannelViewModel : ObservableObject
{
    private readonly MediaChannel _channel;
    private readonly Action<OutputOption> _setOutput;
    private readonly Action<double> _setGain;
    private bool _loading;

    [ObservableProperty] private OutputOption? _selected;
    [ObservableProperty] private double _gain = 1.0;
    [ObservableProperty] private double _level;

    public OutputChannelViewModel(string title, string subtitle, string glyph, MediaChannel channel, ObservableCollection<OutputOption> options,
        Action<OutputOption> setOutput, Action<double> setGain)
    {
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;
        _channel = channel;
        Options = options;
        _setOutput = setOutput;
        _setGain = setGain;
    }

    public string Title { get; }
    public string Subtitle { get; }
    /// <summary>Latency + Test sync; game card only.</summary>
    public GameSyncViewModel? Latency { get; init; }
    public string Glyph { get; }
    /// <summary>Shared with the panel; bound directly so ItemsSource resolves before SelectedItem when the view is recreated.</summary>
    public ObservableCollection<OutputOption> Options { get; }
    /// <summary>YouTube-only songs are metered too (mpv decodes them) — unlike the prototype, no dimming needed.</summary>
    public string StateText => _channel.State == MediaState.Idle ? "" : _channel.State.ToString();

    public void Sync(IEnumerable<OutputOption> options, string mpvId, int offset, double gain)
    {
        _loading = true;
        Selected = options.FirstOrDefault(o => o.MpvDeviceId == mpvId && o.ChannelOffset == offset) ?? options.FirstOrDefault();
        Gain = gain;
        _loading = false;
    }

    /// <summary>Volume changed elsewhere: mirror it without writing it back.</summary>
    public void SyncGain(double gain)
    {
        _loading = true;
        Gain = gain;
        _loading = false;
    }

    public void Poll()
    {
        Level = _channel.LevelRms;
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnSelectedChanged(OutputOption? value)
    {
        if (!_loading && value is not null)
        {
            _setOutput(value);
        }
    }

    partial void OnGainChanged(double value)
    {
        if (!_loading)
        {
            _setGain(value);
        }
    }
}
