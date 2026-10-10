using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Timing;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Test sync (Audio Output → Game): clicks on the game output, a flash on every open beamer when each click should
/// be heard (first click + k s + the game output's latency). The DJ moves LATENCY until flash and click land
/// together; that value is what the lyrics are shifted by (docs/03-game-engine.md "Latency and sync").
/// Needs an open beamer and no running song; stops when either goes away.
/// </summary>
public sealed class SyncTestService : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly OutputsService _outputs;
    private readonly PlaybackService _playback;
    private readonly IDisplayService _displays;
    private readonly ILogger<SyncTestService> _log;
    private IAudioLine? _line;
    private SyncClicks? _clicks;
    private string? _outputKey;

    public SyncTestService(IAudioBackend backend, OutputsService outputs, PlaybackService playback, IDisplayService displays, ILogger<SyncTestService> log)
    {
        _backend = backend;
        _outputs = outputs;
        _playback = playback;
        _displays = displays;
        _log = log;
        _playback.StateChanged += _ => OnConditionsChanged();
        _displays.OpenStateChanged += (_, _) => OnConditionsChanged();
        _outputs.Changed += OnOutputsChanged;
    }

    /// <summary>UI thread: running state or <see cref="CanRun"/> changed.</summary>
    public event Action? Changed;

    public bool IsRunning => _line is not null;

    public bool CanRun => _playback.AnyDisplayOpen && !IsSongRunning;

    /// <summary>Why <see cref="CanRun"/> is false, for the button's tooltip.</summary>
    public string? Blocker => IsSongRunning ? L.T("sync.not_while_song") : !_playback.AnyDisplayOpen ? L.T("sync.open_display_first") : null;

    private bool IsSongRunning => _playback.State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused;

    /// <summary>Render thread of the beamers, every frame: allocation-free.</summary>
    public bool IsFlashOn()
    {
        if (_clicks?.FirstClickTimestamp is not { } first)
        {
            return false;
        }

        double since = (double)(Stopwatch.GetTimestamp() - first) / Stopwatch.Frequency;
        return LatencyModel.SyncFlashOn(since, _outputs.GameLatencyMs);
    }

    public void Start()
    {
        if (IsRunning || !CanRun)
        {
            return;
        }

        string? id = _outputs.GamePortAudioDeviceId();
        AudioDeviceInfo? info = id is null ? null : _backend.Find(id);
        if (info is null)
        {
            _log.LogWarning("Test sync: game output not found in PortAudio");
            return;
        }

        try
        {
            SyncClicks clicks = new(info.DefaultSampleRateHz, LatencyModel.SyncPeriodSec);
            _line = _backend.OpenOutput(info.Id, _outputs.Game.ChannelOffset, null, clicks.OnOutput);
            _clicks = clicks;
            _outputKey = _outputs.GameOutputKey;
            _log.LogInformation("Test sync started on {Device} (latency {Latency} ms)", info.Name, _outputs.GameLatencyMs);
        }
        catch (AudioBackendException ex)
        {
            _log.LogWarning(ex, "Test sync: could not open {Device}", info.Name);
        }

        Changed?.Invoke();
    }

    public void Stop()
    {
        if (_line is null)
        {
            return;
        }

        _line.Dispose();
        _line = null;
        _clicks = null;
        _log.LogInformation("Test sync stopped (latency {Latency} ms)", _outputs.GameLatencyMs);
        Changed?.Invoke();
    }

    private void OnConditionsChanged()
    {
        if (IsRunning && !CanRun)
        {
            Stop();
        }

        Changed?.Invoke();
    }

    // Another game device: its latency is a different setting, so the clicks move with it.
    private void OnOutputsChanged()
    {
        if (IsRunning && _outputKey != _outputs.GameOutputKey)
        {
            Stop();
            Start();
        }
    }

    public void Dispose() => _line?.Dispose();
}
